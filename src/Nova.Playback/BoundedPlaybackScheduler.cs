namespace PeerCast.Nova.Playback;

public enum PlaybackSchedulerFullPolicy
{
    DropOldest,
    DropIncoming,
    Reject,
}

public enum PlaybackSchedulerInitialOutputKind
{
    Video,
    Audio,
}

public enum PlaybackSchedulerDurationMediaScope
{
    All,
    AudioOnly,
}

public enum PlaybackSchedulerEnqueueResult
{
    Enqueued,
    EnqueuedAfterDroppingOldest,
    DroppedIncoming,
    Rejected,
    InvalidFrame,
    FrameTooLarge,
    SequenceRejected,
    Disposed,
    DurationUnavailable,
    FrameDurationTooLarge,
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
    long DeliveryCount,
    long OwnedByteCount = 0)
{
    public TimeSpan OwnedDuration { get; init; }
}

public sealed record PlaybackSchedulerOptions
{
    public const int DefaultCapacity = 16;
    public const int MaximumCapacity = 1_024;
    public const long DefaultMaximumOwnedBytes = 64L * 1024 * 1024;
    public const long MaximumOwnedBytesHardLimit = 1024L * 1024 * 1024;
    public static readonly TimeSpan MaximumOwnedDurationHardLimit = TimeSpan.FromMinutes(10);

    public int Capacity { get; init; } = DefaultCapacity;

    public long MaximumOwnedBytes { get; init; } = DefaultMaximumOwnedBytes;

    public TimeSpan? MaximumOwnedDuration { get; init; }

    public PlaybackSchedulerDurationMediaScope DurationMediaScope { get; init; } =
        PlaybackSchedulerDurationMediaScope.All;

    public bool DeriveUnknownAudioDurationFromSamples { get; init; }

    public PlaybackSchedulerFullPolicy FullPolicy { get; init; } =
        PlaybackSchedulerFullPolicy.DropIncoming;

    public PlaybackSchedulerInitialOutputKind InitialOutputKind { get; init; } =
        PlaybackSchedulerInitialOutputKind.Video;

    public bool RestrictOutputToInitialKind { get; init; }
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
/// scheduler disposal. Count, aggregate owned-byte, and optional aggregate
/// owned-duration limits are independent.
/// </summary>
public sealed class BoundedPlaybackScheduler : IDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly ScheduledFrame?[] _queue;
    private readonly PlaybackSchedulerFullPolicy _fullPolicy;
    private readonly PlaybackSchedulerOutputKind _initialOutputKind;
    private readonly bool _restrictOutputToInitialKind;
    private readonly long _maximumOwnedBytes;
    private readonly long? _maximumOwnedDurationTicks;
    private readonly PlaybackSchedulerDurationMediaScope _durationMediaScope;
    private readonly bool _deriveUnknownAudioDurationFromSamples;
    private readonly PlaybackPolicyClockBoundary _policy;
    private readonly PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary _delivery;
    private readonly IPlaybackSchedulerDelay _delay;
    private int _head;
    private int _count;
    private bool _hasEnqueuedSequence;
    private long _lastEnqueuedSequence;
    private long _ownedBytes;
    private long _ownedDurationTicks;
    private long _dropCount;
    private long _deliveryCount;
    private int _isDisposed;
    private ScheduledFrame? _outputReservation;
    private bool _hasReservedOutput;

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

        if (options.MaximumOwnedBytes is < 1 or > PlaybackSchedulerOptions.MaximumOwnedBytesHardLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        if (options.MaximumOwnedDuration is { } maximumOwnedDuration &&
            (maximumOwnedDuration <= TimeSpan.Zero ||
             maximumOwnedDuration > PlaybackSchedulerOptions.MaximumOwnedDurationHardLimit))
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        if (options.DeriveUnknownAudioDurationFromSamples &&
            !options.MaximumOwnedDuration.HasValue)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        if (!Enum.IsDefined(options.DurationMediaScope) ||
            (options.DurationMediaScope != PlaybackSchedulerDurationMediaScope.All &&
             !options.MaximumOwnedDuration.HasValue))
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        if (!Enum.IsDefined(options.FullPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        if (!Enum.IsDefined(options.InitialOutputKind))
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        _queue = new ScheduledFrame?[options.Capacity];
        _fullPolicy = options.FullPolicy;
        _initialOutputKind = options.InitialOutputKind == PlaybackSchedulerInitialOutputKind.Video
            ? PlaybackSchedulerOutputKind.Video
            : PlaybackSchedulerOutputKind.Audio;
        _restrictOutputToInitialKind = options.RestrictOutputToInitialKind;
        _maximumOwnedBytes = options.MaximumOwnedBytes;
        _maximumOwnedDurationTicks = options.MaximumOwnedDuration?.Ticks;
        _durationMediaScope = options.DurationMediaScope;
        _deriveUnknownAudioDurationFromSamples =
            options.DeriveUnknownAudioDurationFromSamples;
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

            if (frame.IsDisposed || sequence < 0 || !Enum.IsDefined(discontinuity) ||
                !TryGetFrameByteCount(frame, out var frameByteCount))
            {
                frame.Dispose();
                return PlaybackSchedulerEnqueueResult.InvalidFrame;
            }

            if (!TryGetFrameDurationTicks(frame, out var frameDurationTicks))
            {
                frame.Dispose();
                return _maximumOwnedDurationTicks.HasValue
                    ? PlaybackSchedulerEnqueueResult.DurationUnavailable
                    : PlaybackSchedulerEnqueueResult.InvalidFrame;
            }

            if (frameByteCount > _maximumOwnedBytes)
            {
                frame.Dispose();
                return PlaybackSchedulerEnqueueResult.FrameTooLarge;
            }

            if (_maximumOwnedDurationTicks is { } maximumOwnedDurationTicks &&
                frameDurationTicks > maximumOwnedDurationTicks)
            {
                frame.Dispose();
                return PlaybackSchedulerEnqueueResult.FrameDurationTooLarge;
            }

            if (discontinuity != PlaybackDecodedFrameDiscontinuityKind.DecoderReset &&
                _hasEnqueuedSequence && sequence <= _lastEnqueuedSequence)
            {
                frame.Dispose();
                return PlaybackSchedulerEnqueueResult.SequenceRejected;
            }

            var result = PlaybackSchedulerEnqueueResult.Enqueued;
            while (WouldExceedLimit(frameByteCount, frameDurationTicks))
            {
                switch (_fullPolicy)
                {
                    case PlaybackSchedulerFullPolicy.DropOldest:
                        if (!DropFirstUnreserved())
                        {
                            frame.Dispose();
                            IncrementSaturated(ref _dropCount);
                            return PlaybackSchedulerEnqueueResult.DroppedIncoming;
                        }

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
            _queue[tail] = new(
                frame,
                sequence,
                discontinuity,
                frameByteCount,
                frameDurationTicks);
            _count++;
            _ownedBytes += frameByteCount;
            _ownedDurationTicks += frameDurationTicks;
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

            try
            {
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
                ReleaseOwnershipAccounting(scheduled);
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
            return new(_count, _dropCount, _deliveryCount, _ownedBytes)
            {
                OwnedDuration = TimeSpan.FromTicks(_ownedDurationTicks),
            };
        }
    }

    internal bool IsDisposed => Volatile.Read(ref _isDisposed) != 0;

    internal async ValueTask<PlaybackSchedulerOutputReservation?> ReserveOutputAsync(
        PlaybackSchedulerOutputKind preferredKind,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(preferredKind) || Volatile.Read(ref _isDisposed) != 0)
        {
            return null;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _disposeCancellation.Token);
        try
        {
            await _runGate.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        lock (_sync)
        {
            if (_isDisposed != 0 || _outputReservation is not null)
            {
                _runGate.Release();
                return null;
            }

            var effectivePreferredKind = _restrictOutputToInitialKind
                ? _initialOutputKind
                : _hasReservedOutput
                    ? preferredKind
                    : _initialOutputKind;
            var scheduled = FindOutput(effectivePreferredKind);
            if (scheduled is null && !_restrictOutputToInitialKind)
            {
                scheduled = FindOutput(
                    effectivePreferredKind == PlaybackSchedulerOutputKind.Video
                        ? PlaybackSchedulerOutputKind.Audio
                        : PlaybackSchedulerOutputKind.Video);
            }

            if (scheduled is null)
            {
                _runGate.Release();
                return null;
            }

            _outputReservation = scheduled;
            _hasReservedOutput = true;
            return new PlaybackSchedulerOutputReservation(this, scheduled, scheduled.Frame,
                scheduled.Sequence, scheduled.Discontinuity);
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
                if (!DropFirstUnreserved())
                {
                    break;
                }
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

    private bool WouldExceedLimit(long incomingByteCount, long incomingDurationTicks) =>
        _count >= _queue.Length ||
        incomingByteCount > _maximumOwnedBytes - _ownedBytes ||
        (_maximumOwnedDurationTicks is { } maximumOwnedDurationTicks &&
         incomingDurationTicks > maximumOwnedDurationTicks - _ownedDurationTicks);

    private static bool TryGetFrameByteCount(
        PlaybackDecoderFfmpegManagedFrame frame,
        out long byteCount)
    {
        byteCount = 0;
        if (frame.Planes.Count == 0)
        {
            return false;
        }

        try
        {
            foreach (var plane in frame.Planes)
            {
                var length = plane.Buffer.Length;
                if (length < 1)
                {
                    byteCount = 0;
                    return false;
                }

                byteCount = checked(byteCount + length);
            }
        }
        catch (OverflowException)
        {
            byteCount = 0;
            return false;
        }

        return byteCount > 0;
    }

    private bool TryGetFrameDurationTicks(
        PlaybackDecoderFfmpegManagedFrame frame,
        out long durationTicks)
    {
        if (!_maximumOwnedDurationTicks.HasValue)
        {
            durationTicks = 0;
            return true;
        }

        var metadata = frame.Metadata;
        if (_durationMediaScope == PlaybackSchedulerDurationMediaScope.AudioOnly)
        {
            if (metadata.MediaKind == PlaybackDecoderFfmpegFrameMediaKind.Video)
            {
                durationTicks = 0;
                return true;
            }

            if (metadata.MediaKind != PlaybackDecoderFfmpegFrameMediaKind.Audio)
            {
                durationTicks = 0;
                return false;
            }
        }

        var duration = metadata.Duration;
        if (duration.IsKnown)
        {
            durationTicks = duration.Value.Ticks;
            return durationTicks > 0;
        }

        if (!_deriveUnknownAudioDurationFromSamples ||
            metadata.MediaKind != PlaybackDecoderFfmpegFrameMediaKind.Audio ||
            metadata.SampleRate <= 0 ||
            metadata.SampleCount <= 0)
        {
            durationTicks = 0;
            return false;
        }

        var sampleTicks = checked((long)metadata.SampleCount * TimeSpan.TicksPerSecond);
        durationTicks = checked(
            (sampleTicks + metadata.SampleRate - 1) / metadata.SampleRate);
        return durationTicks > 0;
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

    private ScheduledFrame? FindOutput(PlaybackSchedulerOutputKind kind)
    {
        for (var offset = 0; offset < _count; offset++)
        {
            var scheduled = _queue[(_head + offset) % _queue.Length];
            if (scheduled is not null && ToOutputKind(scheduled) == kind)
            {
                return scheduled;
            }
        }
        return null;
    }

    private bool DropFirstUnreserved()
    {
        for (var offset = 0; offset < _count; offset++)
        {
            var scheduled = _queue[(_head + offset) % _queue.Length];
            if (scheduled is not null && !ReferenceEquals(scheduled, _outputReservation))
            {
                Remove(scheduled, dispose: true, countDrop: true);
                return true;
            }
        }
        return false;
    }

    private void CompleteOutput(
        ScheduledFrame scheduled,
        PlaybackSchedulerOutputCompletion completion)
    {
        if (!Enum.IsDefined(completion))
        {
            completion = PlaybackSchedulerOutputCompletion.Retained;
        }

        lock (_sync)
        {
            if (!ReferenceEquals(_outputReservation, scheduled))
            {
                return;
            }

            _outputReservation = null;
            if (completion != PlaybackSchedulerOutputCompletion.Retained || _isDisposed != 0)
            {
                Remove(scheduled, dispose: true, countDrop: false);
                if (completion == PlaybackSchedulerOutputCompletion.Delivered)
                {
                    IncrementSaturated(ref _deliveryCount);
                }
                else if (completion == PlaybackSchedulerOutputCompletion.Dropped)
                {
                    IncrementSaturated(ref _dropCount);
                }
            }
        }

        _runGate.Release();
    }

    internal void CompleteReservedIdentity(
        object scheduledIdentity,
        PlaybackSchedulerOutputCompletion completion)
    {
        if (scheduledIdentity is ScheduledFrame scheduled)
        {
            CompleteOutput(scheduled, completion);
        }
    }

    internal void CompleteReservedIdentity(object scheduledIdentity, bool consume) =>
        CompleteReservedIdentity(
            scheduledIdentity,
            consume
                ? PlaybackSchedulerOutputCompletion.Delivered
                : PlaybackSchedulerOutputCompletion.Retained);

    private void Remove(ScheduledFrame scheduled, bool dispose, bool countDrop)
    {
        var found = -1;
        for (var offset = 0; offset < _count; offset++)
        {
            if (ReferenceEquals(_queue[(_head + offset) % _queue.Length], scheduled))
            {
                found = offset;
                break;
            }
        }
        if (found < 0) return;
        for (var offset = found; offset < _count - 1; offset++)
        {
            _queue[(_head + offset) % _queue.Length] =
                _queue[(_head + offset + 1) % _queue.Length];
        }
        _queue[(_head + _count - 1) % _queue.Length] = null;
        _count--;
        ReleaseOwnershipAccountingNoLock(scheduled);
        if (dispose) scheduled.Frame.Dispose();
        if (countDrop) IncrementSaturated(ref _dropCount);
    }

    private void ReleaseOwnershipAccounting(ScheduledFrame scheduled)
    {
        lock (_sync)
        {
            ReleaseOwnershipAccountingNoLock(scheduled);
        }
    }

    private void ReleaseOwnershipAccountingNoLock(ScheduledFrame scheduled)
    {
        _ownedBytes = scheduled.ByteCount <= _ownedBytes
            ? _ownedBytes - scheduled.ByteCount
            : 0;
        _ownedDurationTicks = scheduled.DurationTicks <= _ownedDurationTicks
            ? _ownedDurationTicks - scheduled.DurationTicks
            : 0;
    }

    private static PlaybackSchedulerOutputKind? ToOutputKind(ScheduledFrame scheduled) =>
        scheduled.Frame.Metadata.MediaKind switch
        {
            PlaybackDecoderFfmpegFrameMediaKind.Video => PlaybackSchedulerOutputKind.Video,
            PlaybackDecoderFfmpegFrameMediaKind.Audio => PlaybackSchedulerOutputKind.Audio,
            _ => null,
        };

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
        PlaybackDecodedFrameDiscontinuityKind Discontinuity,
        long ByteCount,
        long DurationTicks);
}

internal enum PlaybackSchedulerOutputKind
{
    Video,
    Audio,
}

internal enum PlaybackSchedulerOutputCompletion
{
    Retained,
    Delivered,
    Dropped,
}

internal sealed class PlaybackSchedulerOutputReservation : IDisposable
{
    private BoundedPlaybackScheduler? _scheduler;
    private readonly object _scheduledIdentity;

    internal PlaybackSchedulerOutputReservation(
        BoundedPlaybackScheduler scheduler,
        object scheduledIdentity,
        PlaybackDecoderFfmpegManagedFrame frame,
        long sequence,
        PlaybackDecodedFrameDiscontinuityKind discontinuity)
    {
        _scheduler = scheduler;
        _scheduledIdentity = scheduledIdentity;
        Frame = frame;
        Sequence = sequence;
        Discontinuity = discontinuity;
    }

    public PlaybackDecoderFfmpegManagedFrame Frame { get; }
    public long Sequence { get; }
    public PlaybackDecodedFrameDiscontinuityKind Discontinuity { get; }

    public void Complete(PlaybackSchedulerOutputCompletion completion)
    {
        if (!Enum.IsDefined(completion))
        {
            completion = PlaybackSchedulerOutputCompletion.Retained;
        }

        var scheduler = Interlocked.Exchange(ref _scheduler, null);
        if (scheduler is not null)
        {
            scheduler.CompleteReservedIdentity(_scheduledIdentity, completion);
        }
    }

    public void Complete(bool consume) =>
        Complete(
            consume
                ? PlaybackSchedulerOutputCompletion.Delivered
                : PlaybackSchedulerOutputCompletion.Retained);

    public void Dispose() => Complete(PlaybackSchedulerOutputCompletion.Retained);
}
