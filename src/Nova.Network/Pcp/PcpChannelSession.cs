using System.Collections.Immutable;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Owns one stable local channel session across a finite number of upstream
/// TCP reconnects. It retains the same Core identity, position adapter, packet
/// sequence, and consumer while each connection and PipeReader are replaced.
/// </summary>
public sealed class PcpChannelSession : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly IPcpChannelTransportConnector _connector;
    private readonly ChannelStatusTracker _statusTracker;
    private readonly PcpChannelStateAdapter _stateAdapter;
    private readonly IPcpChannelPacketConsumer _packetConsumer;
    private readonly PcpChannelSessionOptions _options;
    private readonly PcpPeerDirectory? _peers;
    private readonly CancellationTokenSource _stopSource = new();
    private readonly TaskCompletionSource<PcpChannelSessionNegotiationResult>
        _negotiation = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Task<PcpChannelSessionResult>? _runTask;
    private int _disposed;

    public PcpChannelSession(
        TcpTransportConnector connector,
        ChannelStatusTracker statusTracker,
        IPcpChannelPacketConsumer packetConsumer,
        PcpChannelSessionOptions? options = null)
        : this(
            new TcpTransportConnectorAdapter(connector),
            statusTracker,
            packetConsumer,
            options)
    {
    }

    internal PcpChannelSession(
        IPcpChannelTransportConnector connector,
        ChannelStatusTracker statusTracker,
        IPcpChannelPacketConsumer packetConsumer,
        PcpChannelSessionOptions? options = null,
        PcpPeerDirectory? peers = null)
    {
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(statusTracker);
        ArgumentNullException.ThrowIfNull(packetConsumer);

        if (statusTracker.Snapshot.State != ChannelSessionState.Created)
        {
            throw new ArgumentException(
                "A channel session must receive a tracker in the Created state.",
                nameof(statusTracker));
        }

        var sourceOptions = options ?? new PcpChannelSessionOptions();
        ArgumentNullException.ThrowIfNull(sourceOptions.Stream);

        _options = sourceOptions with
        {
            Stream = sourceOptions.Stream with { },
        };
        _options.Validate();

        _connector = connector;
        _statusTracker = statusTracker;
        _stateAdapter = new PcpChannelStateAdapter(statusTracker);
        _packetConsumer = packetConsumer;
        if (peers is not null && (peers.ChannelId != statusTracker.Snapshot.ChannelId.Value ||
            peers.LocalSessionId != statusTracker.Snapshot.SessionId.Value))
            throw new ArgumentException("Peer directory must belong to this channel owner.", nameof(peers));
        _peers = peers;
    }

    /// <summary>
    /// Completes after the first accepted handshake enters Receiving, or after
    /// a terminal handshake rejection is recorded. It remains accepted across
    /// later reconnects. A stop, caller cancellation, or failure before that
    /// point cancels or faults the task respectively.
    /// </summary>
    public Task<PcpChannelSessionNegotiationResult> Negotiation =>
        _negotiation.Task;

    public Task<PcpChannelSessionResult> RunAsync(
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            host,
            port,
            initialStreamPosition: 0,
            cancellationToken);
    }

    public Task<PcpChannelSessionResult> RunAsync(
        string host,
        int port,
        long initialStreamPosition,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        if (port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            throw new ArgumentOutOfRangeException(
                nameof(port),
                port,
                "The TCP port is outside the valid range.");
        }

        if (initialStreamPosition < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialStreamPosition),
                initialStreamPosition,
                "The initial stream position cannot be negative.");
        }

        lock (_gate)
        {
            if (_stopSource.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    "The channel session has already been stopped.");
            }

            if (_runTask is not null)
            {
                throw new InvalidOperationException(
                    "A channel session instance can only be run once.");
            }

            _runTask = RunCoreAsync(
                host,
                port,
                initialStreamPosition,
                cancellationToken);
            return _runTask;
        }
    }

    /// <summary>
    /// Requests cooperative session shutdown without waiting. This is safe for
    /// a packet consumer to call from inside its callback.
    /// </summary>
    public void RequestStop()
    {
        ThrowIfDisposed();
        _stopSource.Cancel();
        _negotiation.TrySetCanceled(_stopSource.Token);
    }

    public async Task StopAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        Task<PcpChannelSessionResult>? runTask;

        lock (_gate)
        {
            _stopSource.Cancel();
            runTask = _runTask;
        }

        _negotiation.TrySetCanceled(_stopSource.Token);

        if (runTask is null)
        {
            TransitionToStopped();
            return;
        }

        try
        {
            await runTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (runTask.IsCompleted)
            {
                TransitionToStopped();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Task<PcpChannelSessionResult>? runTask;

        lock (_gate)
        {
            _stopSource.Cancel();
            runTask = _runTask;
        }

        _negotiation.TrySetCanceled(_stopSource.Token);

        try
        {
            if (runTask is null)
            {
                TransitionToStopped();
            }
            else if (!runTask.IsCompleted)
            {
                await runTask.ConfigureAwait(false);
            }
            else if (runTask.IsFaulted)
            {
                _ = runTask.Exception;
            }
        }
        finally
        {
            TransitionToStopped();
            _stopSource.Dispose();
        }
    }

    private async Task<PcpChannelSessionResult> RunCoreAsync(
        string host,
        int port,
        long initialStreamPosition,
        CancellationToken callerCancellationToken)
    {
        using var linkedSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                callerCancellationToken,
                _stopSource.Token);
        var token = linkedSource.Token;
        var progress = new SessionProgress();
        var countingConsumer = new CountingPacketConsumer(_packetConsumer);
        var attemptCount = 0;
        var reconnectCount = 0;
        var redirectAttemptCount = 0;
        var attemptedEndpoints = new HashSet<PcpRelayHostEndpoint>();
        var startupRedirectPlan = ImmutableArray<PcpRelayRedirectPlanEntry>.Empty;
        var startupRedirectPlanIndex = 0;
        var startupRedirectActive = false;
        var hasEverAccepted = false;
        PcpRelayHandshakeResult? originalRelayFull = null;
        var attemptTarget = new AttemptTarget(host, port, RedirectEndpoint: null);

        try
        {
            _statusTracker.TransitionTo(ChannelSessionState.Connecting);

            while (true)
            {
                token.ThrowIfCancellationRequested();
                attemptCount = checked(attemptCount + 1);
                var attempt = await RunAttemptAsync(
                    attemptTarget.Host,
                    attemptTarget.Port,
                    attemptTarget.RedirectEndpoint,
                    initialStreamPosition,
                    countingConsumer,
                    progress,
                    token).ConfigureAwait(false);

                var observedEndpoint = attemptTarget.RedirectEndpoint is { } redirected
                    ? new PcpRelayHostEndpoint(redirected.Address, redirected.Port) : attempt.ConnectedEndpoint;
                if (observedEndpoint is not null)
                    _peers?.RecordConnectionResult(observedEndpoint,
                        attempt.RetryableFailure is null && attempt.Handshake?.Outcome == PcpCompatibilityOutcome.Accepted);

                if (attempt.ReachedAccepted)
                {
                    hasEverAccepted = true;
                    startupRedirectActive = false;
                    startupRedirectPlan = ImmutableArray<PcpRelayRedirectPlanEntry>.Empty;
                    originalRelayFull = null;
                }

                if (attempt.RetryableFailure is not null)
                {
                    if (!hasEverAccepted && startupRedirectActive)
                    {
                        if (TrySelectNextRedirectTarget(
                                startupRedirectPlan,
                                ref startupRedirectPlanIndex,
                                ref redirectAttemptCount,
                                attemptedEndpoints) is { } nextTarget)
                        {
                            attemptTarget = nextTarget;
                            continue;
                        }

                        var originalHandshake = originalRelayFull ??
                            throw new InvalidOperationException(
                                "A startup redirect has no original RelayFull handshake.");
                        return CompleteOriginalRelayFull(
                            originalHandshake,
                            attemptCount,
                            reconnectCount,
                            progress,
                            countingConsumer);
                    }

                    if (CanReconnect(reconnectCount))
                    {
                        reconnectCount = checked(reconnectCount + 1);
                        await PrepareReconnectAsync(token).ConfigureAwait(false);
                        continue;
                    }

                    var failureStage = attempt.FailureStage ??
                        throw new InvalidOperationException(
                            "A retryable channel failure has no stage.");
                    RecordFault(CreateFaultSnapshot(
                        attempt.RetryableFailure,
                        failureStage,
                        attemptCount,
                        reconnectCount));
                    throw attempt.RetryableFailure;
                }

                var handshake = attempt.Handshake ??
                    throw new InvalidOperationException(
                        "A completed channel attempt has no handshake result.");

                if (handshake.Outcome != PcpCompatibilityOutcome.Accepted)
                {
                    // Only capacity refusals advance the startup referral plan.
                    // An explicit protocol/auth rejection must retain its own
                    // terminal reason instead of becoming the original RelayFull.
                    if (!hasEverAccepted && startupRedirectActive &&
                        handshake.Outcome == PcpCompatibilityOutcome.RelayFull)
                    {
                        if (TrySelectNextRedirectTarget(
                                startupRedirectPlan,
                                ref startupRedirectPlanIndex,
                                ref redirectAttemptCount,
                                attemptedEndpoints) is { } nextTarget)
                        {
                            attemptTarget = nextTarget;
                            continue;
                        }

                        var originalHandshake = originalRelayFull ??
                            throw new InvalidOperationException(
                                "A startup redirect has no original RelayFull handshake.");
                        return CompleteOriginalRelayFull(
                            originalHandshake,
                            attemptCount,
                            reconnectCount,
                            progress,
                            countingConsumer);
                    }

                    if (!hasEverAccepted &&
                        handshake.Outcome == PcpCompatibilityOutcome.RelayFull)
                    {
                        originalRelayFull = handshake;
                        if (attempt.ConnectedEndpoint is { } originalEndpoint)
                        {
                            attemptedEndpoints.Add(originalEndpoint);
                        }

                        startupRedirectPlan =
                            PcpRelayRedirectPlanner.CreatePlan(
                                handshake.RelayHostCandidates.Where(h =>
                                    (h.ChannelId is null || h.ChannelId == _statusTracker.Snapshot.ChannelId.Value) &&
                                    h.PeerSessionId != _statusTracker.Snapshot.SessionId.Value &&
                                    _peers?.IsKnownLoop(h) != true).ToImmutableArray(),
                                attemptedEndpoints);
                        startupRedirectPlanIndex = 0;
                        startupRedirectActive = true;

                        if (TrySelectNextRedirectTarget(
                                startupRedirectPlan,
                                ref startupRedirectPlanIndex,
                                ref redirectAttemptCount,
                                attemptedEndpoints) is { } nextTarget)
                        {
                            attemptTarget = nextTarget;
                            continue;
                        }

                        return CompleteOriginalRelayFull(
                            handshake,
                            attemptCount,
                            reconnectCount,
                            progress,
                            countingConsumer);
                    }

                    RecordFault(CreateHandshakeFaultSnapshot(
                        handshake.Outcome,
                        attemptCount,
                        reconnectCount));
                    _negotiation.TrySetResult(new(
                        PcpChannelSessionNegotiationOutcome.Rejected,
                        handshake.HttpStatusCode,
                        handshake.Outcome));
                    return CreateTerminalHandshakeResult(
                        handshake,
                        attemptCount,
                        reconnectCount,
                        progress,
                        countingConsumer);
                }

                var stream = attempt.Stream ??
                    throw new InvalidOperationException(
                        "An accepted channel attempt has no stream result.");

                if (stream.EndReason == PcpChannelStreamEndReason.QuitReceived)
                {
                    RecordFault(new ChannelFaultSnapshot(
                        ChannelFaultCategory.RemoteEnded,
                        ChannelFaultStage.Receiving,
                        attemptCount,
                        reconnectCount));
                    return CreateResult(
                        PcpChannelSessionEndReason.QuitReceived,
                        attemptCount,
                        reconnectCount,
                        handshake.HttpStatusCode,
                        progress.ChannelMessageCount,
                        countingConsumer) with
                    { Quit = stream.Quit };
                }

                if (CanReconnect(reconnectCount))
                {
                    reconnectCount = checked(reconnectCount + 1);
                    await PrepareReconnectAsync(token).ConfigureAwait(false);
                    continue;
                }

                RecordFault(new ChannelFaultSnapshot(
                    ChannelFaultCategory.RemoteEnded,
                    ChannelFaultStage.Receiving,
                    attemptCount,
                    reconnectCount));
                return CreateResult(
                    PcpChannelSessionEndReason.ConnectionEnded,
                    attemptCount,
                    reconnectCount,
                    handshake.HttpStatusCode,
                    progress.ChannelMessageCount,
                    countingConsumer);
            }
        }
        catch (OperationCanceledException)
            when (_stopSource.IsCancellationRequested)
        {
            _negotiation.TrySetCanceled(_stopSource.Token);
            TransitionToStopped();
            return CreateResult(
                PcpChannelSessionEndReason.Stopped,
                attemptCount,
                reconnectCount,
                progress.LastHttpStatusCode,
                progress.ChannelMessageCount,
                countingConsumer);
        }
        catch (OperationCanceledException)
            when (callerCancellationToken.IsCancellationRequested)
        {
            _negotiation.TrySetCanceled(callerCancellationToken);
            TransitionToStopped();
            throw;
        }
        catch (Exception exception)
        {
            if (_statusTracker.Snapshot.State is not (
                ChannelSessionState.Faulted or
                ChannelSessionState.Stopping or
                ChannelSessionState.Stopped))
            {
                RecordFault(CreateFaultSnapshot(
                    exception,
                    InferFaultStage(_statusTracker.Snapshot.State),
                    attemptCount,
                    reconnectCount));
            }

            _negotiation.TrySetException(exception);

            throw;
        }
    }

    private async Task<AttemptResult> RunAttemptAsync(
        string host,
        int port,
        IPEndPoint? redirectEndpoint,
        long initialStreamPosition,
        CountingPacketConsumer countingConsumer,
        SessionProgress progress,
        CancellationToken cancellationToken)
    {
        TcpTransportConnection connection;

        try
        {
            connection = redirectEndpoint is null
                ? await _connector.ConnectOriginalAsync(
                    host,
                    port,
                    cancellationToken).ConfigureAwait(false)
                : await _connector.ConnectRedirectAsync(
                    redirectEndpoint,
                    cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            return AttemptResult.FromRetryableFailure(
                exception,
                ChannelFaultStage.Connecting);
        }
        catch (SocketException exception)
        {
            return AttemptResult.FromRetryableFailure(
                exception,
                ChannelFaultStage.Connecting);
        }
        catch (IOException exception)
        {
            return AttemptResult.FromRetryableFailure(
                exception,
                ChannelFaultStage.Connecting);
        }

        var connectedEndpoint = new PcpRelayHostEndpoint(
            connection.RemoteEndPoint.Address,
            connection.RemoteEndPoint.Port);

        await using (connection.ConfigureAwait(false))
        {
            var pipeReader = PipeReader.Create(
                connection.TransportStream,
                new StreamPipeReaderOptions(
                    bufferSize: _options.StreamPipeBufferSize,
                    minimumReadSize: _options.StreamPipeMinimumReadSize,
                    leaveOpen: true));
            Exception? failure = null;

            try
            {
                _statusTracker.TransitionTo(ChannelSessionState.Negotiating);

                PcpRelayHandshakeResult handshake;

                try
                {
                    handshake = await PerformHandshakeAsync(
                        connection,
                        pipeReader,
                        host,
                        port,
                        _statusTracker.Snapshot.StreamPosition ??
                            initialStreamPosition,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException exception)
                {
                    return AttemptResult.FromRetryableFailure(
                        exception,
                        ChannelFaultStage.Negotiating);
                }
                catch (IOException exception)
                {
                    return AttemptResult.FromRetryableFailure(
                        exception,
                        ChannelFaultStage.Negotiating);
                }
                catch (PcpProtocolException exception)
                    when (exception.Error ==
                        PcpParseError.UnexpectedEndOfStream)
                {
                    return AttemptResult.FromRetryableFailure(
                        exception,
                        ChannelFaultStage.Negotiating);
                }

                progress.ObserveHttpStatus(handshake.HttpStatusCode);

                if (handshake.Outcome != PcpCompatibilityOutcome.Accepted)
                {
                    return new AttemptResult(
                        handshake,
                        Stream: null,
                        RetryableFailure: null,
                        FailureStage: null,
                        ConnectedEndpoint: connectedEndpoint);
                }

                cancellationToken.ThrowIfCancellationRequested();
                _peers?.RecordConnectionResult(redirectEndpoint is null ? connectedEndpoint :
                    new(redirectEndpoint.Address, redirectEndpoint.Port), succeeded: true);
                using var trafficObservation = connection.ObservePcpSession();
                _statusTracker.TransitionTo(ChannelSessionState.Receiving);
                _negotiation.TrySetResult(new(
                    PcpChannelSessionNegotiationOutcome.Accepted,
                    handshake.HttpStatusCode,
                    handshake.Outcome));

                var streamReader = new PcpChannelStreamReader(
                    pipeReader,
                    _stateAdapter,
                    countingConsumer,
                    _options.Stream,
                    completeReader: false,
                    initialPacketSequence: countingConsumer.PacketCount,
                    progress.ObserveChannelMessage,
                    hostObserved: _peers is null ? null : host => _peers.Observe(host,
                        new(PcpPeerSourceKind.UpstreamStream, connection.ConnectionId)));

                try
                {
                    var stream = await streamReader.ReadToEndAsync(
                        cancellationToken).ConfigureAwait(false);

                    return new AttemptResult(
                        handshake,
                        stream,
                        RetryableFailure: null,
                        FailureStage: null,
                        ConnectedEndpoint: connectedEndpoint,
                        ReachedAccepted: true);
                }
                catch (TimeoutException exception)
                {
                    return AttemptResult.FromRetryableFailure(
                        exception,
                        ChannelFaultStage.Receiving,
                        connectedEndpoint,
                        reachedAccepted: true);
                }
                catch (IOException exception)
                {
                    return AttemptResult.FromRetryableFailure(
                        exception,
                        ChannelFaultStage.Receiving,
                        connectedEndpoint,
                        reachedAccepted: true);
                }
                catch (PcpProtocolException exception)
                    when (exception.Error ==
                        PcpParseError.UnexpectedEndOfStream)
                {
                    return AttemptResult.FromRetryableFailure(
                        exception,
                        ChannelFaultStage.Receiving,
                        connectedEndpoint,
                        reachedAccepted: true);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
                throw;
            }
            finally
            {
                try
                {
                    await pipeReader.CompleteAsync(failure).ConfigureAwait(false);
                }
                catch when (failure is not null)
                {
                    // Preserve the primary session failure.
                }
            }
        }
    }

    private async ValueTask<PcpRelayHandshakeResult> PerformHandshakeAsync(
        TcpTransportConnection connection,
        PipeReader pipeReader,
        string host,
        int port,
        long streamPosition,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(
            _options.HandshakeTimeout);
        using var linkedSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);

        try
        {
            return await PcpRelayHandshakeClient.NegotiateAsync(
                connection,
                pipeReader,
                host,
                port,
                _statusTracker.Snapshot.ChannelId.Value,
                _statusTracker.Snapshot.SessionId.Value,
                streamPosition,
                _options.PcpVersion,
                _options.CreateHandshakeSettings(),
                linkedSource.Token,
                hostObserved: _peers is null ? null : host => _peers.Observe(host,
                    new(PcpPeerSourceKind.UpstreamHandshake, connection.ConnectionId))).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (timeoutSource.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The PCP relay handshake exceeded {_options.HandshakeTimeout}.",
                exception);
        }
    }

    private AttemptTarget? TrySelectNextRedirectTarget(
        ImmutableArray<PcpRelayRedirectPlanEntry> plan,
        ref int planIndex,
        ref int redirectAttemptCount,
        HashSet<PcpRelayHostEndpoint> attemptedEndpoints)
    {
        while (planIndex < plan.Length)
        {
            var entry = plan[planIndex++];
            var eligibility = PcpRelayRedirectPolicy.Evaluate(
                entry,
                redirectAttemptCount);
            if (!eligibility.IsEligible ||
                !attemptedEndpoints.Add(entry.Endpoint))
            {
                continue;
            }

            redirectAttemptCount = checked(redirectAttemptCount + 1);
            _statusTracker.TransitionTo(ChannelSessionState.Reconnecting);

            var endpoint = new IPEndPoint(
                entry.Endpoint.Address,
                entry.Endpoint.Port);
            return new AttemptTarget(
                entry.Endpoint.Address.ToString(),
                entry.Endpoint.Port,
                endpoint);
        }

        return null;
    }

    private PcpChannelSessionResult CompleteOriginalRelayFull(
        PcpRelayHandshakeResult handshake,
        int attemptCount,
        int reconnectCount,
        SessionProgress progress,
        CountingPacketConsumer countingConsumer)
    {
        RecordFault(CreateHandshakeFaultSnapshot(
            handshake.Outcome,
            attemptCount,
            reconnectCount));
        _negotiation.TrySetResult(new(
            PcpChannelSessionNegotiationOutcome.Rejected,
            handshake.HttpStatusCode,
            handshake.Outcome));
        return CreateTerminalHandshakeResult(
            handshake,
            attemptCount,
            reconnectCount,
            progress,
            countingConsumer);
    }

    private bool CanReconnect(int reconnectCount)
    {
        return reconnectCount < _options.MaxReconnectAttempts;
    }

    private async Task PrepareReconnectAsync(
        CancellationToken cancellationToken)
    {
        _statusTracker.TransitionTo(ChannelSessionState.Reconnecting);

        if (_options.ReconnectDelay > TimeSpan.Zero)
        {
            await Task.Delay(
                _options.ReconnectDelay,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static PcpChannelSessionResult CreateTerminalHandshakeResult(
        PcpRelayHandshakeResult handshake,
        int attemptCount,
        int reconnectCount,
        SessionProgress progress,
        CountingPacketConsumer countingConsumer)
    {
        var endReason = handshake.Outcome switch
        {
            PcpCompatibilityOutcome.RelayFull =>
                PcpChannelSessionEndReason.RelayFull,
            PcpCompatibilityOutcome.HttpRejected =>
                PcpChannelSessionEndReason.HttpRejected,
            PcpCompatibilityOutcome.ProtocolRejected =>
                PcpChannelSessionEndReason.ProtocolRejected,
            _ => throw new InvalidOperationException(
                "An accepted handshake is not a terminal result."),
        };

        return CreateResult(
            endReason,
            attemptCount,
            reconnectCount,
            handshake.HttpStatusCode,
            progress.ChannelMessageCount,
            countingConsumer) with
        { Quit = handshake.Quit };
    }

    private static PcpChannelSessionResult CreateResult(
        PcpChannelSessionEndReason endReason,
        int attemptCount,
        int reconnectCount,
        int? httpStatusCode,
        long channelMessageCount,
        CountingPacketConsumer countingConsumer)
    {
        return new PcpChannelSessionResult(
            endReason,
            attemptCount,
            reconnectCount,
            httpStatusCode,
            channelMessageCount,
            countingConsumer.PacketCount,
            countingConsumer.PayloadByteCount);
    }

    private static ChannelFaultSnapshot CreateHandshakeFaultSnapshot(
        PcpCompatibilityOutcome outcome,
        int attemptCount,
        int reconnectCount)
    {
        var category = outcome switch
        {
            PcpCompatibilityOutcome.RelayFull =>
                ChannelFaultCategory.RemoteUnavailable,
            PcpCompatibilityOutcome.HttpRejected or
            PcpCompatibilityOutcome.ProtocolRejected =>
                ChannelFaultCategory.RemoteRejected,
            _ => throw new InvalidOperationException(
                "An accepted handshake cannot create a terminal fault."),
        };

        return new ChannelFaultSnapshot(
            category,
            ChannelFaultStage.Negotiating,
            attemptCount,
            reconnectCount);
    }

    private static ChannelFaultSnapshot CreateFaultSnapshot(
        Exception exception,
        ChannelFaultStage stage,
        int attemptCount,
        int reconnectCount)
    {
        var category = exception switch
        {
            PcpChannelConsumerException =>
                ChannelFaultCategory.ConsumerFailed,
            TimeoutException =>
                ChannelFaultCategory.TimedOut,
            PcpProtocolException protocolException
                when IsResourceLimit(protocolException.Error) =>
                    ChannelFaultCategory.ResourceLimitExceeded,
            PcpProtocolException protocolException
                when protocolException.Error ==
                    PcpParseError.UnexpectedEndOfStream =>
                    ChannelFaultCategory.ConnectionFailed,
            PcpProtocolException =>
                ChannelFaultCategory.ProtocolViolation,
            InvalidDataException =>
                ChannelFaultCategory.ProtocolViolation,
            OverflowException =>
                ChannelFaultCategory.ResourceLimitExceeded,
            SocketException or IOException =>
                ChannelFaultCategory.ConnectionFailed,
            _ => ChannelFaultCategory.InternalFailure,
        };
        var effectiveStage = exception is PcpChannelConsumerException
            ? ChannelFaultStage.Consuming
            : stage;

        return new ChannelFaultSnapshot(
            category,
            effectiveStage,
            attemptCount,
            reconnectCount);
    }

    private static bool IsResourceLimit(PcpParseError error)
    {
        return error is
            PcpParseError.ContentLengthLimitExceeded or
            PcpParseError.ChildCountLimitExceeded or
            PcpParseError.NestingDepthLimitExceeded;
    }

    private static ChannelFaultStage InferFaultStage(
        ChannelSessionState state)
    {
        return state switch
        {
            ChannelSessionState.Connecting or
            ChannelSessionState.Reconnecting =>
                ChannelFaultStage.Connecting,
            ChannelSessionState.Negotiating =>
                ChannelFaultStage.Negotiating,
            _ => ChannelFaultStage.Receiving,
        };
    }

    private void RecordFault(ChannelFaultSnapshot fault)
    {
        var state = _statusTracker.Snapshot.State;

        if (state is not (
            ChannelSessionState.Faulted or
            ChannelSessionState.Stopping or
            ChannelSessionState.Stopped))
        {
            _statusTracker.RecordFault(fault);
        }
    }

    private void TransitionToStopped()
    {
        var state = _statusTracker.Snapshot.State;

        if (state == ChannelSessionState.Stopped)
        {
            return;
        }

        if (state != ChannelSessionState.Stopping)
        {
            _statusTracker.TransitionTo(ChannelSessionState.Stopping);
        }

        _statusTracker.TransitionTo(ChannelSessionState.Stopped);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
    }

    private sealed record AttemptResult(
        PcpRelayHandshakeResult? Handshake,
        PcpChannelStreamResult? Stream,
        Exception? RetryableFailure,
        ChannelFaultStage? FailureStage,
        PcpRelayHostEndpoint? ConnectedEndpoint,
        bool ReachedAccepted = false)
    {
        public static AttemptResult FromRetryableFailure(
            Exception exception,
            ChannelFaultStage stage,
            PcpRelayHostEndpoint? connectedEndpoint = null,
            bool reachedAccepted = false)
        {
            return new AttemptResult(
                Handshake: null,
                Stream: null,
                RetryableFailure: exception,
                FailureStage: stage,
                ConnectedEndpoint: connectedEndpoint,
                ReachedAccepted: reachedAccepted);
        }
    }

    private sealed record AttemptTarget(
        string Host,
        int Port,
        IPEndPoint? RedirectEndpoint);

    private sealed class CountingPacketConsumer : IPcpChannelPacketConsumer
    {
        private readonly IPcpChannelPacketConsumer _inner;
        public CountingPacketConsumer(IPcpChannelPacketConsumer inner)
        {
            _inner = inner;
        }

        public long PacketCount { get; private set; }

        public long PayloadByteCount { get; private set; }

        public async ValueTask ConsumeAsync(
            PcpChannelPacket packet,
            CancellationToken cancellationToken)
        {
            await _inner.ConsumeAsync(
                packet,
                cancellationToken).ConfigureAwait(false);

            PacketCount = checked(PacketCount + 1);
            PayloadByteCount = checked(
                PayloadByteCount + packet.Payload.Length);
        }
    }

    private sealed class SessionProgress
    {
        public int? LastHttpStatusCode { get; private set; }

        public long ChannelMessageCount { get; private set; }

        public void ObserveHttpStatus(int httpStatusCode)
        {
            LastHttpStatusCode = httpStatusCode;
        }

        public void ObserveChannelMessage()
        {
            ChannelMessageCount = checked(ChannelMessageCount + 1);
        }
    }
}
