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
    private readonly TcpTransportConnector _connector;
    private readonly ChannelStatusTracker _statusTracker;
    private readonly PcpChannelStateAdapter _stateAdapter;
    private readonly IPcpChannelPacketConsumer _packetConsumer;
    private readonly PcpChannelSessionOptions _options;
    private readonly CancellationTokenSource _stopSource = new();

    private Task<PcpChannelSessionResult>? _runTask;
    private int _disposed;

    public PcpChannelSession(
        TcpTransportConnector connector,
        ChannelStatusTracker statusTracker,
        IPcpChannelPacketConsumer packetConsumer,
        PcpChannelSessionOptions? options = null)
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
    }

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
        var countingConsumer = new CountingPacketConsumer(_packetConsumer);
        var progress = new SessionProgress();

        var attemptCount = 0;
        var reconnectCount = 0;

        _statusTracker.TransitionTo(ChannelSessionState.Connecting);

        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                attemptCount = checked(attemptCount + 1);

                var attempt = await RunAttemptAsync(
                    host,
                    port,
                    initialStreamPosition,
                    countingConsumer,
                    progress,
                    token).ConfigureAwait(false);

                if (attempt.RetryableFailure is not null)
                {
                    if (CanReconnect(attemptCount))
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
                    RecordFault(CreateHandshakeFaultSnapshot(
                        handshake.Outcome,
                        attemptCount,
                        reconnectCount));
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
                        countingConsumer);
                }

                if (CanReconnect(attemptCount))
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

            throw;
        }
    }

    private async Task<AttemptResult> RunAttemptAsync(
        string host,
        int port,
        long initialStreamPosition,
        CountingPacketConsumer countingConsumer,
        SessionProgress progress,
        CancellationToken cancellationToken)
    {
        TcpTransportConnection connection;

        try
        {
            connection = await _connector.ConnectAsync(
                host,
                port,
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
                        FailureStage: null);
                }

                _statusTracker.TransitionTo(ChannelSessionState.Receiving);

                var streamReader = new PcpChannelStreamReader(
                    pipeReader,
                    _stateAdapter,
                    countingConsumer,
                    _options.Stream,
                    completeReader: false,
                    initialPacketSequence: countingConsumer.PacketCount,
                    progress.ObserveChannelMessage);

                try
                {
                    var stream = await streamReader.ReadToEndAsync(
                        cancellationToken).ConfigureAwait(false);

                    return new AttemptResult(
                        handshake,
                        stream,
                        RetryableFailure: null,
                        FailureStage: null);
                }
                catch (TimeoutException exception)
                {
                    return AttemptResult.FromRetryableFailure(
                        exception,
                        ChannelFaultStage.Receiving);
                }
                catch (IOException exception)
                {
                    return AttemptResult.FromRetryableFailure(
                        exception,
                        ChannelFaultStage.Receiving);
                }
                catch (PcpProtocolException exception)
                    when (exception.Error ==
                        PcpParseError.UnexpectedEndOfStream)
                {
                    return AttemptResult.FromRetryableFailure(
                        exception,
                        ChannelFaultStage.Receiving);
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
                linkedSource.Token).ConfigureAwait(false);
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

    private bool CanReconnect(int attemptCount)
    {
        return attemptCount <= _options.MaxReconnectAttempts;
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
            countingConsumer);
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
        ChannelFaultStage? FailureStage)
    {
        public static AttemptResult FromRetryableFailure(
            Exception exception,
            ChannelFaultStage stage)
        {
            return new AttemptResult(
                Handshake: null,
                Stream: null,
                RetryableFailure: exception,
                FailureStage: stage);
        }
    }

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
