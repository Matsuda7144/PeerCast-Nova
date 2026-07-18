namespace PeerCast.Nova.Playback;

public enum PlaybackSchedulerFullPolicy
{
    DropOldest,
    DropIncoming,
    Reject,
}

public enum PlaybackSchedulerEnqueueResult
{
    Enqueued,
    EnqueuedAfterDroppingOldest,
    DroppedIncoming,
    Rejected,
    InvalidFrame,
    SequenceRejected,
    Disposed,
}

public enum PlaybackSchedulerRunResult
{
    Delivered,
    Empty,
    WaitCancelled,
    DroppedByPolicy,
    ClockReset,
    PolicyRejected,
    SinkFailed,
    DeliveryFailed,
    Disposed,
    IntegrationFailed,
}

public readonly record struct PlaybackSchedulerSnapshot(
    int QueueLength,
    long DropCount,
    long DeliveryCount);

public sealed record PlaybackSchedulerOptions
{
    public const int DefaultCapacity = 16;
    public const int MaximumCapacity = 1_024;

    public int Capacity { get; init; } = DefaultCapacity;

    public PlaybackSchedulerFullPolicy FullPolicy { get; init; } =
        PlaybackSchedulerFullPolicy.DropIncoming;
}

internal interface IPlaybackSchedulerDelay
{
    ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

internal sealed class PlaybackSchedulerDelay : IPlaybackSchedulerDelay
{
    public async ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
}

/// <summary>
/// Explicitly driven bounded scheduler. Accepted frames become scheduler-owned
/// and are disposed after delivery, policy handling, cancellation, dropping, or
/// scheduler disposal.
/// </summary>
public sealed class BoundedPlaybackScheduler : IDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly ScheduledFrame?[] _queue;
    private readonly PlaybackSchedulerFullPolicy _fullPolicy;
    private readonly PlaybackPolicyClockBoundary _policy;
    private readonly PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary _delivery;
    private readonly IPlaybackSchedulerDelay _delay;
    private int _head;
    private int _count;
    private bool _hasEnqueuedSequence;
    private long _lastEnqueuedSequence;
    private long _dropCount;
    private long _deliveryCount;
    private int _isDisposed;

    public BoundedPlaybackScheduler(
        PlaybackSchedulerOptions options,
        PlaybackPolicyClockBoundary policy)
        : this(
            options,
            policy,
            new PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary(),
            new PlaybackSchedulerDelay())
    {
    }

    internal BoundedPlaybackScheduler(
        PlaybackSchedulerOptions options,
        PlaybackPolicyClockBoundary policy,
        PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary delivery,
        IPlaybackSchedulerDelay delay)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(delay);
        if (options.Capacity is < 1 or > PlaybackSchedulerOptions.MaximumCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        if (!Enum.IsDefined(options.FullPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        _queue = new ScheduledFrame?[options.Capacity];
        _fullPolicy = options.FullPolicy;
        _policy = policy;
        _delivery = delivery;
        _delay = delay;
    }

    public PlaybackSchedulerEnqueueResult Enqueue(
        PlaybackDecoderFfmpegManagedFrame? frame,
        long sequence,
        PlaybackDecodedFrameDiscontinuityKind discontinuity)
    {
        if (frame is null)
        {
            return PlaybackSchedulerEnqueueResult.InvalidFrame;
        }

        lock (_sync)
        {
            if (_isDisposed != 0)
            {
                frame.Dispose();
                return PlaybackSchedulerEnqueueResult.Disposed;
            }

            if (frame.IsDisposed || sequence < 0 || !Enum.IsDefined(discontinuity))
            {
                frame.Dispose();
                return PlaybackSchedulerEnqueueResult.InvalidFrame;
            }

            if (discontinuity != PlaybackDecodedFrameDiscontinuityKind.DecoderReset &&
                _hasEnqueuedSequence && sequence <= _lastEnqueuedSequence)
            {
                frame.Dispose();
                return PlaybackSchedulerEnqueueResult.SequenceRejected;
            }

            var result = PlaybackSchedulerEnqueueResult.Enqueued;
            if (_count == _queue.Length)
            {
                switch (_fullPolicy)
                {
                    case PlaybackSchedulerFullPolicy.DropOldest:
                        DropHead();
                        result = PlaybackSchedulerEnqueueResult.EnqueuedAfterDroppingOldest;
                        break;
                    case PlaybackSchedulerFullPolicy.DropIncoming:
                        frame.Dispose();
                        IncrementSaturated(ref _dropCount);
                        return PlaybackSchedulerEnqueueResult.DroppedIncoming;
                    case PlaybackSchedulerFullPolicy.Reject:
                        frame.Dispose();
                        return PlaybackSchedulerEnqueueResult.Rejected;
                    default:
                        frame.Dispose();
                        return PlaybackSchedulerEnqueueResult.Rejected;
                }
            }

            var tail = (_head + _count) % _queue.Length;
            _queue[tail] = new(frame, sequence, discontinuity);
            _count++;
            _hasEnqueuedSequence = true;
            _lastEnqueuedSequence = sequence;
            return result;
        }
    }

    public async ValueTask<PlaybackSchedulerRunResult> RunOneAsync(
        PlaybackDecoderFfmpegContextLease contextLease,
        IPlaybackDecodedFrameSink sink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contextLease);
        ArgumentNullException.ThrowIfNull(sink);
        if (Volatile.Read(ref _isDisposed) != 0)
        {
            return PlaybackSchedulerRunResult.Disposed;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCancellation.Token);
        try
        {
            await _runGate.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Volatile.Read(ref _isDisposed) != 0
                ? PlaybackSchedulerRunResult.Disposed
                : PlaybackSchedulerRunResult.WaitCancelled;
        }

        try
        {
            if (Volatile.Read(ref _isDisposed) != 0)
            {
                return PlaybackSchedulerRunResult.Disposed;
            }

            ScheduledFrame? scheduled;
            lock (_sync)
            {
                if (_isDisposed != 0)
                {
                    return PlaybackSchedulerRunResult.Disposed;
                }

                scheduled = Dequeue();
            }

            if (scheduled is null)
            {
                return PlaybackSchedulerRunResult.Empty;
            }

            using (scheduled.Frame)
            {
                try
                {
                    var decision = _policy.Evaluate(CreatePolicyInput(scheduled));
                    switch (decision.Kind)
                    {
                        case PlaybackPolicyDecisionKind.Drop:
                            IncrementDrop();
                            return PlaybackSchedulerRunResult.DroppedByPolicy;
                        case PlaybackPolicyDecisionKind.ResetClock:
                            return PlaybackSchedulerRunResult.ClockReset;
                        case PlaybackPolicyDecisionKind.Reject:
                            return PlaybackSchedulerRunResult.PolicyRejected;
                        case PlaybackPolicyDecisionKind.Wait:
                            await _delay.DelayAsync(
                                decision.WaitDuration,
                                linkedCancellation.Token).ConfigureAwait(false);
                            break;
                        case PlaybackPolicyDecisionKind.DeliverNow:
                            break;
                        default:
                            contextLease.Dispose();
                            return PlaybackSchedulerRunResult.IntegrationFailed;
                    }

                    var delivery = await _delivery.DeliverAsync(
                        contextLease,
                        scheduled.Frame,
                        scheduled.Sequence,
                        scheduled.Discontinuity,
                        sink,
                        linkedCancellation.Token).ConfigureAwait(false);
                    return MapDelivery(delivery);
                }
                catch (OperationCanceledException)
                {
                    return Volatile.Read(ref _isDisposed) != 0
                        ? PlaybackSchedulerRunResult.Disposed
                        : PlaybackSchedulerRunResult.WaitCancelled;
                }
                catch
                {
                    contextLease.Dispose();
                    return PlaybackSchedulerRunResult.IntegrationFailed;
                }
            }
        }
        finally
        {
            _runGate.Release();
        }
    }

    public PlaybackSchedulerSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new(_count, _dropCount, _deliveryCount);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _disposeCancellation.Cancel();
        _delivery.Dispose();
        lock (_sync)
        {
            while (_count > 0)
            {
                DropHead();
            }
        }

        GC.SuppressFinalize(this);
    }

    private PlaybackSchedulerRunResult MapDelivery(
        PlaybackDecoderFfmpegAwaitedSinkDeliveryResult result)
    {
        switch (result)
        {
            case PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Delivered:
                lock (_sync)
                {
                    IncrementSaturated(ref _deliveryCount);
                }

                return PlaybackSchedulerRunResult.Delivered;
            case PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Cancelled:
                return Volatile.Read(ref _isDisposed) != 0
                    ? PlaybackSchedulerRunResult.Disposed
                    : PlaybackSchedulerRunResult.WaitCancelled;
            case PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.SinkFailed:
                return PlaybackSchedulerRunResult.SinkFailed;
            case PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.IntegrationFailed:
                return PlaybackSchedulerRunResult.IntegrationFailed;
            default:
                return PlaybackSchedulerRunResult.DeliveryFailed;
        }
    }

    private static PlaybackPolicyFrameInput CreatePolicyInput(ScheduledFrame scheduled)
    {
        var metadata = scheduled.Frame.Metadata;
        var mediaKind = metadata.MediaKind switch
        {
            PlaybackDecoderFfmpegFrameMediaKind.Audio => PlaybackPolicyMediaKind.Audio,
            PlaybackDecoderFfmpegFrameMediaKind.Video => PlaybackPolicyMediaKind.Video,
            _ => (PlaybackPolicyMediaKind)(-1),
        };
        return new(
            mediaKind,
            metadata.PresentationTimestamp.IsKnown
                ? PlaybackPolicyTime.Known(metadata.PresentationTimestamp.Value)
                : PlaybackPolicyTime.Unknown,
            metadata.Duration.IsKnown
                ? PlaybackPolicyTime.Known(metadata.Duration.Value)
                : PlaybackPolicyTime.Unknown,
            scheduled.Sequence,
            scheduled.Discontinuity);
    }

    private ScheduledFrame? Dequeue()
    {
        if (_count == 0)
        {
            return null;
        }

        var frame = _queue[_head];
        _queue[_head] = null;
        _head = (_head + 1) % _queue.Length;
        _count--;
        return frame;
    }

    private void DropHead()
    {
        var frame = Dequeue();
        if (frame is not null)
        {
            frame.Frame.Dispose();
            IncrementSaturated(ref _dropCount);
        }
    }

    private void IncrementDrop()
    {
        lock (_sync)
        {
            IncrementSaturated(ref _dropCount);
        }
    }

    private static void IncrementSaturated(ref long value)
    {
        if (value < long.MaxValue)
        {
            value++;
        }
    }

    private sealed record ScheduledFrame(
        PlaybackDecoderFfmpegManagedFrame Frame,
        long Sequence,
        PlaybackDecodedFrameDiscontinuityKind Discontinuity);
}
