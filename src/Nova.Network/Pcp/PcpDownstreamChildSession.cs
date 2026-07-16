using System.Buffers;
using System.Globalization;
using System.IO.Pipelines;
using System.Net.Sockets;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Owns one accepted TCP connection from HTTP relay entry through PCP
/// negotiation, bounded CHAN delivery, remote control monitoring, and cleanup.
/// The fan-out remains caller-owned.
/// </summary>
public sealed class PcpDownstreamChildSession :
    IPcpRelayChildSink,
    IAsyncDisposable
{
    private const uint AcceptedCode = 1;
    private const uint GenericQuitCode = 0;
    private const string ChannelPathPrefix = "/channel/";

    private readonly object _lifecycleGate = new();
    private readonly TcpTransportConnection _connection;
    private readonly PcpRelayFanout _fanout;
    private readonly IPcpDownstreamPacketWriter _packetWriter;
    private readonly ChannelId _channelId;
    private readonly Guid _localSessionId;
    private readonly PcpDownstreamChildOptions _options;
    private readonly CancellationTokenSource _lifetimeSource = new();
    private readonly TaskCompletionSource _handshakeReady = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly byte[] _packetPrefix =
        new byte[PcpChannelPacketWireWriter.PrefixLength];

    private Task<PcpDownstreamChildResult>? _runTask;
    private PcpRelayChildRegistration? _registration;
    private CancellationToken _operationToken;
    private int _httpStatusCode;
    private int _handshakeAccepted;
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
            packetWriter: null)
    {
    }

    internal PcpDownstreamChildSession(
        TcpTransportConnection connection,
        PcpRelayFanout fanout,
        ChannelId channelId,
        Guid localSessionId,
        PcpDownstreamChildOptions? options,
        IPcpDownstreamPacketWriter? packetWriter)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(fanout);
        ArgumentNullException.ThrowIfNull(channelId);

        if (localSessionId == Guid.Empty)
        {
            throw new ArgumentException(
                "The local session identifier cannot be empty.",
                nameof(localSessionId));
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
    }

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
                await _connection.DisposeAsync().ConfigureAwait(false);
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

            await _packetWriter.WriteAsync(
                _packetPrefix.AsMemory(0, prefixLength),
                linkedSource.Token).ConfigureAwait(false);

            if (!packet.Payload.IsEmpty)
            {
                await _packetWriter.WriteAsync(
                    packet.Payload,
                    linkedSource.Token).ConfigureAwait(false);
            }
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

        var pipeReader = PipeReader.Create(
            _connection.TransportStream,
            new StreamPipeReaderOptions(
                bufferSize: _options.PipeBufferSize,
                minimumReadSize: _options.PipeMinimumReadSize,
                leaveOpen: true));

        await using var atomReader = new PcpAtomPipeMessageReader(
            pipeReader,
            _options.CreateControlLimits(),
            _options.MaxControlAtomBytes);

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
            _handshakeReady.TrySetCanceled(operationSource.Token);
            CancelBestEffort(operationSource);

            var stopped = true;
            if (_registration is not null)
            {
                stopped = await _registration.StopAsync(
                    CancellationToken.None)
                    .ConfigureAwait(false);
            }

            try
            {
                await pipeReader.CompleteAsync().ConfigureAwait(false);
            }
            finally
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
            }

            if (!stopped && _registration is not null)
            {
                cleanupCompleted = await _registration.StopAsync(
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        var snapshot = _registration?.Snapshot;
        return new PcpDownstreamChildResult(
            endReason,
            _httpStatusCode == 0 ? null : _httpStatusCode,
            Volatile.Read(ref _handshakeAccepted) != 0,
            cleanupCompleted,
            snapshot?.DeliveredPacketCount ?? 0,
            snapshot?.DeliveredByteCount ?? 0,
            snapshot?.DiscardedPacketCount ?? 0,
            snapshot?.DiscardedByteCount ?? 0);
    }

    private async Task<PcpDownstreamChildEndReason>
        NegotiateAndMonitorAsync(
            PipeReader pipeReader,
            PcpAtomPipeMessageReader atomReader,
            CancellationTokenSource operationSource)
    {
        using var handshakeTimeoutSource = new CancellationTokenSource(
            _options.HandshakeTimeout);
        using var handshakeSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                operationSource.Token,
                handshakeTimeoutSource.Token);
        var handshakeToken = handshakeSource.Token;

        PcpHttpRequestHeader request;

        try
        {
            request = await PcpHttpHeaderBlockReader.ReadRequestAsync(
                pipeReader,
                _options.MaxHttpHeaderBytes,
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

        var validation = ValidateRequest(request);
        if (validation.StatusCode is 400 or 404)
        {
            _httpStatusCode = validation.StatusCode;
            await WriteControlAsync(
                PcpDownstreamHttpResponse.CreateRejection(
                    validation.StatusCode,
                    _options.UserAgent),
                handshakeToken).ConfigureAwait(false);
            return PcpDownstreamChildEndReason.ProtocolRejected;
        }

        if (!_fanout.TryAddChild(this, out var registration))
        {
            _httpStatusCode = 503;
            await WriteControlAsync(
                PcpDownstreamHttpResponse.CreatePcp(
                    503,
                    _options.UserAgent,
                    _options.PcpVersion,
                    validation.StreamPosition),
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

        _registration = registration;
        _httpStatusCode = 200;

        await WriteControlAsync(
            PcpDownstreamHttpResponse.CreatePcp(
                200,
                _options.UserAgent,
                _options.PcpVersion,
                validation.StreamPosition),
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

        await WriteControlAsync(
            EncodeHandshakeReply(accepted: true),
            handshakeToken).ConfigureAwait(false);

        Interlocked.Exchange(ref _handshakeAccepted, 1);
        _handshakeReady.TrySetResult();
        handshakeTimeoutSource.CancelAfter(Timeout.InfiniteTimeSpan);

        var monitorTask = MonitorRemoteControlAsync(
            atomReader,
            operationSource.Token);
        var completionTask = registration.Completion;
        var cancellationTask = Task.Delay(
            Timeout.InfiniteTimeSpan,
            operationSource.Token);

        var completed = await Task.WhenAny(
            monitorTask,
            completionTask,
            cancellationTask).ConfigureAwait(false);

        if (completed == completionTask)
        {
            var result = MapRegistrationEndReason(registration.Snapshot);
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

    private static async Task<PcpDownstreamChildEndReason>
        ReadAndValidateHeloAsync(
        PcpAtomPipeMessageReader atomReader,
        CancellationToken cancellationToken)
    {
        try
        {
            var helo = await atomReader.ReadNextAsync(
                cancellationToken).ConfigureAwait(false);
            _ = PcpHeloCodec.ParseHelo(helo);
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

    private static async Task<PcpDownstreamChildEndReason>
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
                    return PcpDownstreamChildEndReason.QuitReceived;
                }
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

    private PcpDownstreamRequestValidation ValidateRequest(
        PcpHttpRequestHeader request)
    {
        if (!string.Equals(request.Method, "GET", StringComparison.Ordinal) ||
            (request.HttpVersion != new Version(1, 0) &&
                request.HttpVersion != new Version(1, 1)))
        {
            return new PcpDownstreamRequestValidation(400, 0);
        }

        if (!request.RequestTarget.StartsWith(
                ChannelPathPrefix,
                StringComparison.Ordinal) ||
            request.RequestTarget.Length !=
                ChannelPathPrefix.Length + 32 ||
            !Guid.TryParseExact(
                request.RequestTarget[ChannelPathPrefix.Length..],
                "N",
                out var requestedChannelId) ||
            requestedChannelId != _channelId.Value)
        {
            return new PcpDownstreamRequestValidation(404, 0);
        }

        if (!request.Headers.TryGetValue(
                "x-peercast-pcp",
                out var versionValue) ||
            !int.TryParse(
                versionValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var pcpVersion) ||
            pcpVersion != _options.PcpVersion)
        {
            return new PcpDownstreamRequestValidation(400, 0);
        }

        if (!request.Headers.TryGetValue(
                "x-peercast-pos",
                out var positionValue) ||
            !long.TryParse(
                positionValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var streamPosition))
        {
            return new PcpDownstreamRequestValidation(400, 0);
        }

        return new PcpDownstreamRequestValidation(200, streamPosition);
    }

    private byte[] EncodeHandshakeReply(bool accepted)
    {
        var writer = new ArrayBufferWriter<byte>();
        PcpAtomTreeCodec.Write(
            writer,
            PcpHeloCodec.CreateOleh(
                _options.UserAgent,
                _localSessionId,
                _options.ServantVersion));
        PcpAtomTreeCodec.Write(
            writer,
            PcpAtom.Leaf(
                accepted
                    ? PcpKnownAtomNames.Okay
                    : PcpKnownAtomNames.Quit,
                PcpWireValueCodec.EncodeUInt32(
                    accepted ? AcceptedCode : GenericQuitCode)));
        return writer.WrittenSpan.ToArray();
    }

    private static byte[] EncodeQuitOnly()
    {
        var writer = new ArrayBufferWriter<byte>();
        PcpAtomTreeCodec.Write(
            writer,
            PcpAtom.Leaf(
                PcpKnownAtomNames.Quit,
                PcpWireValueCodec.EncodeUInt32(GenericQuitCode)));
        return writer.WrittenSpan.ToArray();
    }

    private async ValueTask WriteControlAsync(
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(
            _options.WriteTimeout);
        using var linkedSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);

        try
        {
            await _connection.WriteAsync(
                bytes,
                linkedSource.Token).ConfigureAwait(false);
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

    private sealed record PcpDownstreamRequestValidation(
        int StatusCode,
        long StreamPosition);
}
