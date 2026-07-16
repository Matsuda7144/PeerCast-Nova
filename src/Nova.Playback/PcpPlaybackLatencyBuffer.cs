using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Playback;

/// <summary>
/// Adapts callback-scoped PCP packets to one packet/byte-bounded player input.
/// Overflow evicts the oldest queued packets and marks the first retained
/// packet with an explicit latency discontinuity. One reader owns each
/// returned payload lease until disposal.
/// </summary>
public sealed class PcpPlaybackLatencyBuffer :
    IPcpChannelPacketConsumer,
    IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Queue<PlaybackQueuedPacket> _pending = [];
    private readonly PlaybackLatencyBufferOptions _options;
    private TaskCompletionSource<bool> _signal = CreateSignal();
    private PlaybackInputState _state = PlaybackInputState.Accepting;
    private PlaybackInputEndReason? _endReason;
    private PlaybackDiscontinuityKind _nextPacketDiscontinuity;
    private int _pendingPacketCount;
    private long _pendingByteCount;
    private int _outstandingPacketCount;
    private long _outstandingByteCount;
    private long _receivedPacketCount;
    private long _receivedByteCount;
    private long _dequeuedPacketCount;
    private long _dequeuedByteCount;
    private long _skippedPacketCount;
    private long _skippedByteCount;
    private long _discontinuityPacketCount;
    private int _consumeActive;
    private int _readActive;

    public PcpPlaybackLatencyBuffer(
        PlaybackLatencyBufferOptions? options = null)
    {
        _options = options is null
            ? new PlaybackLatencyBufferOptions()
            : options with { };
        _options.Validate();
    }

    public PlaybackInputStatusSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return new PlaybackInputStatusSnapshot(
                    _state,
                    _endReason,
                    _pendingPacketCount,
                    _pendingByteCount,
                    _outstandingPacketCount,
                    _outstandingByteCount,
                    _receivedPacketCount,
                    _receivedByteCount,
                    _dequeuedPacketCount,
                    _dequeuedByteCount,
                    _skippedPacketCount,
                    _skippedByteCount,
                    _discontinuityPacketCount);
            }
        }
    }

    public ValueTask ConsumeAsync(
        PcpChannelPacket packet,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packet);
        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.Exchange(ref _consumeActive, 1) != 0)
        {
            throw new InvalidOperationException(
                "Playback input consumption must remain sequential.");
        }

        try
        {
            Consume(packet, cancellationToken);
            return ValueTask.CompletedTask;
        }
        finally
        {
            Volatile.Write(ref _consumeActive, 0);
        }
    }

    public async ValueTask<PlaybackPacketLease?> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.Exchange(ref _readActive, 1) != 0)
        {
            throw new InvalidOperationException(
                "Playback input supports one active reader.");
        }

        try
        {
            while (true)
            {
                Task waitTask;

                lock (_gate)
                {
                    if (_pending.TryDequeue(out var queued))
                    {
                        _pendingPacketCount--;
                        _pendingByteCount -= queued.PayloadLength;
                        _outstandingPacketCount++;
                        _outstandingByteCount = checked(
                            _outstandingByteCount + queued.PayloadLength);
                        _dequeuedPacketCount = checked(
                            _dequeuedPacketCount + 1);
                        _dequeuedByteCount = checked(
                            _dequeuedByteCount + queued.PayloadLength);

                        if (queued.Discontinuity !=
                            PlaybackDiscontinuityKind.None)
                        {
                            _discontinuityPacketCount = checked(
                                _discontinuityPacketCount + 1);
                        }

                        if (_state == PlaybackInputState.Draining &&
                            _pendingPacketCount == 0)
                        {
                            _state = PlaybackInputState.Completed;
                        }

                        return queued.TakeLease(OnLeaseDisposed);
                    }

                    if (_state == PlaybackInputState.Draining)
                    {
                        _state = PlaybackInputState.Completed;
                    }

                    if (_state is PlaybackInputState.Completed or
                        PlaybackInputState.Stopped)
                    {
                        return null;
                    }

                    waitTask = _signal.Task;
                }

                await waitTask.WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            Volatile.Write(ref _readActive, 0);
        }
    }

    public void Complete()
    {
        TaskCompletionSource<bool>? signal = null;

        lock (_gate)
        {
            if (_state != PlaybackInputState.Accepting)
            {
                return;
            }

            _endReason = PlaybackInputEndReason.SourceCompleted;
            _state = _pendingPacketCount == 0
                ? PlaybackInputState.Completed
                : PlaybackInputState.Draining;
            signal = PulseLocked();
        }

        signal.TrySetResult(true);
    }

    public void Stop()
    {
        TaskCompletionSource<bool>? signal = null;

        lock (_gate)
        {
            if (_state is PlaybackInputState.Completed or
                PlaybackInputState.Stopped)
            {
                return;
            }

            _state = PlaybackInputState.Stopped;
            _endReason = PlaybackInputEndReason.OwnerStopped;
            DrainPendingLocked();
            signal = PulseLocked();
        }

        signal.TrySetResult(true);
    }

    public ValueTask DisposeAsync()
    {
        Stop();
        return ValueTask.CompletedTask;
    }

    private static TaskCompletionSource<bool> CreateSignal()
    {
        return new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private void Consume(
        PcpChannelPacket packet,
        CancellationToken cancellationToken)
    {
        var payloadLength = packet.Payload.Length;

        lock (_gate)
        {
            EnsureAcceptingLocked();

            if (payloadLength > _options.MaxPendingBytes)
            {
                RecordReceivedLocked(payloadLength);
                RecordSkippedLocked(payloadLength);
                _nextPacketDiscontinuity |=
                    PlaybackDiscontinuityKind.LatencySkip;

                if (packet.Discontinuity is not null)
                {
                    _nextPacketDiscontinuity |=
                        PlaybackDiscontinuityKind.Source;
                }

                return;
            }
        }

        var sourceDiscontinuity = packet.Discontinuity is null
            ? PlaybackDiscontinuityKind.None
            : PlaybackDiscontinuityKind.Source;
        var queued = PlaybackQueuedPacket.CopyFrom(
            packet.Sequence,
            packet.StreamPosition,
            sourceDiscontinuity,
            packet.Payload);
        TaskCompletionSource<bool>? signal = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                EnsureAcceptingLocked();
                RecordReceivedLocked(payloadLength);

                var skippedDiscontinuity =
                    PlaybackDiscontinuityKind.None;

                while (_pendingPacketCount >=
                        _options.MaxPendingPackets ||
                    payloadLength >
                        _options.MaxPendingBytes - _pendingByteCount)
                {
                    var skipped = _pending.Dequeue();
                    _pendingPacketCount--;
                    _pendingByteCount -= skipped.PayloadLength;
                    RecordSkippedLocked(skipped.PayloadLength);
                    skippedDiscontinuity |= skipped.Discontinuity |
                        PlaybackDiscontinuityKind.LatencySkip;
                    skipped.Dispose();
                }

                if (skippedDiscontinuity !=
                    PlaybackDiscontinuityKind.None)
                {
                    if (_pending.TryPeek(out var firstRetained))
                    {
                        firstRetained.MarkDiscontinuity(
                            skippedDiscontinuity);
                    }
                    else
                    {
                        _nextPacketDiscontinuity |=
                            skippedDiscontinuity;
                    }
                }

                queued.MarkDiscontinuity(_nextPacketDiscontinuity);
                _nextPacketDiscontinuity =
                    PlaybackDiscontinuityKind.None;
                _pending.Enqueue(queued);
                _pendingPacketCount++;
                _pendingByteCount = checked(
                    _pendingByteCount + payloadLength);
                signal = PulseLocked();
            }
        }
        catch
        {
            queued.Dispose();
            throw;
        }

        signal.TrySetResult(true);
    }

    private void EnsureAcceptingLocked()
    {
        ObjectDisposedException.ThrowIf(
            _state == PlaybackInputState.Stopped,
            this);

        if (_state != PlaybackInputState.Accepting)
        {
            throw new InvalidOperationException(
                "Playback input is no longer accepting packets.");
        }
    }

    private void RecordReceivedLocked(int payloadLength)
    {
        _receivedPacketCount = checked(_receivedPacketCount + 1);
        _receivedByteCount = checked(
            _receivedByteCount + payloadLength);
    }

    private void RecordSkippedLocked(int payloadLength)
    {
        _skippedPacketCount = checked(_skippedPacketCount + 1);
        _skippedByteCount = checked(
            _skippedByteCount + payloadLength);
    }

    private TaskCompletionSource<bool> PulseLocked()
    {
        var signal = _signal;
        _signal = CreateSignal();
        return signal;
    }

    private void DrainPendingLocked()
    {
        while (_pending.TryDequeue(out var queued))
        {
            _pendingPacketCount--;
            _pendingByteCount -= queued.PayloadLength;
            RecordSkippedLocked(queued.PayloadLength);
            queued.Dispose();
        }

        _nextPacketDiscontinuity =
            PlaybackDiscontinuityKind.None;
    }

    private void OnLeaseDisposed(int payloadLength)
    {
        lock (_gate)
        {
            _outstandingPacketCount--;
            _outstandingByteCount -= payloadLength;
        }
    }
}
