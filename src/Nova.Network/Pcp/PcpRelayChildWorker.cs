namespace PeerCast.Nova.Network.Pcp;

internal sealed class PcpRelayChildWorker : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Queue<PcpRelayQueuedPacket> _pending = new();
    private readonly SemaphoreSlim _available = new(0);
    private readonly CancellationTokenSource _stopSource = new();
    private readonly IPcpRelayChildSink _sink;
    private readonly Action<PcpRelayChildWorker> _stoppedAccepting;
    private readonly int _maxPendingPackets;
    private readonly long _maxPendingBytes;
    private readonly TimeSpan _stopTimeout;
    private readonly Task _worker;
    private Task? _cancellationTask;
    private PcpRelayChildState _state = PcpRelayChildState.Accepting;
    private PcpRelayChildEndReason? _endReason;
    private int _pendingPacketCount;
    private int _inFlightPacketCount;
    private bool _cleanupFailed;
    private bool _startupPending;
    private bool _startupFailed;
    private long _pendingByteCount;
    private long _deliveredPacketCount;
    private long _deliveredByteCount;
    private long _discardedPacketCount;
    private long _discardedByteCount;

    public PcpRelayChildWorker(
        IPcpRelayChildSink sink,
        PcpRelayFanoutOptions options,
        Action<PcpRelayChildWorker> stoppedAccepting)
    {
        _sink = sink;
        _stoppedAccepting = stoppedAccepting;
        _maxPendingPackets = options.MaxPendingPacketsPerChild;
        _maxPendingBytes = options.MaxPendingBytesPerChild;
        _stopTimeout = options.ChildStopTimeout;
        _startupPending = sink is IPcpRelayChildStartupBoundary;
        _worker = RunAsync();
    }

    public PcpRelayChildStatusSnapshot Snapshot
    {
        get
        {
            // Query the optional startup owner outside the worker lock. A fault
            // is visible even when an uncooperative initial write is still pending.
            var startupCancellationFailed = _sink is IPcpRelayChildStartupBoundary startup &&
                startup.StartupCancellationCompletion.IsFaulted;
            lock (_gate)
            {
                return new PcpRelayChildStatusSnapshot(
                    _state,
                    _endReason,
                    _pendingPacketCount,
                    _pendingByteCount,
                    _deliveredPacketCount,
                    _deliveredByteCount,
                    _discardedPacketCount,
                    _discardedByteCount)
                {
                    InFlightPacketCount = _inFlightPacketCount,
                    CleanupFailed = _cleanupFailed || startupCancellationFailed,
                    StartupPending = _startupPending,
                    StartupFailed = _startupFailed
                };
            }
        }
    }

    public Task Completion => _worker;

    public bool TryEnqueue(
        PcpChannelPacket source,
        PcpRelaySharedPayload payload,
        bool isRetainedHeadReplay = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(payload);

        var shouldStop = false;
        var accepted = false;

        lock (_gate)
        {
            if (_state != PcpRelayChildState.Accepting)
            {
                return false;
            }

            var payloadLength = payload.Memory.Length;

            if (_pendingPacketCount >= _maxPendingPackets ||
                payloadLength > _maxPendingBytes - _pendingByteCount)
            {
                BeginStopLocked(PcpRelayChildEndReason.SlowConsumer);
                _discardedPacketCount = checked(_discardedPacketCount + 1);
                _discardedByteCount = checked(
                    _discardedByteCount + payloadLength);
                shouldStop = true;
            }
            else
            {
                var packet = new PcpRelayQueuedPacket(
                    source,
                    payload,
                    isRetainedHeadReplay);
                _pending.Enqueue(packet);
                _pendingPacketCount = checked(_pendingPacketCount + 1);
                _pendingByteCount = checked(
                    _pendingByteCount + packet.PayloadLength);
                accepted = true;
            }
        }

        if (accepted)
        {
            _available.Release();
        }
        else if (shouldStop)
        {
            SignalStop();
        }

        return accepted;
    }

    public void RequestStop(PcpRelayChildEndReason endReason)
    {
        var shouldStop = false;

        lock (_gate)
        {
            if (_state == PcpRelayChildState.Accepting)
            {
                BeginStopLocked(endReason);
                shouldStop = true;
            }
        }

        if (shouldStop)
        {
            SignalStop();
        }
    }

    public async Task<bool> WaitForCompletionAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await _worker.WaitAsync(
                _stopTimeout,
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        RequestStop(PcpRelayChildEndReason.OwnerRemoved);
        await _worker.ConfigureAwait(false);
    }

    private void BeginStopLocked(PcpRelayChildEndReason endReason)
    {
        _state = PcpRelayChildState.Stopping;
        _endReason = endReason;
    }

    private void SignalStop()
    {
        _stoppedAccepting(this);

        lock (_gate)
        {
            _cancellationTask ??= Task.Run(CancelSource);
        }
    }

    private void CancelSource()
    {
        try
        {
            _stopSource.Cancel();
        }
        catch (AggregateException)
        {
            // A child-owned cancellation callback must not affect the upstream.
            lock (_gate) { _cleanupFailed = true; }
        }
    }

    private async Task RunAsync()
    {
        try
        {
            if (_sink is IPcpRelayChildStartupBoundary startup)
            {
                using var registration = _stopSource.Token.Register(startup.RequestStartupCancellation);
                try { await startup.StartupCompletion.ConfigureAwait(false); }
                catch (OperationCanceledException) when (_stopSource.IsCancellationRequested) { throw; }
                catch (Exception)
                {
                    lock (_gate) { _startupFailed = true; }
                    throw;
                }
                finally { lock (_gate) { _startupPending = false; } }
            }
            while (true)
            {
                await _available.WaitAsync(
                    _stopSource.Token).ConfigureAwait(false);

                PcpRelayQueuedPacket? packet;

                lock (_gate)
                {
                    packet = _pending.Count == 0
                        ? null
                        : _pending.Dequeue();
                    _inFlightPacketCount = packet is null ? 0 : 1;
                }

                if (packet is null)
                {
                    continue;
                }

                var delivered = false;

                try
                {
                    await _sink.SendAsync(
                        packet.Packet,
                        _stopSource.Token).ConfigureAwait(false);
                    delivered = true;
                }
                finally
                {
                    packet.Dispose();
                    CompletePacket(packet.PayloadLength, delivered);
                }
            }
        }
        catch (OperationCanceledException)
            when (_stopSource.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            RequestStop(PcpRelayChildEndReason.SinkFailed);
        }
        finally
        {
            DrainPendingPackets();

            Task cancellationTask;

            lock (_gate)
            {
                cancellationTask = _cancellationTask ?? Task.CompletedTask;
            }

            await cancellationTask.ConfigureAwait(false);
            if (_sink is IPcpRelayChildStartupBoundary startupCleanup)
            {
                // The request callback has been unregistered. Join the actual
                // cancellation operation, not only its nonblocking request.
                try { await startupCleanup.StartupCancellationCompletion.ConfigureAwait(false); }
                catch { lock (_gate) { _cleanupFailed = true; } }
            }
            _available.Dispose();
            _stopSource.Dispose();

            lock (_gate)
            {
                _state = PcpRelayChildState.Stopped;
            }

            // Optional internal boundary after actual cleanup, outside locks.
            if (_sink is IPcpRelayChildCompletionObserver observer)
                await observer.ObserveCompletionAsync().ConfigureAwait(false);

        }
    }

    private void CompletePacket(int payloadLength, bool delivered)
    {
        lock (_gate)
        {
            _pendingPacketCount--;
            _inFlightPacketCount--;
            _pendingByteCount -= payloadLength;

            if (delivered)
            {
                _deliveredPacketCount = checked(_deliveredPacketCount + 1);
                _deliveredByteCount = checked(
                    _deliveredByteCount + payloadLength);
            }
            else
            {
                _discardedPacketCount = checked(_discardedPacketCount + 1);
                _discardedByteCount = checked(
                    _discardedByteCount + payloadLength);
            }
        }
    }

    private void DrainPendingPackets()
    {
        lock (_gate)
        {
            while (_pending.TryDequeue(out var packet))
            {
                packet.Dispose();
                _pendingPacketCount--;
                _pendingByteCount -= packet.PayloadLength;
                _discardedPacketCount = checked(_discardedPacketCount + 1);
                _discardedByteCount = checked(
                    _discardedByteCount + packet.PayloadLength);
            }
        }
    }
}
