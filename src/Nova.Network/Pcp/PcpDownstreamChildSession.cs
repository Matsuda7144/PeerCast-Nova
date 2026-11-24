using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

// Internal writer observation boundary, used only by deterministic managed tests.
// Called on the worker send path, outside server/registration/fanout locks.
internal interface IPcpDownstreamWorkerLifetimeObserver
{
    void ObserveWorkerLifetime(CancellationToken cancellationToken);
}


/// <summary>
/// Runs one downstream PCP child from HTTP relay entry through negotiation,
/// bounded CHAN delivery, remote control monitoring, and cleanup. Standalone
/// sessions own their connection and PipeReader; routed sessions borrow both
/// from the shared listener for the duration of the run. The fan-out remains
/// caller-owned in either mode.
/// </summary>
public sealed partial class PcpDownstreamChildSession :
    IPcpRelayChildSink,
    IAsyncDisposable
{
    private const uint AcceptedCode = 1;
    private const uint GenericQuitCode = 0;

    private readonly object _lifecycleGate = new();
    private readonly TcpTransportConnection _connection;
    private readonly PcpRelayFanout _fanout;
    private readonly IPcpDownstreamPacketWriter _packetWriter;
    private readonly ChannelId _channelId;
    private readonly Guid _localSessionId;
    private readonly PcpDownstreamChildOptions _options;
    private readonly PipeReader? _borrowedPipeReader;
    private readonly PcpDownstreamRelayRequest? _preRoutedRequest;
    private readonly bool _ownsTransport;
    private readonly IPcpDownstreamRelayAdmission? _admission;
    private readonly PcpDownstreamTrafficMeter _trafficScope;
    private PcpDownstreamTrafficMeter? _traffic;
    public PcpDownstreamTrafficSnapshot? TrafficSnapshot => _traffic?.Snapshot;
    private readonly ReadOnlyMemory<byte> _initialMetadata;
    private readonly PcpRelayChildAdmission? _childAdmission;
    private readonly PcpRelayQuiescenceObserver? _quiescenceObserver;
    private readonly PcpWanIngressPolicy.Lease? _wanIngress;
    private readonly PcpPeerDirectory? _peers;
    private PcpRelayPeerIdentity? _remotePeer;
    private IDisposable? _peerLease;
    private int _packetWriteIncomplete;
    private IDisposable? _admissionReservation;
    private Task<bool> _admissionCleanup = Task.FromResult(true);
    private Task? _pingWrite;
    private readonly CancellationTokenSource _lifetimeSource = new();
    private readonly TaskCompletionSource _handshakeReady = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly byte[] _packetPrefix =
        new byte[PcpChannelPacketWireWriter.PrefixLength];

    private Task<PcpDownstreamChildResult>? _runTask;
    private PcpQuitInfo? _remoteQuit;
    private PcpRelayChildRegistration? _registration;
    private CancellationToken _operationToken;
    private int _httpStatusCode;
    private int _handshakeAccepted;
    private int _relayActive;
    internal bool IsEstablishedRelay => Volatile.Read(ref _relayActive) != 0 && _registration?.Completion.IsCompleted == false;
    private int _writeTimedOut;
    private int _writeFailed;
    private bool _disposed;

    public PcpDownstreamChildSession(
        TcpTransportConnection connection,
        PcpRelayFanout fanout,
        ChannelId channelId,
        Guid localSessionId,
        PcpDownstreamChildOptions? options = null)
        : this(
            connection,
            fanout,
            channelId,
            localSessionId,
            options,
            packetWriter: null,
            admission: null)
    {
    }

    internal PcpDownstreamChildSession(
        TcpTransportConnection connection,
        PcpRelayFanout fanout,
        ChannelId channelId,
        Guid localSessionId,
        PcpDownstreamChildOptions? options,
        IPcpDownstreamPacketWriter? packetWriter,
        IPcpDownstreamRelayAdmission? admission = null,
        PcpRelayChildAdmission? childAdmission = null,
        PcpRelayQuiescenceObserver? quiescenceObserver = null,
        ReadOnlyMemory<byte> initialMetadata = default,
        PcpWanIngressPolicy.Lease? wanIngress = null,
        PcpPeerDirectory? peers = null,
        PcpDownstreamTrafficMeter? traffic = null)
        : this(
            connection,
            fanout,
            channelId,
            localSessionId,
            options,
            packetWriter,
            borrowedPipeReader: null,
            preRoutedRequest: null,
            ownsTransport: true,
            admission, traffic: traffic)
    {
        _childAdmission = childAdmission;
        _quiescenceObserver = quiescenceObserver;
        _initialMetadata = initialMetadata;
        _wanIngress = wanIngress;
        _peers = peers;
    }

    private PcpDownstreamChildSession(
        TcpTransportConnection connection,
        PcpRelayFanout fanout,
        ChannelId channelId,
        Guid localSessionId,
        PcpDownstreamChildOptions? options,
        IPcpDownstreamPacketWriter? packetWriter,
        PipeReader? borrowedPipeReader,
        PcpDownstreamRelayRequest? preRoutedRequest,
        bool ownsTransport,
        IPcpDownstreamRelayAdmission? admission,
        PcpRelayChildAdmission? childAdmission = null,
        PcpPeerDirectory? peers = null,
        PcpWanIngressPolicy.Lease? wanIngress = null,
        ReadOnlyMemory<byte> initialMetadata = default,
        PcpDownstreamTrafficMeter? traffic = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(fanout);
        ArgumentNullException.ThrowIfNull(channelId);
        if (admission is not null && admission.ChannelId != channelId)
            throw new ArgumentException("Admission must belong to this channel.", nameof(admission));

        if (localSessionId == Guid.Empty)
        {
            throw new ArgumentException(
                "The local session identifier cannot be empty.",
                nameof(localSessionId));
        }

        if ((borrowedPipeReader is null) != (preRoutedRequest is null))
        {
            throw new ArgumentException(
                "A routed downstream child requires both a PipeReader and a routed request.");
        }

        if (ownsTransport == (borrowedPipeReader is not null))
        {
            throw new ArgumentException(
                "Transport ownership must match the downstream child input mode.",
                nameof(ownsTransport));
        }

        if (preRoutedRequest is not null &&
            preRoutedRequest.ChannelId != channelId.Value)
        {
            throw new ArgumentException(
                "The routed request channel must match the downstream child channel.",
                nameof(preRoutedRequest));
        }

        _connection = connection;
        _fanout = fanout;
        _packetWriter = packetWriter ??
            new TcpDownstreamPacketWriter(connection);
        _channelId = channelId;
        _localSessionId = localSessionId;
        _options = options is null
            ? new PcpDownstreamChildOptions()
            : options with { };
        _options.Validate();
        _borrowedPipeReader = borrowedPipeReader;
        _preRoutedRequest = preRoutedRequest;
        _ownsTransport = ownsTransport;
        _admission = admission;
        _pacing = connection.RemoteEndPoint.AddressFamily == AddressFamily.InterNetwork || connection.RemoteEndPoint.Address.IsIPv4MappedToIPv6
            ? (admission as IPcpDownstreamRelayPacing)?.Pacing : null;
        _trafficScope = traffic ?? (admission as IPcpDownstreamRelayTraffic)?.Traffic ?? new();
        _childAdmission = childAdmission;
        _peers = peers;
        _wanIngress = wanIngress;
        _initialMetadata = initialMetadata;
    }

    internal static PcpDownstreamChildSession CreateRouted(
        TcpTransportConnection connection,
        PipeReader reader,
        PcpDownstreamRelayRequest request,
        PcpRelayFanout fanout,
        Guid localSessionId,
        PcpDownstreamChildOptions? options = null,
        IPcpDownstreamPacketWriter? packetWriter = null,
        IPcpDownstreamRelayAdmission? admission = null,
        PcpRelayChildAdmission? childAdmission = null,
        PcpPeerDirectory? peers = null,
        PcpWanIngressPolicy.Lease? wanIngress = null,
        ReadOnlyMemory<byte> initialMetadata = default,
        PcpDownstreamTrafficMeter? traffic = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(request);

        return new PcpDownstreamChildSession(
            connection,
            fanout,
            new ChannelId(request.ChannelId),
            localSessionId,
            options,
            packetWriter,
            reader,
            request,
            ownsTransport: false,
            admission, childAdmission, peers, wanIngress, initialMetadata, traffic);
    }

    internal Task<bool> AdmissionCleanupCompletion => _admissionCleanup;

    public Task<PcpDownstreamChildResult> RunAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_runTask is not null)
            {
                throw new InvalidOperationException(
                    "A downstream child session can run only once.");
            }

            _traffic = _connection.Traffic.ObserveDownstream(_trafficScope);
            _runTask = RunCoreAsync(cancellationToken);
            return _runTask;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task<PcpDownstreamChildResult>? runTask;

        lock (_lifecycleGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CancelBestEffort(_lifetimeSource);
            runTask = _runTask;
        }

        try
        {
            if (runTask is null)
            {
                if (_ownsTransport)
                {
                    await _connection.DisposeAsync().ConfigureAwait(false);
                }
            }
            else
            {
                await runTask.ConfigureAwait(false);
            }
        }
        finally
        {
            _lifetimeSource.Dispose();
        }
    }

    async ValueTask IPcpRelayChildSink.SendAsync(
        PcpRelayPacket packet,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packet);

        if (_packetWriter is IPcpDownstreamWorkerLifetimeObserver observer)
            observer.ObserveWorkerLifetime(cancellationToken);
        if (_pacing is { MediaKbps: not null })
        {
            await SendPacedAsync(packet, cancellationToken).ConfigureAwait(false);
            return;
        }
        using var timeoutSource = new CancellationTokenSource(
            _options.WriteTimeout);
        using var linkedSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _operationToken,
                timeoutSource.Token);

        try
        {
            await _handshakeReady.Task.WaitAsync(
                linkedSource.Token).ConfigureAwait(false);

            var prefixLength = PcpChannelPacketWireWriter.WritePrefix(
                _packetPrefix,
                _channelId.Value,
                packet,
                _options.MaxPacketBytes);

            Volatile.Write(ref _packetWriteIncomplete, 1);
            await _packetWriter.WriteAsync(
                _packetPrefix.AsMemory(0, prefixLength),
                linkedSource.Token).ConfigureAwait(false);

            if (!packet.Payload.IsEmpty)
            {
                await _packetWriter.WriteAsync(
                    packet.Payload,
                    linkedSource.Token).ConfigureAwait(false);
            }
            Volatile.Write(ref _packetWriteIncomplete, 0);
        }
        catch (OperationCanceledException exception)
            when (timeoutSource.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested &&
                !_operationToken.IsCancellationRequested)
        {
            Interlocked.Exchange(ref _writeTimedOut, 1);
            throw new TimeoutException(
                "The bounded downstream packet write timed out.",
                exception);
        }
        catch (Exception)
            when (!cancellationToken.IsCancellationRequested &&
                !_operationToken.IsCancellationRequested)
        {
            Interlocked.Exchange(ref _writeFailed, 1);
            throw;
        }
    }

    private async Task<PcpDownstreamChildResult> RunCoreAsync(
        CancellationToken cancellationToken)
    {
        using var operationSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeSource.Token);
        _operationToken = operationSource.Token;

        var pipeReader = _borrowedPipeReader ?? PipeReader.Create(
            _connection.TransportStream,
            new StreamPipeReaderOptions(
                bufferSize: _options.PipeBufferSize,
                minimumReadSize: _options.PipeMinimumReadSize,
                leaveOpen: true));

        await using var atomReader = new PcpAtomPipeMessageReader(
            pipeReader,
            _wanIngress is null ? _options.CreateControlLimits() : new PcpProtocolLimits { MaxContentLength = 4096, MaxChildCount = 32, MaxNestingDepth = 4 },
            _wanIngress is null ? _options.MaxControlAtomBytes : 4096);

        PcpDownstreamChildEndReason endReason;
        var cleanupCompleted = true;

        try
        {
            endReason = await NegotiateAndMonitorAsync(
                pipeReader,
                atomReader,
                operationSource).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (operationSource.IsCancellationRequested)
        {
            endReason = PcpDownstreamChildEndReason.Stopped;
        }
        catch (OperationCanceledException)
        {
            endReason = PcpDownstreamChildEndReason.HandshakeTimedOut;
        }
        catch (TimeoutException)
        {
            endReason = PcpDownstreamChildEndReason.WriteTimedOut;
        }
        catch (PcpPacingLimitException)
        {
            endReason = PcpDownstreamChildEndReason.PacingLimited;
        }
        catch (PcpProtocolException exception)
            when (exception.Error == PcpParseError.UnexpectedEndOfStream)
        {
            endReason = PcpDownstreamChildEndReason.ConnectionEnded;
        }
        catch (PcpProtocolException)
        {
            endReason = PcpDownstreamChildEndReason.ProtocolRejected;
        }
        catch (EndOfStreamException)
        {
            endReason = PcpDownstreamChildEndReason.ConnectionEnded;
        }
        catch (InvalidDataException) when (_wanIngress is not null)
        {
            endReason = PcpDownstreamChildEndReason.ProtocolRejected;
        }
        catch (IOException)
        {
            endReason = PcpDownstreamChildEndReason.ConnectionEnded;
        }
        catch (SocketException)
        {
            endReason = PcpDownstreamChildEndReason.ConnectionEnded;
        }
        finally
        {
            try
            {
                Volatile.Write(ref _observationClosing, 1);
                Interlocked.Exchange(ref _relayActive, 0);
                _handshakeReady.TrySetCanceled(operationSource.Token);
                CancelBestEffort(operationSource);

                var stopped = true;
                if (_registration is not null)
                {
                    stopped = await _registration.StopAsync(
                        CancellationToken.None)
                        .ConfigureAwait(false);
                }

                if (_ownsTransport)
                {
                    try
                    {
                        await pipeReader.CompleteAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        await _connection.DisposeAsync().ConfigureAwait(false);
                    }
                }

                if (!stopped && _registration is not null)
                {
                    cleanupCompleted = await _registration.StopAsync(
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                var reservation = Interlocked.Exchange(ref _admissionReservation, null);
                if (reservation is not null)
                    _admissionCleanup = ReleaseAdmissionAfterCleanupAsync(
                        reservation, _connection.CleanupCompletion,
                        _registration?.Completion ?? Task.CompletedTask);
                if (_pingWrite is { } pingWrite)
                    _admissionCleanup = ObservePingCleanupAsync(pingWrite, _connection.CleanupCompletion);
                if (Interlocked.Exchange(ref _peerLease, null) is { } peerLease)
                    _admissionCleanup = ReleasePeerAfterCleanupAsync(
                        peerLease, _connection.CleanupCompletion, _admissionCleanup);
            }
        }

        var snapshot = _registration?.Snapshot;
        // Callback success is separate from actual worker/transport termination
        // and the existing reservation-release proof chain.
        cleanupCompleted &= snapshot?.CleanupFailed != true;
        cleanupCompleted &= _pingWrite?.IsCompleted != false;
        return new PcpDownstreamChildResult(
            endReason,
            _httpStatusCode == 0 ? null : _httpStatusCode,
            Volatile.Read(ref _handshakeAccepted) != 0,
            cleanupCompleted,
            snapshot?.DeliveredPacketCount ?? 0,
            snapshot?.DeliveredByteCount ?? 0,
            snapshot?.DiscardedPacketCount ?? 0,
            snapshot?.DiscardedByteCount ?? 0)
        {
            Quit = _remoteQuit,
            Traffic = _traffic?.Snapshot,
            TrafficFinal = _connection.CleanupCompletion.IsCompletedSuccessfully &&
                (_registration?.Completion.IsCompleted ?? true) && _pingWrite?.IsCompleted != false,
        };
    }

    private async Task<PcpDownstreamChildEndReason>
        NegotiateAndMonitorAsync(
            PipeReader pipeReader,
            PcpAtomPipeMessageReader atomReader,
            CancellationTokenSource operationSource)
    {
        using var handshakeTimeoutSource = new CancellationTokenSource(
            _wanIngress?.Remaining(_options.HandshakeTimeout) ?? _options.HandshakeTimeout,
            _wanIngress?.Clock ?? TimeProvider.System);
        using var handshakeSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                operationSource.Token,
                handshakeTimeoutSource.Token);
        var handshakeToken = handshakeSource.Token;

        if (_wanIngress is { } ingress && _preRoutedRequest is null)
        {
            using var identify = new CancellationTokenSource(ingress.Remaining(ingress.IdentificationTimeout), ingress.Clock);
            using var identifyLinked = CancellationTokenSource.CreateLinkedTokenSource(handshakeToken, identify.Token);
            if (await PcpWanProtocolIngress.IsDirectPcpAsync(pipeReader, identifyLinked.Token).ConfigureAwait(false))
                return await PcpWanProtocolIngress.RunPingAsync(atomReader, _localSessionId, ingress,
                    WritePingAsync, operationSource.Token).ConfigureAwait(false);
        }

        PcpDownstreamRelayRequest relayRequest;

        if (_preRoutedRequest is null)
        {
            PcpDownstreamRelayRequestValidation validation;

            try
            {
                validation = await PcpDownstreamRelayRequestReader.ReadAsync(
                    pipeReader,
                    _options.MaxHttpHeaderBytes,
                    _options.PcpVersion,
                    handshakeToken).ConfigureAwait(false);
            }
            catch (InvalidDataException)
            {
                _httpStatusCode = 400;
                await TryWriteControlAsync(
                    PcpDownstreamHttpResponse.CreateRejection(
                        400,
                        _options.UserAgent),
                    operationSource.Token).ConfigureAwait(false);
                return PcpDownstreamChildEndReason.ProtocolRejected;
            }

            if (!validation.IsAccepted || validation.Request is null)
            {
                _httpStatusCode = validation.StatusCode;
                await WriteControlAsync(
                    PcpDownstreamHttpResponse.CreateRejection(
                        validation.StatusCode,
                        _options.UserAgent),
                    handshakeToken).ConfigureAwait(false);
                return PcpDownstreamChildEndReason.ProtocolRejected;
            }

            relayRequest = validation.Request;
        }
        else
        {
            relayRequest = _preRoutedRequest;
        }

        if (relayRequest.ChannelId != _channelId.Value)
        {
            _httpStatusCode = 404;
            await WriteControlAsync(
                PcpDownstreamHttpResponse.CreateRejection(
                    404,
                    _options.UserAgent),
                handshakeToken).ConfigureAwait(false);
            return PcpDownstreamChildEndReason.ProtocolRejected;
        }

        // Request format and ChannelId are already validated. A remote loopback
        // address is still a PCP relay role, not an internal media consumer.
        Volatile.Write(ref _identifiedRelay, 1);
        if (_childAdmission?.IsClosed == true)
            return await RejectClosedAdmissionAsync(handshakeToken).ConfigureAwait(false);
        if (_quiescenceObserver is not null)
            await _quiescenceObserver.ObserveAsync(PcpRelayQuiescencePoint.BeforeReserve).ConfigureAwait(false);
        var policyAccepted = _admission is null ||
            _admission.TryReserve(_connection.ConnectionId, out _admissionReservation, handshakeToken);
        if (policyAccepted && _quiescenceObserver is not null)
            await _quiescenceObserver.ObserveAsync(PcpRelayQuiescencePoint.AfterReserve).ConfigureAwait(false);
        PcpRelayChildRegistration? registration = null;
        var registered = policyAccepted && (_childAdmission is null
            ? _fanout.TryAddChild(this, out registration)
            : _childAdmission.TryAddChild(_fanout, this, out registration));
        if (!registered)
        {
            if (_childAdmission?.IsClosed == true)
                return await RejectClosedAdmissionAsync(handshakeToken).ConfigureAwait(false);
            _httpStatusCode = 503;
            await WriteControlAsync(
                PcpDownstreamHttpResponse.CreatePcp(
                    503,
                    _options.UserAgent,
                    _options.PcpVersion,
                    relayRequest.StreamPosition),
                handshakeToken).ConfigureAwait(false);

            var heloResult = await ReadAndValidateHeloAsync(
                atomReader,
                handshakeToken).ConfigureAwait(false);
            if (heloResult != PcpDownstreamChildEndReason.Stopped)
            {
                return heloResult;
            }

            await WriteControlAsync(
                EncodeHandshakeReply(accepted: false),
                handshakeToken).ConfigureAwait(false);
            return PcpDownstreamChildEndReason.RelayFull;
        }

        _registration = registration ?? throw new InvalidOperationException("Missing admitted relay child.");
        _httpStatusCode = 200;

        await WriteControlAsync(
            PcpDownstreamHttpResponse.CreatePcp(
                200,
                _options.UserAgent,
                _options.PcpVersion,
                relayRequest.StreamPosition),
            handshakeToken).ConfigureAwait(false);

        var heloEndReason = await ReadAndValidateHeloAsync(
            atomReader,
            handshakeToken).ConfigureAwait(false);
        if (heloEndReason != PcpDownstreamChildEndReason.Stopped)
        {
            if (heloEndReason ==
                PcpDownstreamChildEndReason.ProtocolRejected)
            {
                await TryWriteControlAsync(
                    EncodeQuitOnly(),
                    operationSource.Token).ConfigureAwait(false);
            }

            return heloEndReason;
        }

        if (_peers is not null && _remotePeer is not null)
            _peerLease = _peers.TrackDownstream(_connection.ConnectionId, _remotePeer);
        await WriteControlAsync(
            EncodeHandshakeReply(accepted: true),
            handshakeToken).ConfigureAwait(false);

        // Media workers wait on _handshakeReady. Complete the immutable
        // generation's control information first, using existing write deadlines.
        if (!_initialMetadata.IsEmpty)
            await WriteControlAsync(_initialMetadata, handshakeToken, _packetWriter).ConfigureAwait(false);

        using var trafficObservation = _connection.ObservePcpSession();
        Interlocked.Exchange(ref _handshakeAccepted, 1);
        Interlocked.Exchange(ref _relayActive, 1);
        _wanIngress?.Handshaken();
        _handshakeReady.TrySetResult();
        handshakeTimeoutSource.CancelAfter(Timeout.InfiniteTimeSpan);

        var monitorTask = MonitorRemoteControlAsync(
            atomReader,
            operationSource.Token);
        var completionTask = _registration.Completion;
        var cancellationTask = Task.Delay(
            Timeout.InfiniteTimeSpan,
            operationSource.Token);

        var completed = await Task.WhenAny(
            monitorTask,
            completionTask,
            cancellationTask).ConfigureAwait(false);

        if (completed == completionTask)
        {
            if (_registration.Snapshot.EndReason == PcpRelayChildEndReason.UpstreamChanged &&
                Volatile.Read(ref _packetWriteIncomplete) == 0)
            {
                // Never append a control atom to a canceled partial media atom.
                // In that case transport close is the only safe terminal signal.
                var quit = new ArrayBufferWriter<byte>();
                PcpAtomTreeCodec.Write(quit, PcpQuitCodec.Create(PcpQuitCodec.UnavailableCode));
                await TryWriteControlAsync(quit.WrittenMemory, operationSource.Token).ConfigureAwait(false);
            }
            var result = MapRegistrationEndReason(_registration.Snapshot);
            CancelBestEffort(operationSource);
            await IgnoreExpectedCancellationAsync(monitorTask)
                .ConfigureAwait(false);
            return result;
        }

        if (completed == monitorTask)
        {
            var result = await monitorTask.ConfigureAwait(false);
            CancelBestEffort(operationSource);
            return result;
        }

        operationSource.Token.ThrowIfCancellationRequested();
        throw new InvalidOperationException(
            "The downstream cancellation task completed unexpectedly.");
    }

    private async Task<PcpDownstreamChildEndReason> RejectClosedAdmissionAsync(CancellationToken cancellationToken)
    {
        _httpStatusCode = 503;
        if (_quiescenceObserver is not null)
            await _quiescenceObserver.ObserveAsync(PcpRelayQuiescencePoint.BeforeClosedResponse).ConfigureAwait(false);
        await WriteControlAsync(PcpDownstreamHttpResponse.CreateRejection(503, _options.UserAgent),
            cancellationToken).ConfigureAwait(false);
        return PcpDownstreamChildEndReason.AdmissionClosed;
    }

    internal static async Task<bool> ReleaseAdmissionAfterCleanupAsync(
        IDisposable reservation, Task transportCleanup, Task childCompletion)
    {
        try { await transportCleanup.ConfigureAwait(false); }
        catch
        {
            // Keep the occupied slot if closing the actual transport is unproven.
            return false;
        }
        try { await childCompletion.ConfigureAwait(false); }
        catch
        {
            // A terminal worker fault still means its callback has returned.
        }
        reservation.Dispose();
        return true;
    }

    private async ValueTask WritePingAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        _pingWrite = WriteControlAsync(bytes, token, _packetWriter).AsTask();
        await _pingWrite.WaitAsync(token).ConfigureAwait(false);
    }
    private static async Task<bool> ObservePingCleanupAsync(Task write, Task transport)
    {
        try { await transport.ConfigureAwait(false); }
        catch { return false; }
        try { await write.ConfigureAwait(false); }
        catch { /* An ended faulted write is distinct from a retained worker. */ }
        return true;
    }

    private async Task<PcpDownstreamChildEndReason>
        ReadAndValidateHeloAsync(
        PcpAtomPipeMessageReader atomReader,
        CancellationToken cancellationToken)
    {
        try
        {
            var helo = await atomReader.ReadNextAsync(
                cancellationToken).ConfigureAwait(false);
            var peer = PcpHeloCodec.ParseHelo(helo);
            if (_wanIngress is not null && peer.SessionId == Guid.Empty)
                return PcpDownstreamChildEndReason.ProtocolRejected;
            _remotePeer = new(peer.SessionId,
                new(_connection.RemoteEndPoint.Address, _connection.RemoteEndPoint.Port),
                peer.OpenPort is > 0 ? new(_connection.RemoteEndPoint.Address, peer.OpenPort.Value) : null);
            return PcpDownstreamChildEndReason.Stopped;
        }
        catch (PcpProtocolException exception)
            when (exception.Error == PcpParseError.UnexpectedEndOfStream)
        {
            return PcpDownstreamChildEndReason.ConnectionEnded;
        }
        catch (PcpProtocolException)
        {
            return PcpDownstreamChildEndReason.ProtocolRejected;
        }
        catch (InvalidDataException)
        {
            return PcpDownstreamChildEndReason.ProtocolRejected;
        }
    }

    private async Task<PcpDownstreamChildEndReason>
        MonitorRemoteControlAsync(
            PcpAtomPipeMessageReader atomReader,
            CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                var atom = await atomReader.ReadNextAsync(
                    cancellationToken).ConfigureAwait(false);

                if (atom.Name == PcpKnownAtomNames.Quit)
                {
                    _remoteQuit = PcpQuitCodec.Parse(atom);
                    return PcpDownstreamChildEndReason.QuitReceived;
                }
                if (CanUsePeers && atom.Name == PcpKnownAtomNames.Host &&
                    PcpRelayHostCandidateParser.TryParse(atom) is { } host)
                    _peers!.Observe(host, new(PcpPeerSourceKind.DownstreamControl, _connection.ConnectionId));
                if (CanUsePeers && atom.Name == PcpBroadcastHostReader.Broadcast)
                    foreach (var reported in PcpBroadcastHostReader.Read(atom, _peers!.ChannelId, _localSessionId))
                        _peers.ObserveReport(reported, new(PcpPeerSourceKind.DownstreamControl, _connection.ConnectionId));
            }
            catch (InvalidDataException)
            {
                return PcpDownstreamChildEndReason.ProtocolRejected;
            }
            catch (PcpProtocolException exception)
                when (exception.Error ==
                    PcpParseError.UnexpectedEndOfStream)
            {
                return PcpDownstreamChildEndReason.ConnectionEnded;
            }
            catch (PcpProtocolException)
            {
                return PcpDownstreamChildEndReason.ProtocolRejected;
            }
        }
    }

    private static async Task<bool> ReleasePeerAfterCleanupAsync(
        IDisposable lease, Task transportCleanup, Task<bool> previousCleanup)
    {
        try
        {
            await transportCleanup.ConfigureAwait(false);
            if (!await previousCleanup.ConfigureAwait(false)) return false;
            lease.Dispose();
            return true;
        }
        catch { return false; }
    }

    private bool CanUsePeers => _peers?.IsActive == true &&
        !_operationToken.IsCancellationRequested && _childAdmission?.IsClosed != true &&
        (_admission is null || _admission is IPcpDownstreamRelayCapacity { IsCurrent: true });

    private byte[] EncodeHandshakeReply(bool accepted)
    {
        var writer = new ArrayBufferWriter<byte>();
        var openPort = _remotePeer?.ListeningEndpoint?.Port is > 0 and <= ushort.MaxValue
            ? (ushort?)_remotePeer.ListeningEndpoint.Port
            : null;
        PcpAtomTreeCodec.Write(
            writer,
            PcpHeloCodec.CreateOleh(
                _options.UserAgent,
                _localSessionId,
                _options.ServantVersion,
                remoteIp: _connection.RemoteEndPoint.Address,
                openPort: openPort));
        if (!accepted && CanUsePeers && _remotePeer is not null)
            foreach (var peer in PcpReferralPlanner.Select(_peers!, _remotePeer))
                PcpAtomTreeCodec.Write(writer, PcpReferralPlanner.CreateHost(peer));
        PcpAtomTreeCodec.Write(
            writer,
            accepted
                ? PcpAtom.Leaf(PcpKnownAtomNames.Okay, PcpWireValueCodec.EncodeUInt32(AcceptedCode))
                : PcpQuitCodec.Create(PcpQuitCodec.UnavailableCode));
        return writer.WrittenSpan.ToArray();
    }

    private static byte[] EncodeQuitOnly()
    {
        var writer = new ArrayBufferWriter<byte>();
        PcpAtomTreeCodec.Write(
            writer,
            PcpQuitCodec.Create(GenericQuitCode));
        return writer.WrittenSpan.ToArray();
    }

    private async ValueTask WriteControlAsync(
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken,
        IPcpDownstreamPacketWriter? writer = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _pacing?.ConsumeControl(bytes.Length);
        using var timeoutSource = new CancellationTokenSource(
            _options.WriteTimeout, _pacing?.Clock ?? TimeProvider.System);
        using var linkedSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);

        try
        {
            if (writer is null)
                await _connection.WriteAsync(bytes, linkedSource.Token).ConfigureAwait(false);
            else
                await writer.WriteAsync(bytes, linkedSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (timeoutSource.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                "The bounded downstream control write timed out.",
                exception);
        }
    }

    private async ValueTask TryWriteControlAsync(
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        try
        {
            await WriteControlAsync(bytes, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is
            OperationCanceledException or
            TimeoutException or
            IOException or
            SocketException or
            ObjectDisposedException)
        {
            // A diagnostic rejection is best effort and remains bounded.
        }
    }

    private PcpDownstreamChildEndReason MapRegistrationEndReason(
        PcpRelayChildStatusSnapshot snapshot)
    {
        return snapshot.EndReason switch
        {
            PcpRelayChildEndReason.FanoutStopped =>
                PcpDownstreamChildEndReason.FanoutStopped,
            PcpRelayChildEndReason.SlowConsumer =>
                PcpDownstreamChildEndReason.SlowConsumer,
            PcpRelayChildEndReason.PacingLimited => PcpDownstreamChildEndReason.PacingLimited,
            PcpRelayChildEndReason.SinkFailed when Volatile.Read(ref _pacingLimited) != 0 =>
                PcpDownstreamChildEndReason.PacingLimited,
            PcpRelayChildEndReason.SinkFailed
                when Volatile.Read(ref _writeTimedOut) != 0 =>
                    PcpDownstreamChildEndReason.WriteTimedOut,
            PcpRelayChildEndReason.SinkFailed
                when Volatile.Read(ref _writeFailed) != 0 =>
                    PcpDownstreamChildEndReason.WriteFailed,
            PcpRelayChildEndReason.SinkFailed =>
                PcpDownstreamChildEndReason.WriteFailed,
            _ => PcpDownstreamChildEndReason.Stopped,
        };
    }

    private static async Task IgnoreExpectedCancellationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void CancelBestEffort(
        CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
        }
        catch (AggregateException)
        {
            // A cancellation callback cannot change session ownership.
        }
    }
}
