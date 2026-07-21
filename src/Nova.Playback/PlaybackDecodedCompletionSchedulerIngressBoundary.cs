namespace PeerCast.Nova.Playback;

public enum PlaybackDecodedCompletionSchedulerIngressState
{
    Accepting,
    Faulted,
    Disposed,
}

public enum PlaybackDecodedCompletionSchedulerIngressResult
{
    None,
    Transferred,
    Cancelled,
    InvalidCompletion,
    SequenceExhausted,
    EnqueueRejected,
    Faulted,
    Disposed,
}

public sealed record PlaybackDecodedCompletionSchedulerIngressSnapshot(
    PlaybackDecodedCompletionSchedulerIngressState State,
    PlaybackDecodedCompletionSchedulerIngressResult Result,
    int DecoderGeneration,
    int DiscontinuityGeneration,
    long NextVideoSourceSequence,
    long NextAudioSourceSequence,
    long NextSchedulerSequence,
    long AcceptedTransferCount,
    bool DecoderResetPending,
    PlaybackSchedulerSnapshot Scheduler);

internal interface IPlaybackDecodedCompletionSchedulerIngressOperations
{
    PlaybackSchedulerEnqueueResult Enqueue(
        BoundedPlaybackScheduler scheduler,
        PlaybackDecoderFfmpegManagedFrame frame,
        long sequence,
        PlaybackDecodedFrameDiscontinuityKind discontinuity);
}

internal sealed class PlaybackDecodedCompletionSchedulerIngressOperations :
    IPlaybackDecodedCompletionSchedulerIngressOperations
{
    public PlaybackSchedulerEnqueueResult Enqueue(
        BoundedPlaybackScheduler scheduler,
        PlaybackDecoderFfmpegManagedFrame frame,
        long sequence,
        PlaybackDecodedFrameDiscontinuityKind discontinuity) =>
        scheduler.Enqueue(frame, sequence, discontinuity);
}

/// <summary>
/// Generation-scoped one-shot ownership transfer from an exact source-bound decoded
/// completion into the existing bounded scheduler. The scheduler and matching decoder
/// context lifetime are disposed in that order. No second decoded queue or retry is owned.
/// </summary>
public sealed class PlaybackDecodedCompletionSchedulerIngressBoundary : IDisposable
{
    private readonly object _sync = new();
    private readonly PlaybackBoundedDemuxSession _sourceSession;
    private readonly int _decoderGeneration;
    private readonly int _discontinuityGeneration;
    private readonly IPlaybackDecodedCompletionSchedulerIngressOperations _operations;

    private BoundedPlaybackScheduler? _scheduler;
    private IDisposable? _decoderContextLifetime;
    private PlaybackDecodedCompletionSchedulerIngressState _state =
        PlaybackDecodedCompletionSchedulerIngressState.Accepting;
    private PlaybackDecodedCompletionSchedulerIngressResult _result =
        PlaybackDecodedCompletionSchedulerIngressResult.None;
    private long _nextVideoSourceSequence;
    private long _nextAudioSourceSequence;
    private long _nextSchedulerSequence;
    private long _acceptedTransferCount;
    private bool _decoderResetPending;
    private PlaybackSchedulerSnapshot _lastSchedulerSnapshot;

    public PlaybackDecodedCompletionSchedulerIngressBoundary(
        PlaybackBoundedDemuxSession sourceSession,
        int decoderGeneration,
        int discontinuityGeneration,
        BoundedPlaybackScheduler scheduler,
        IDisposable decoderContextLifetime,
        long initialSchedulerSequence = 0)
        : this(
            sourceSession,
            decoderGeneration,
            discontinuityGeneration,
            scheduler,
            decoderContextLifetime,
            initialSchedulerSequence,
            new PlaybackDecodedCompletionSchedulerIngressOperations())
    {
    }

    internal PlaybackDecodedCompletionSchedulerIngressBoundary(
        PlaybackBoundedDemuxSession sourceSession,
        int decoderGeneration,
        int discontinuityGeneration,
        BoundedPlaybackScheduler scheduler,
        IDisposable decoderContextLifetime,
        long initialSchedulerSequence,
        IPlaybackDecodedCompletionSchedulerIngressOperations operations)
    {
        ArgumentNullException.ThrowIfNull(sourceSession);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(decoderContextLifetime);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentOutOfRangeException.ThrowIfNegative(decoderGeneration);
        ArgumentOutOfRangeException.ThrowIfNegative(discontinuityGeneration);
        ArgumentOutOfRangeException.ThrowIfNegative(initialSchedulerSequence);

        if (sourceSession.IsDisposed || scheduler.IsDisposed)
        {
            try
            {
                scheduler.Dispose();
            }
            finally
            {
                decoderContextLifetime.Dispose();
            }

            throw new ArgumentException(
                "The scheduler ingress generation requires a live source and scheduler.",
                nameof(sourceSession));
        }

        _sourceSession = sourceSession;
        _decoderGeneration = decoderGeneration;
        _discontinuityGeneration = discontinuityGeneration;
        _scheduler = scheduler;
        _decoderContextLifetime = decoderContextLifetime;
        _nextSchedulerSequence = initialSchedulerSequence;
        _decoderResetPending = discontinuityGeneration > 0;
        _operations = operations;
        _lastSchedulerSnapshot = scheduler.GetSnapshot();
    }

    public PlaybackDecodedCompletionSchedulerIngressSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new(
                    _state,
                    _result,
                    _decoderGeneration,
                    _discontinuityGeneration,
                    _nextVideoSourceSequence,
                    _nextAudioSourceSequence,
                    _nextSchedulerSequence,
                    _acceptedTransferCount,
                    _decoderResetPending,
                    _scheduler?.GetSnapshot() ?? _lastSchedulerSnapshot);
            }
        }
    }

    public PlaybackDecodedCompletionSchedulerIngressResult Transfer(
        PlaybackSourceBoundDecodedFrameCompletion? completion,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_state == PlaybackDecodedCompletionSchedulerIngressState.Disposed)
            {
                completion?.Dispose();
                return SetResult(PlaybackDecodedCompletionSchedulerIngressResult.Disposed);
            }

            if (_state == PlaybackDecodedCompletionSchedulerIngressState.Faulted)
            {
                completion?.Dispose();
                return _result;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return SetResult(PlaybackDecodedCompletionSchedulerIngressResult.Cancelled);
            }

            var scheduler = _scheduler;
            if (scheduler is null || !IsValidCompletion(completion))
            {
                return FailLocked(
                    PlaybackDecodedCompletionSchedulerIngressResult.InvalidCompletion,
                    completion);
            }

            if (_nextSchedulerSequence == long.MaxValue)
            {
                return FailLocked(
                    PlaybackDecodedCompletionSchedulerIngressResult.SequenceExhausted,
                    completion);
            }

            var nextSchedulerSequence = checked(_nextSchedulerSequence + 1);
            var nextSourceSequence = checked(completion!.Sequence + 1);
            var schedulerSequence = _nextSchedulerSequence;
            var discontinuity = _decoderResetPending
                ? PlaybackDecodedFrameDiscontinuityKind.DecoderReset
                : PlaybackDecodedFrameDiscontinuityKind.None;
            PlaybackDecoderFfmpegManagedFrame? frame = null;
            try
            {
                if (!completion.TryDetachFrame(out frame) || frame is null || frame.IsDisposed)
                {
                    frame?.Dispose();
                    completion.Dispose();
                    return FailLocked(
                        PlaybackDecodedCompletionSchedulerIngressResult.InvalidCompletion);
                }

                completion.Dispose();
                var enqueue = _operations.Enqueue(
                    scheduler,
                    frame,
                    schedulerSequence,
                    discontinuity);
                frame = null;
                if (!Enum.IsDefined(enqueue) || !IsAcceptedEnqueueResult(enqueue))
                {
                    return FailLocked(
                        PlaybackDecodedCompletionSchedulerIngressResult.EnqueueRejected);
                }

                _nextSchedulerSequence = nextSchedulerSequence;
                if (completion.MediaKind == PlaybackBoundedDemuxMediaKind.Video)
                {
                    _nextVideoSourceSequence = nextSourceSequence;
                }
                else
                {
                    _nextAudioSourceSequence = nextSourceSequence;
                }

                _acceptedTransferCount = SaturatingIncrement(_acceptedTransferCount);
                _decoderResetPending = false;
                _lastSchedulerSnapshot = scheduler.GetSnapshot();
                return SetResult(PlaybackDecodedCompletionSchedulerIngressResult.Transferred);
            }
            catch
            {
                frame?.Dispose();
                completion?.Dispose();
                return FailLocked(PlaybackDecodedCompletionSchedulerIngressResult.Faulted);
            }
        }
    }

    public void Dispose()
    {
        BoundedPlaybackScheduler? scheduler;
        IDisposable? decoderContextLifetime;
        lock (_sync)
        {
            if (_state == PlaybackDecodedCompletionSchedulerIngressState.Disposed)
            {
                return;
            }

            scheduler = _scheduler;
            decoderContextLifetime = _decoderContextLifetime;
            if (scheduler is not null)
            {
                _lastSchedulerSnapshot = scheduler.GetSnapshot();
            }

            _scheduler = null;
            _decoderContextLifetime = null;
            _state = PlaybackDecodedCompletionSchedulerIngressState.Disposed;
            _result = PlaybackDecodedCompletionSchedulerIngressResult.Disposed;
        }

        DisposeOwned(scheduler, decoderContextLifetime);
        GC.SuppressFinalize(this);
    }

    private bool IsValidCompletion(PlaybackSourceBoundDecodedFrameCompletion? completion)
    {
        if (completion is null || completion.IsDisposed || !completion.HasFrameOwnership ||
            _sourceSession.IsDisposed ||
            !completion.ReferencesSourceSession(_sourceSession) ||
            completion.DecoderGeneration != _decoderGeneration ||
            completion.DiscontinuityGeneration != _discontinuityGeneration ||
            !Enum.IsDefined(completion.MediaKind) ||
            completion.Sequence < 0 ||
            completion.Sequence == long.MaxValue ||
            completion.ManagedByteCount < 1)
        {
            return false;
        }

        var expectedStreamId = completion.MediaKind switch
        {
            PlaybackBoundedDemuxMediaKind.Video => _sourceSession.Video.StreamId,
            PlaybackBoundedDemuxMediaKind.Audio => _sourceSession.Audio.StreamId,
            _ => -1,
        };
        var expectedSourceSequence = completion.MediaKind switch
        {
            PlaybackBoundedDemuxMediaKind.Video => _nextVideoSourceSequence,
            PlaybackBoundedDemuxMediaKind.Audio => _nextAudioSourceSequence,
            _ => -1,
        };
        if (completion.StreamId != expectedStreamId ||
            completion.Sequence != expectedSourceSequence)
        {
            return false;
        }

        try
        {
            var frame = completion.Frame;
            var expectedFrameKind = completion.MediaKind == PlaybackBoundedDemuxMediaKind.Video
                ? PlaybackDecoderFfmpegFrameMediaKind.Video
                : PlaybackDecoderFfmpegFrameMediaKind.Audio;
            return !frame.IsDisposed &&
                frame.Metadata.MediaKind == expectedFrameKind &&
                TryGetManagedByteCount(frame, out var managedByteCount) &&
                managedByteCount == completion.ManagedByteCount;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private PlaybackDecodedCompletionSchedulerIngressResult FailLocked(
        PlaybackDecodedCompletionSchedulerIngressResult result,
        PlaybackSourceBoundDecodedFrameCompletion? completion = null)
    {
        try
        {
            completion?.Dispose();
        }
        catch
        {
            // Preserve the fixed terminal ingress state.
        }

        _state = PlaybackDecodedCompletionSchedulerIngressState.Faulted;
        _result = result;
        var scheduler = _scheduler;
        var decoderContextLifetime = _decoderContextLifetime;
        if (scheduler is not null)
        {
            _lastSchedulerSnapshot = scheduler.GetSnapshot();
        }

        _scheduler = null;
        _decoderContextLifetime = null;
        DisposeOwned(scheduler, decoderContextLifetime);
        return result;
    }

    private PlaybackDecodedCompletionSchedulerIngressResult SetResult(
        PlaybackDecodedCompletionSchedulerIngressResult result)
    {
        _result = result;
        return result;
    }

    private static bool IsAcceptedEnqueueResult(PlaybackSchedulerEnqueueResult result) =>
        result is PlaybackSchedulerEnqueueResult.Enqueued or
            PlaybackSchedulerEnqueueResult.EnqueuedAfterDroppingOldest;

    private static bool TryGetManagedByteCount(
        PlaybackDecoderFfmpegManagedFrame frame,
        out long managedByteCount)
    {
        managedByteCount = 0;
        if (frame.Planes.Count < 1)
        {
            return false;
        }

        try
        {
            foreach (var plane in frame.Planes)
            {
                if (plane.Buffer.IsEmpty)
                {
                    return false;
                }

                managedByteCount = checked(managedByteCount + plane.Buffer.Length);
            }

            return managedByteCount > 0 &&
                managedByteCount <=
                    PlaybackSourceBoundOneFrameDecodeOptions.MaximumManagedFrameBytesHardLimit;
        }
        catch (OverflowException)
        {
            managedByteCount = 0;
            return false;
        }
    }

    private static void DisposeOwned(
        BoundedPlaybackScheduler? scheduler,
        IDisposable? decoderContextLifetime)
    {
        try
        {
            scheduler?.Dispose();
        }
        catch
        {
            // Preserve the fixed terminal ingress state.
        }

        try
        {
            decoderContextLifetime?.Dispose();
        }
        catch
        {
            // Preserve the fixed terminal ingress state.
        }
    }

    private static long SaturatingIncrement(long value) =>
        value == long.MaxValue ? long.MaxValue : value + 1;
}
