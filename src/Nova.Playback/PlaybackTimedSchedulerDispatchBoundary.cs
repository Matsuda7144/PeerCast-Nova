namespace PeerCast.Nova.Playback;

public enum PlaybackTimedSchedulerDispatchState
{
    Ready,
    Waiting,
    Dispatching,
    Resetting,
    ResetRequired,
    Faulted,
    Disposed,
}

public enum PlaybackTimedSchedulerDispatchResult
{
    VideoProcessed,
    AudioProcessed,
    WaitLimitReached,
    VideoLate,
    AudioLate,
    Empty,
    WouldBlock,
    InitializationRequired,
    BindingRequired,
    ResizeRequired,
    DeviceLost,
    DiscontinuityResetRequired,
    GenerationMismatch,
    ClockNotStarted,
    InvalidItem,
    InvalidConfiguration,
    InvalidEnvironment,
    AlreadyRunning,
    Cancelled,
    Disposed,
    Failure,
}

public enum PlaybackTimedSchedulerResetResult
{
    Reset,
    Cancelled,
    Disposed,
    Failure,
    PartialResetFault,
}

public sealed record PlaybackTimedSchedulerDispatchOptions
{
    public const int MaximumDecisionCountLimit = 16;
    public static readonly TimeSpan MaximumTotalWaitLimit = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan MaximumSingleDelayLimit = TimeSpan.FromSeconds(1);

    public int MaximumDecisionCount { get; init; } = 2;
    public TimeSpan MaximumTotalWait { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan MinimumDelay { get; init; } = TimeSpan.FromMilliseconds(1);
    public TimeSpan MaximumSingleDelay { get; init; } = TimeSpan.FromMilliseconds(250);
}

public readonly record struct PlaybackTimedSchedulerDispatchSnapshot(
    long VideoGeneration,
    long AudioGeneration,
    bool PreferVideoNext,
    PlaybackTimedSchedulerDispatchState State);

public sealed class PlaybackTimedSchedulerDispatchBoundary : IDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IPlaybackWindowsOutputSchedulerSource _source;
    private readonly IPlaybackWindowsOutputBufferDispatcher _dispatcher;
    private readonly IPlaybackTimedSchedulerTimingDecider _timing;
    private readonly IPlaybackTimedSchedulerDelay _delay;
    private readonly Func<bool> _isSupportedPlatform;
    private CancellationTokenSource? _operationCancellation;
    private long _videoGeneration;
    private long _audioGeneration;
    private bool _preferVideoNext = true;
    private PlaybackTimedSchedulerDispatchState _state;
    private bool _isDisposed;

    public PlaybackTimedSchedulerDispatchBoundary(
        BoundedPlaybackScheduler scheduler,
        PlaybackAvTimingDecisionBoundary timing,
        PlaybackWindowsOutputBufferProcessingBoundary processor,
        long videoGeneration,
        long audioGeneration)
        : this(new BoundedPlaybackWindowsOutputSchedulerSource(scheduler),
            new PlaybackWindowsOutputBufferDispatcher(processor),
            new PlaybackTimedSchedulerTimingDecider(timing),
            new TaskPlaybackTimedSchedulerDelay(), videoGeneration, audioGeneration,
            static () => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22_000))
    {
    }

    internal PlaybackTimedSchedulerDispatchBoundary(
        IPlaybackWindowsOutputSchedulerSource source,
        IPlaybackWindowsOutputBufferDispatcher dispatcher,
        IPlaybackTimedSchedulerTimingDecider timing,
        IPlaybackTimedSchedulerDelay delay,
        long videoGeneration,
        long audioGeneration,
        Func<bool> isSupportedPlatform)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(timing);
        ArgumentNullException.ThrowIfNull(delay);
        ArgumentNullException.ThrowIfNull(isSupportedPlatform);
        ArgumentOutOfRangeException.ThrowIfNegative(videoGeneration);
        ArgumentOutOfRangeException.ThrowIfNegative(audioGeneration);
        _source = source;
        _dispatcher = dispatcher;
        _timing = timing;
        _delay = delay;
        _videoGeneration = videoGeneration;
        _audioGeneration = audioGeneration;
        _isSupportedPlatform = isSupportedPlatform;
    }

    public PlaybackTimedSchedulerDispatchSnapshot Snapshot
    {
        get
        {
            lock (_sync)
                return new(_videoGeneration, _audioGeneration, _preferVideoNext, _state);
        }
    }

    public async ValueTask<PlaybackTimedSchedulerDispatchResult> DispatchOneAsync(
        PlaybackTimedSchedulerDispatchOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!ValidateOptions(options))
            return PlaybackTimedSchedulerDispatchResult.InvalidConfiguration;
        CancellationTokenSource operation;
        lock (_sync)
        {
            if (_isDisposed) return PlaybackTimedSchedulerDispatchResult.Disposed;
            if (_state == PlaybackTimedSchedulerDispatchState.ResetRequired)
                return PlaybackTimedSchedulerDispatchResult.DiscontinuityResetRequired;
            if (_state == PlaybackTimedSchedulerDispatchState.Faulted)
                return PlaybackTimedSchedulerDispatchResult.Failure;
            if (_state != PlaybackTimedSchedulerDispatchState.Ready)
                return PlaybackTimedSchedulerDispatchResult.AlreadyRunning;
            if (!_isSupportedPlatform())
                return PlaybackTimedSchedulerDispatchResult.InvalidEnvironment;
            operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _operationCancellation = operation;
            _state = PlaybackTimedSchedulerDispatchState.Dispatching;
        }

        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            bool preferVideo;
            lock (_sync) preferVideo = _preferVideoNext;
            var preferred = preferVideo
                ? PlaybackSchedulerOutputKind.Video : PlaybackSchedulerOutputKind.Audio;
            var taken = await _source.ReserveAsync(preferred, operation.Token).ConfigureAwait(false);
            if (!Enum.IsDefined(taken.Result)) return Fault();
            if (taken.Result == PlaybackWindowsOutputSchedulerTakeResult.Empty)
                return PlaybackTimedSchedulerDispatchResult.Empty;
            if (taken.Result == PlaybackWindowsOutputSchedulerTakeResult.Cancelled)
                return PlaybackTimedSchedulerDispatchResult.Cancelled;
            if (taken.Result == PlaybackWindowsOutputSchedulerTakeResult.Disposed)
                return PlaybackTimedSchedulerDispatchResult.Disposed;
            if (taken.Result != PlaybackWindowsOutputSchedulerTakeResult.Reserved ||
                taken.Reservation is null)
                return Fault();

            using var reservation = taken.Reservation;
            var consumed = false;
            try
            {
                var result = await DecideAndDispatchAsync(
                    reservation, options, operation.Token).ConfigureAwait(false);
                consumed = result is PlaybackTimedSchedulerDispatchResult.VideoProcessed or
                    PlaybackTimedSchedulerDispatchResult.AudioProcessed;
                if (consumed)
                {
                    lock (_sync)
                        _preferVideoNext = result == PlaybackTimedSchedulerDispatchResult.AudioProcessed;
                }
                return result;
            }
            finally { reservation.Complete(consumed); }
        }
        catch (OperationCanceledException) { return PlaybackTimedSchedulerDispatchResult.Cancelled; }
        catch { return Fault(); }
        finally
        {
            _gate.Release();
            operation.Dispose();
            lock (_sync)
            {
                if (ReferenceEquals(_operationCancellation, operation))
                    _operationCancellation = null;
                if (!_isDisposed && _state is PlaybackTimedSchedulerDispatchState.Waiting or
                    PlaybackTimedSchedulerDispatchState.Dispatching)
                    _state = PlaybackTimedSchedulerDispatchState.Ready;
            }
        }
    }

    public async ValueTask<PlaybackTimedSchedulerResetResult> ResetAsync(
        long videoGeneration,
        long audioGeneration,
        CancellationToken cancellationToken = default)
    {
        if (videoGeneration < 0 || audioGeneration < 0)
            return PlaybackTimedSchedulerResetResult.Failure;

        long oldVideoGeneration;
        long oldAudioGeneration;
        lock (_sync)
        {
            if (_isDisposed) return PlaybackTimedSchedulerResetResult.Disposed;
            if (_state == PlaybackTimedSchedulerDispatchState.Resetting)
                return PlaybackTimedSchedulerResetResult.Failure;
            oldVideoGeneration = _videoGeneration;
            oldAudioGeneration = _audioGeneration;
            _state = PlaybackTimedSchedulerDispatchState.Resetting;
            _operationCancellation?.Cancel();
        }

        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            SetState(PlaybackTimedSchedulerDispatchState.ResetRequired);
            return PlaybackTimedSchedulerResetResult.Cancelled;
        }

        try
        {
            var audio = await InvokeOutputResetAsync(video: false, audioGeneration,
                cancellationToken).ConfigureAwait(false);
            if (!IsOutputResetSuccess(audio))
                return CommitSimpleResetFailure(MapOutputReset(audio));

            var video = await InvokeOutputResetAsync(video: true, videoGeneration,
                cancellationToken).ConfigureAwait(false);
            if (!IsOutputResetSuccess(video))
            {
                var audioRollback = await InvokeOutputResetAsync(video: false,
                    oldAudioGeneration, CancellationToken.None).ConfigureAwait(false);
                if (IsOutputResetSuccess(audioRollback))
                    return CommitSimpleResetFailure(MapOutputReset(video));

                lock (_sync) _audioGeneration = audioGeneration;
                SetState(PlaybackTimedSchedulerDispatchState.Faulted);
                return PlaybackTimedSchedulerResetResult.PartialResetFault;
            }

            var timing = await InvokeTimingResetAsync(videoGeneration, audioGeneration,
                cancellationToken).ConfigureAwait(false);
            if (timing != PlaybackAvTimingResetResult.Reset)
            {
                var timingRollback = await InvokeTimingResetAsync(oldVideoGeneration,
                    oldAudioGeneration, CancellationToken.None).ConfigureAwait(false);
                var videoRollback = await InvokeOutputResetAsync(video: true,
                    oldVideoGeneration, CancellationToken.None).ConfigureAwait(false);
                var audioRollback = await InvokeOutputResetAsync(video: false,
                    oldAudioGeneration, CancellationToken.None).ConfigureAwait(false);

                if (timingRollback == PlaybackAvTimingResetResult.Reset &&
                    IsOutputResetSuccess(videoRollback) && IsOutputResetSuccess(audioRollback))
                    return CommitSimpleResetFailure(MapTimingReset(timing));

                lock (_sync)
                {
                    if (!IsOutputResetSuccess(videoRollback)) _videoGeneration = videoGeneration;
                    if (!IsOutputResetSuccess(audioRollback)) _audioGeneration = audioGeneration;
                }
                SetState(PlaybackTimedSchedulerDispatchState.Faulted);
                return PlaybackTimedSchedulerResetResult.PartialResetFault;
            }

            lock (_sync)
            {
                if (_isDisposed) return PlaybackTimedSchedulerResetResult.Disposed;
                _videoGeneration = videoGeneration;
                _audioGeneration = audioGeneration;
                _preferVideoNext = true;
                _state = PlaybackTimedSchedulerDispatchState.Ready;
            }
            return PlaybackTimedSchedulerResetResult.Reset;
        }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _state = PlaybackTimedSchedulerDispatchState.Disposed;
            _operationCancellation?.Cancel();
        }
        _gate.Wait();
        _gate.Release();
    }

    private async ValueTask<PlaybackTimedSchedulerDispatchResult> DecideAndDispatchAsync(
        IPlaybackWindowsOutputSchedulerReservation reservation,
        PlaybackTimedSchedulerDispatchOptions options,
        CancellationToken cancellationToken)
    {
        if (!TryCreateTimingItem(reservation, out var item))
            return PlaybackTimedSchedulerDispatchResult.InvalidItem;
        var totalWait = TimeSpan.Zero;
        for (var decisionCount = 1; decisionCount <= options.MaximumDecisionCount; decisionCount++)
        {
            var decision = await _timing.DecideAsync(item, cancellationToken).ConfigureAwait(false);
            if (!Enum.IsDefined(decision.Result)) return Fault();
            if (decision.Result != PlaybackAvTimingDecisionResult.Wait)
            {
                var result = decision.Result == PlaybackAvTimingDecisionResult.ProcessNow
                    ? await DispatchAsync(reservation, cancellationToken).ConfigureAwait(false)
                    : MapDecision(decision.Result);
                if (result == PlaybackTimedSchedulerDispatchResult.DiscontinuityResetRequired)
                    SetState(PlaybackTimedSchedulerDispatchState.ResetRequired);
                return result == PlaybackTimedSchedulerDispatchResult.Failure ? Fault() : result;
            }
            if (decisionCount == options.MaximumDecisionCount ||
                decision.WaitDuration < options.MinimumDelay ||
                decision.WaitDuration > options.MaximumSingleDelay)
                return PlaybackTimedSchedulerDispatchResult.WaitLimitReached;
            TimeSpan nextTotal;
            try { nextTotal = totalWait + decision.WaitDuration; }
            catch (OverflowException) { return Fault(); }
            if (nextTotal > options.MaximumTotalWait)
                return PlaybackTimedSchedulerDispatchResult.WaitLimitReached;
            totalWait = nextTotal;
            SetState(PlaybackTimedSchedulerDispatchState.Waiting);
            await _delay.DelayAsync(decision.WaitDuration, cancellationToken).ConfigureAwait(false);
            SetState(PlaybackTimedSchedulerDispatchState.Dispatching);
        }
        return PlaybackTimedSchedulerDispatchResult.WaitLimitReached;
    }

    private bool TryCreateTimingItem(
        IPlaybackWindowsOutputSchedulerReservation reservation,
        out PlaybackAvTimingItem item)
    {
        item = default;
        if (!Enum.IsDefined(reservation.Kind) || reservation.Sequence < 0 ||
            !Enum.IsDefined(reservation.Discontinuity) || reservation.Frame.IsDisposed)
            return false;
        var metadata = reservation.Frame.Metadata;
        if (!metadata.PresentationTimestamp.IsKnown ||
            metadata.PresentationTimestamp.Value < TimeSpan.Zero)
            return false;
        var kind = reservation.Kind == PlaybackSchedulerOutputKind.Video
            ? PlaybackPolicyMediaKind.Video : PlaybackPolicyMediaKind.Audio;
        long generation;
        lock (_sync)
            generation = kind == PlaybackPolicyMediaKind.Video
                ? _videoGeneration : _audioGeneration;
        item = new(kind, ToPolicyTime(metadata.PresentationTimestamp),
            ToPolicyTime(metadata.Duration), reservation.Sequence,
            reservation.Discontinuity, generation);
        return true;
    }

    private static PlaybackPolicyTime ToPolicyTime(PlaybackDecoderFfmpegFrameTime value) =>
        value.IsKnown ? PlaybackPolicyTime.Known(value.Value) : PlaybackPolicyTime.Unknown;

    private async ValueTask<PlaybackTimedSchedulerDispatchResult> DispatchAsync(
        IPlaybackWindowsOutputSchedulerReservation reservation,
        CancellationToken cancellationToken)
    {
        var metadata = reservation.Frame.Metadata;
        if (reservation.Frame.Planes.Count != 1)
            return PlaybackTimedSchedulerDispatchResult.InvalidItem;
        var plane = reservation.Frame.Planes[0];
        long videoGeneration;
        long audioGeneration;
        lock (_sync)
        {
            videoGeneration = _videoGeneration;
            audioGeneration = _audioGeneration;
        }

        PlaybackWindowsOutputBufferProcessingResult result;
        if (reservation.Kind == PlaybackSchedulerOutputKind.Video)
        {
            if (metadata.MediaKind != PlaybackDecoderFfmpegFrameMediaKind.Video ||
                metadata.Format != PlaybackDecoderFfmpegFrameFormat.VideoBgra32 ||
                metadata.Width <= 0 || metadata.Height <= 0 || plane.Index != 0 ||
                plane.Rows != metadata.Height || plane.Stride != metadata.Width * 4 ||
                plane.LogicalRowBytes != plane.Stride ||
                plane.Buffer.Length != (long)plane.Stride * plane.Rows)
                return PlaybackTimedSchedulerDispatchResult.InvalidItem;
            result = await _dispatcher.ProcessVideoAsync(new(
                PlaybackDecodedVideoPixelFormat.Bgra32, metadata.Width, metadata.Height,
                plane.Stride, plane.Buffer, videoGeneration), cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            var format = metadata.Format switch
            {
                PlaybackDecoderFfmpegFrameFormat.AudioSigned16 =>
                    PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
                PlaybackDecoderFfmpegFrameFormat.AudioFloat32 =>
                    PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved,
                _ => (PlaybackDecodedAudioSampleFormat)(-1),
            };
            var bytes = format == PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved ? 2 : 4;
            if (metadata.MediaKind != PlaybackDecoderFfmpegFrameMediaKind.Audio ||
                !Enum.IsDefined(format) || metadata.SampleRate <= 0 ||
                metadata.ChannelCount <= 0 || metadata.SampleCount <= 0 ||
                plane.Index != 0 || plane.Rows != 1 ||
                plane.LogicalRowBytes != metadata.SampleCount * metadata.ChannelCount * bytes ||
                plane.Stride != plane.LogicalRowBytes ||
                plane.Buffer.Length != plane.LogicalRowBytes)
                return PlaybackTimedSchedulerDispatchResult.InvalidItem;
            result = await _dispatcher.ProcessAudioAsync(new(format, metadata.SampleRate,
                metadata.ChannelCount, metadata.SampleCount, plane.Buffer, audioGeneration),
                cancellationToken).ConfigureAwait(false);
        }
        return MapDispatch(result, reservation.Kind);
    }

    private async ValueTask<PlaybackWindowsOutputBufferResetResult> InvokeOutputResetAsync(
        bool video, long generation, CancellationToken cancellationToken)
    {
        try
        {
            var result = video
                ? await _dispatcher.ResetVideoAsync(generation, cancellationToken).ConfigureAwait(false)
                : await _dispatcher.ResetAudioAsync(generation, cancellationToken).ConfigureAwait(false);
            return Enum.IsDefined(result) ? result : PlaybackWindowsOutputBufferResetResult.Failure;
        }
        catch (OperationCanceledException) { return PlaybackWindowsOutputBufferResetResult.Cancelled; }
        catch { return PlaybackWindowsOutputBufferResetResult.Failure; }
    }

    private async ValueTask<PlaybackAvTimingResetResult> InvokeTimingResetAsync(
        long videoGeneration, long audioGeneration, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _timing.ResetAsync(videoGeneration, audioGeneration,
                cancellationToken).ConfigureAwait(false);
            return Enum.IsDefined(result) ? result : PlaybackAvTimingResetResult.Failure;
        }
        catch (OperationCanceledException) { return PlaybackAvTimingResetResult.Cancelled; }
        catch { return PlaybackAvTimingResetResult.Failure; }
    }

    private PlaybackTimedSchedulerResetResult CommitSimpleResetFailure(
        PlaybackTimedSchedulerResetResult result)
    {
        SetState(result is PlaybackTimedSchedulerResetResult.Cancelled or
            PlaybackTimedSchedulerResetResult.PartialResetFault
            ? PlaybackTimedSchedulerDispatchState.ResetRequired
            : PlaybackTimedSchedulerDispatchState.Faulted);
        return result;
    }

    private PlaybackTimedSchedulerDispatchResult Fault()
    {
        SetState(PlaybackTimedSchedulerDispatchState.Faulted);
        return PlaybackTimedSchedulerDispatchResult.Failure;
    }

    private void SetState(PlaybackTimedSchedulerDispatchState state)
    {
        lock (_sync)
        {
            if (!_isDisposed) _state = state;
        }
    }

    private static bool IsOutputResetSuccess(PlaybackWindowsOutputBufferResetResult result) =>
        result is PlaybackWindowsOutputBufferResetResult.Reset or
            PlaybackWindowsOutputBufferResetResult.DeviceLost;

    private static PlaybackTimedSchedulerResetResult MapOutputReset(
        PlaybackWindowsOutputBufferResetResult result) => result switch
        {
            PlaybackWindowsOutputBufferResetResult.Cancelled => PlaybackTimedSchedulerResetResult.Cancelled,
            PlaybackWindowsOutputBufferResetResult.Disposed => PlaybackTimedSchedulerResetResult.Disposed,
            _ => PlaybackTimedSchedulerResetResult.Failure,
        };

    private static PlaybackTimedSchedulerResetResult MapTimingReset(
        PlaybackAvTimingResetResult result) => result switch
        {
            PlaybackAvTimingResetResult.Cancelled => PlaybackTimedSchedulerResetResult.Cancelled,
            PlaybackAvTimingResetResult.Disposed => PlaybackTimedSchedulerResetResult.Disposed,
            PlaybackAvTimingResetResult.PartialResetFault => PlaybackTimedSchedulerResetResult.PartialResetFault,
            _ => PlaybackTimedSchedulerResetResult.Failure,
        };

    private static bool ValidateOptions(PlaybackTimedSchedulerDispatchOptions? options) =>
        options is not null && options.MaximumDecisionCount is >= 1 and <=
            PlaybackTimedSchedulerDispatchOptions.MaximumDecisionCountLimit &&
        options.MaximumTotalWait > TimeSpan.Zero &&
        options.MaximumTotalWait <= PlaybackTimedSchedulerDispatchOptions.MaximumTotalWaitLimit &&
        options.MinimumDelay > TimeSpan.Zero &&
        options.MaximumSingleDelay >= options.MinimumDelay &&
        options.MaximumSingleDelay <= PlaybackTimedSchedulerDispatchOptions.MaximumSingleDelayLimit &&
        options.MaximumSingleDelay <= options.MaximumTotalWait;

    private static PlaybackTimedSchedulerDispatchResult MapDecision(
        PlaybackAvTimingDecisionResult result) => result switch
        {
            PlaybackAvTimingDecisionResult.VideoLate => PlaybackTimedSchedulerDispatchResult.VideoLate,
            PlaybackAvTimingDecisionResult.AudioLate => PlaybackTimedSchedulerDispatchResult.AudioLate,
            PlaybackAvTimingDecisionResult.DiscontinuityResetRequired => PlaybackTimedSchedulerDispatchResult.DiscontinuityResetRequired,
            PlaybackAvTimingDecisionResult.GenerationMismatch => PlaybackTimedSchedulerDispatchResult.GenerationMismatch,
            PlaybackAvTimingDecisionResult.ClockNotStarted => PlaybackTimedSchedulerDispatchResult.ClockNotStarted,
            PlaybackAvTimingDecisionResult.WaitLimitExceeded => PlaybackTimedSchedulerDispatchResult.WaitLimitReached,
            PlaybackAvTimingDecisionResult.InvalidTimestamp => PlaybackTimedSchedulerDispatchResult.InvalidItem,
            PlaybackAvTimingDecisionResult.InvalidConfiguration => PlaybackTimedSchedulerDispatchResult.InvalidConfiguration,
            PlaybackAvTimingDecisionResult.Cancelled => PlaybackTimedSchedulerDispatchResult.Cancelled,
            PlaybackAvTimingDecisionResult.Disposed => PlaybackTimedSchedulerDispatchResult.Disposed,
            _ => PlaybackTimedSchedulerDispatchResult.Failure,
        };

    private static PlaybackTimedSchedulerDispatchResult MapDispatch(
        PlaybackWindowsOutputBufferProcessingResult result,
        PlaybackSchedulerOutputKind kind) => result switch
        {
            PlaybackWindowsOutputBufferProcessingResult.Processed =>
                kind == PlaybackSchedulerOutputKind.Video
                    ? PlaybackTimedSchedulerDispatchResult.VideoProcessed
                    : PlaybackTimedSchedulerDispatchResult.AudioProcessed,
            PlaybackWindowsOutputBufferProcessingResult.WouldBlock => PlaybackTimedSchedulerDispatchResult.WouldBlock,
            PlaybackWindowsOutputBufferProcessingResult.InitializationRequired => PlaybackTimedSchedulerDispatchResult.InitializationRequired,
            PlaybackWindowsOutputBufferProcessingResult.BindingRequired => PlaybackTimedSchedulerDispatchResult.BindingRequired,
            PlaybackWindowsOutputBufferProcessingResult.ResizeRequired => PlaybackTimedSchedulerDispatchResult.ResizeRequired,
            PlaybackWindowsOutputBufferProcessingResult.DeviceLost => PlaybackTimedSchedulerDispatchResult.DeviceLost,
            PlaybackWindowsOutputBufferProcessingResult.FormatUnsupported => PlaybackTimedSchedulerDispatchResult.InvalidItem,
            PlaybackWindowsOutputBufferProcessingResult.InvalidConfiguration => PlaybackTimedSchedulerDispatchResult.InvalidItem,
            PlaybackWindowsOutputBufferProcessingResult.InvalidEnvironment => PlaybackTimedSchedulerDispatchResult.InvalidEnvironment,
            PlaybackWindowsOutputBufferProcessingResult.Cancelled => PlaybackTimedSchedulerDispatchResult.Cancelled,
            PlaybackWindowsOutputBufferProcessingResult.Disposed => PlaybackTimedSchedulerDispatchResult.Disposed,
            _ => PlaybackTimedSchedulerDispatchResult.Failure,
        };
}

internal interface IPlaybackTimedSchedulerTimingDecider
{
    ValueTask<PlaybackAvTimingDecision> DecideAsync(
        PlaybackAvTimingItem item, CancellationToken cancellationToken);
    ValueTask<PlaybackAvTimingResetResult> ResetAsync(
        long videoGeneration, long audioGeneration, CancellationToken cancellationToken);
}

internal sealed class PlaybackTimedSchedulerTimingDecider(
    PlaybackAvTimingDecisionBoundary timing) : IPlaybackTimedSchedulerTimingDecider
{
    public ValueTask<PlaybackAvTimingDecision> DecideAsync(
        PlaybackAvTimingItem item, CancellationToken cancellationToken) =>
        timing.DecideAsync(item, cancellationToken);
    public ValueTask<PlaybackAvTimingResetResult> ResetAsync(
        long videoGeneration, long audioGeneration, CancellationToken cancellationToken) =>
        timing.ResetAsync(videoGeneration, audioGeneration, cancellationToken);
}

internal interface IPlaybackTimedSchedulerDelay
{
    ValueTask DelayAsync(TimeSpan duration, CancellationToken cancellationToken);
}

internal sealed class TaskPlaybackTimedSchedulerDelay : IPlaybackTimedSchedulerDelay
{
    public async ValueTask DelayAsync(TimeSpan duration, CancellationToken cancellationToken) =>
        await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
}
