namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffRegularMovieSchedulerIngressCreationResult
{
    Created,
    MissingProgress,
    MissingScheduler,
    InvalidConfiguration,
    ProgressUnavailable,
    SchedulerUnavailable,
    ActiveFrame,
}

public enum PlaybackIsoBmffRegularMovieSchedulerIngressState
{
    Accepting,
    Faulted,
    Disposed,
}

public enum PlaybackIsoBmffRegularMovieSchedulerIngressResult
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

public sealed record PlaybackIsoBmffRegularMovieSchedulerIngressSnapshot(
    PlaybackIsoBmffRegularMovieSchedulerIngressState State,
    PlaybackIsoBmffRegularMovieSchedulerIngressResult Result,
    long NextFrameSequence,
    long NextSchedulerSequence,
    long AcceptedTransferCount,
    PlaybackSchedulerSnapshot Scheduler);

internal interface IPlaybackIsoBmffRegularMovieSchedulerIngressOperations
{
    PlaybackSchedulerEnqueueResult Enqueue(
        BoundedPlaybackScheduler scheduler,
        PlaybackDecoderFfmpegManagedFrame frame,
        long sequence,
        PlaybackDecodedFrameDiscontinuityKind discontinuity);
}

internal sealed class PlaybackIsoBmffRegularMovieSchedulerIngressOperations :
    IPlaybackIsoBmffRegularMovieSchedulerIngressOperations
{
    public PlaybackSchedulerEnqueueResult Enqueue(
        BoundedPlaybackScheduler scheduler,
        PlaybackDecoderFfmpegManagedFrame frame,
        long sequence,
        PlaybackDecodedFrameDiscontinuityKind discontinuity) =>
        scheduler.Enqueue(frame, sequence, discontinuity);
}

public sealed class PlaybackIsoBmffRegularMovieSchedulerIngress :
    IDisposable,
    IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly IPlaybackIsoBmffRegularMovieSchedulerIngressOperations
        _operations;
    private PlaybackIsoBmffRegularMovieNativeDecodeProgress? _progress;
    private BoundedPlaybackScheduler? _scheduler;
    private PlaybackIsoBmffRegularMovieSchedulerIngressState _state =
        PlaybackIsoBmffRegularMovieSchedulerIngressState.Accepting;
    private PlaybackIsoBmffRegularMovieSchedulerIngressResult _result =
        PlaybackIsoBmffRegularMovieSchedulerIngressResult.None;
    private long _nextFrameSequence;
    private long _nextSchedulerSequence;
    private long _acceptedTransferCount;
    private PlaybackSchedulerSnapshot _lastSchedulerSnapshot;

    internal PlaybackIsoBmffRegularMovieSchedulerIngress(
        PlaybackIsoBmffRegularMovieNativeDecodeProgress progress,
        BoundedPlaybackScheduler scheduler,
        long initialSchedulerSequence,
        IPlaybackIsoBmffRegularMovieSchedulerIngressOperations operations)
    {
        _progress = progress;
        _scheduler = scheduler;
        _nextSchedulerSequence = initialSchedulerSequence;
        _operations = operations;
        _lastSchedulerSnapshot = scheduler.GetSnapshot();
    }

    public PlaybackIsoBmffRegularMovieSchedulerIngressSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new(
                    _state,
                    _result,
                    _nextFrameSequence,
                    _nextSchedulerSequence,
                    _acceptedTransferCount,
                    _scheduler?.GetSnapshot() ?? _lastSchedulerSnapshot);
            }
        }
    }

    internal BoundedPlaybackScheduler Scheduler
    {
        get
        {
            lock (_sync)
            {
                return _scheduler ??
                    throw new ObjectDisposedException(
                        nameof(
                            PlaybackIsoBmffRegularMovieSchedulerIngress));
            }
        }
    }

    internal PlaybackIsoBmffRegularMovieNativeDecoderSession SourceSession
    {
        get
        {
            lock (_sync)
            {
                return _progress?.SourceSession ??
                    throw new ObjectDisposedException(
                        nameof(
                            PlaybackIsoBmffRegularMovieSchedulerIngress));
            }
        }
    }

    public PlaybackIsoBmffRegularMovieSchedulerIngressResult Transfer(
        PlaybackIsoBmffRegularMovieNativeDecodeProgressAdvance? advance,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_state ==
                PlaybackIsoBmffRegularMovieSchedulerIngressState.Disposed)
            {
                advance?.Frame?.Dispose();
                return SetResult(
                    PlaybackIsoBmffRegularMovieSchedulerIngressResult
                        .Disposed);
            }

            if (_state ==
                PlaybackIsoBmffRegularMovieSchedulerIngressState.Faulted)
            {
                advance?.Frame?.Dispose();
                return _result;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return SetResult(
                    PlaybackIsoBmffRegularMovieSchedulerIngressResult
                        .Cancelled);
            }

            var progress = _progress;
            var scheduler = _scheduler;
            var completion = advance?.Frame;
            if (progress is null || scheduler is null ||
                advance?.Result !=
                    PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                        .FrameCopied ||
                completion is null || completion.IsDisposed ||
                !progress.Owns(completion) ||
                completion.Sequence != _nextFrameSequence ||
                !IsRoleConsistent(completion))
            {
                return Fail(
                    PlaybackIsoBmffRegularMovieSchedulerIngressResult
                        .InvalidCompletion,
                    completion);
            }

            if (_nextFrameSequence == long.MaxValue ||
                _nextSchedulerSequence == long.MaxValue ||
                _acceptedTransferCount == long.MaxValue)
            {
                return Fail(
                    PlaybackIsoBmffRegularMovieSchedulerIngressResult
                        .SequenceExhausted,
                    completion);
            }

            PlaybackDecoderFfmpegManagedFrame? frame = null;
            try
            {
                if (!completion.TryDetachFrame(out frame) || frame is null ||
                    frame.IsDisposed)
                {
                    frame?.Dispose();
                    completion.Dispose();
                    return Fail(
                        PlaybackIsoBmffRegularMovieSchedulerIngressResult
                            .InvalidCompletion);
                }

                completion.Dispose();
                var enqueue = _operations.Enqueue(
                    scheduler,
                    frame,
                    _nextSchedulerSequence,
                    PlaybackDecodedFrameDiscontinuityKind.None);
                frame = null;
                if (enqueue is not PlaybackSchedulerEnqueueResult.Enqueued and
                    not PlaybackSchedulerEnqueueResult
                        .EnqueuedAfterDroppingOldest)
                {
                    return Fail(
                        PlaybackIsoBmffRegularMovieSchedulerIngressResult
                            .EnqueueRejected);
                }

                _nextFrameSequence++;
                _nextSchedulerSequence++;
                _acceptedTransferCount++;
                _lastSchedulerSnapshot = scheduler.GetSnapshot();
                return SetResult(
                    PlaybackIsoBmffRegularMovieSchedulerIngressResult
                        .Transferred);
            }
            catch
            {
                frame?.Dispose();
                completion.Dispose();
                return Fail(
                    PlaybackIsoBmffRegularMovieSchedulerIngressResult.Faulted);
            }
        }
    }

    public void Dispose()
    {
        BoundedPlaybackScheduler? scheduler;
        PlaybackIsoBmffRegularMovieNativeDecodeProgress? progress;
        lock (_sync)
        {
            if (_state ==
                PlaybackIsoBmffRegularMovieSchedulerIngressState.Disposed)
            {
                return;
            }

            scheduler = _scheduler;
            progress = _progress;
            if (scheduler is not null)
            {
                _lastSchedulerSnapshot = scheduler.GetSnapshot();
            }

            _scheduler = null;
            _progress = null;
            _state =
                PlaybackIsoBmffRegularMovieSchedulerIngressState.Disposed;
            _result =
                PlaybackIsoBmffRegularMovieSchedulerIngressResult.Disposed;
        }

        DisposeOwned(scheduler, progress);
        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private PlaybackIsoBmffRegularMovieSchedulerIngressResult Fail(
        PlaybackIsoBmffRegularMovieSchedulerIngressResult result,
        PlaybackIsoBmffRegularMovieNativeDecodedFrameCompletion? completion =
            null)
    {
        try
        {
            completion?.Dispose();
        }
        catch
        {
            // Preserve the fixed terminal ingress state.
        }

        _state = PlaybackIsoBmffRegularMovieSchedulerIngressState.Faulted;
        _result = result;
        var scheduler = _scheduler;
        var progress = _progress;
        if (scheduler is not null)
        {
            _lastSchedulerSnapshot = scheduler.GetSnapshot();
        }

        _scheduler = null;
        _progress = null;
        DisposeOwned(scheduler, progress);
        return result;
    }

    private PlaybackIsoBmffRegularMovieSchedulerIngressResult SetResult(
        PlaybackIsoBmffRegularMovieSchedulerIngressResult result)
    {
        _result = result;
        return result;
    }

    private static bool IsRoleConsistent(
        PlaybackIsoBmffRegularMovieNativeDecodedFrameCompletion completion)
    {
        try
        {
            var expected = completion.Role ==
                PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Video
                    ? PlaybackDecoderFfmpegFrameMediaKind.Video
                    : PlaybackDecoderFfmpegFrameMediaKind.Audio;
            return Enum.IsDefined(completion.Role) &&
                !completion.Frame.IsDisposed &&
                completion.Frame.Metadata.MediaKind == expected;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static void DisposeOwned(
        BoundedPlaybackScheduler? scheduler,
        PlaybackIsoBmffRegularMovieNativeDecodeProgress? progress)
    {
        try
        {
            scheduler?.Dispose();
        }
        finally
        {
            progress?.Dispose();
        }
    }
}

public sealed record PlaybackIsoBmffRegularMovieSchedulerIngressCreation(
    PlaybackIsoBmffRegularMovieSchedulerIngressCreationResult Result,
    PlaybackIsoBmffRegularMovieSchedulerIngress? Ingress);

public sealed class PlaybackIsoBmffRegularMovieSchedulerIngressFactory
{
    private readonly IPlaybackIsoBmffRegularMovieSchedulerIngressOperations
        _operations;

    public PlaybackIsoBmffRegularMovieSchedulerIngressFactory()
        : this(new PlaybackIsoBmffRegularMovieSchedulerIngressOperations())
    {
    }

    internal PlaybackIsoBmffRegularMovieSchedulerIngressFactory(
        IPlaybackIsoBmffRegularMovieSchedulerIngressOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations;
    }

    public PlaybackIsoBmffRegularMovieSchedulerIngressCreation Create(
        PlaybackIsoBmffRegularMovieNativeDecodeProgress? progress,
        BoundedPlaybackScheduler? scheduler,
        long initialSchedulerSequence = 0)
    {
        if (progress is null)
        {
            scheduler?.Dispose();
            return new(
                PlaybackIsoBmffRegularMovieSchedulerIngressCreationResult
                    .MissingProgress,
                null);
        }

        if (scheduler is null)
        {
            progress.Dispose();
            return new(
                PlaybackIsoBmffRegularMovieSchedulerIngressCreationResult
                    .MissingScheduler,
                null);
        }

        if (initialSchedulerSequence < 0)
        {
            DisposeOwned(scheduler, progress);
            return new(
                PlaybackIsoBmffRegularMovieSchedulerIngressCreationResult
                    .InvalidConfiguration,
                null);
        }

        if (progress.IsDisposed)
        {
            DisposeOwned(scheduler, progress);
            return new(
                PlaybackIsoBmffRegularMovieSchedulerIngressCreationResult
                    .ProgressUnavailable,
                null);
        }

        if (scheduler.IsDisposed)
        {
            DisposeOwned(scheduler, progress);
            return new(
                PlaybackIsoBmffRegularMovieSchedulerIngressCreationResult
                    .SchedulerUnavailable,
                null);
        }

        if (progress.HasActiveFrame)
        {
            DisposeOwned(scheduler, progress);
            return new(
                PlaybackIsoBmffRegularMovieSchedulerIngressCreationResult
                    .ActiveFrame,
                null);
        }

        return new(
            PlaybackIsoBmffRegularMovieSchedulerIngressCreationResult.Created,
            new PlaybackIsoBmffRegularMovieSchedulerIngress(
                progress,
                scheduler,
                initialSchedulerSequence,
                _operations));
    }

    private static void DisposeOwned(
        BoundedPlaybackScheduler scheduler,
        PlaybackIsoBmffRegularMovieNativeDecodeProgress progress)
    {
        try
        {
            scheduler.Dispose();
        }
        finally
        {
            progress.Dispose();
        }
    }
}
