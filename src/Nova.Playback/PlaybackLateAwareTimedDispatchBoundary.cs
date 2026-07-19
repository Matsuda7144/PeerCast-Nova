namespace PeerCast.Nova.Playback;

public enum PlaybackLateAwareTimedDispatchState
{
    Ready,
    Running,
    ResetRequired,
    Faulted,
    Disposed,
}

public enum PlaybackLateAwareTimedDispatchResult
{
    VideoProcessed,
    AudioProcessed,
    RetainedVideoLate,
    RetainedAudioLate,
    VideoDropConsumed,
    AudioRecoveryRequired,
    WaitLimitReached,
    Empty,
    WouldBlock,
    ClockNotStarted,
    InitializationRequired,
    BindingRequired,
    ResizeRequired,
    DeviceLost,
    ResetRequired,
    InvalidItem,
    InvalidConfiguration,
    InvalidEnvironment,
    AlreadyRunning,
    Cancelled,
    Disposed,
    Failure,
}

public readonly record struct PlaybackLateAwareTimedDispatchSnapshot(
    long VideoGeneration,
    long AudioGeneration,
    bool PreferVideoNext,
    PlaybackLateAwareTimedDispatchState State);

public sealed class PlaybackLateAwareTimedDispatchBoundary : IDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IPlaybackWindowsOutputSchedulerSource _source;
    private readonly IPlaybackWindowsOutputBufferDispatcher _dispatcher;
    private readonly IPlaybackTimedSchedulerTimingDecider _timing;
    private readonly IPlaybackTimedSchedulerDelay _delay;
    private readonly PlaybackLateReservationDispositionBoundary _disposition;
    private readonly Func<bool> _isSupportedPlatform;
    private CancellationTokenSource? _operationCancellation;
    private long _videoGeneration;
    private long _audioGeneration;
    private bool _preferVideoNext = true;
    private PlaybackLateAwareTimedDispatchState _state;
    private bool _isDisposed;

    public PlaybackLateAwareTimedDispatchBoundary(
        BoundedPlaybackScheduler scheduler,
        PlaybackAvTimingDecisionBoundary timing,
        PlaybackWindowsOutputBufferProcessingBoundary processor,
        PlaybackLateReservationDispositionBoundary disposition,
        long videoGeneration,
        long audioGeneration)
        : this(new BoundedPlaybackWindowsOutputSchedulerSource(scheduler),
            new PlaybackWindowsOutputBufferDispatcher(processor),
            new PlaybackTimedSchedulerTimingDecider(timing),
            new TaskPlaybackTimedSchedulerDelay(), disposition,
            videoGeneration, audioGeneration,
            static () => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22_000))
    {
    }

    internal PlaybackLateAwareTimedDispatchBoundary(
        IPlaybackWindowsOutputSchedulerSource source,
        IPlaybackWindowsOutputBufferDispatcher dispatcher,
        IPlaybackTimedSchedulerTimingDecider timing,
        IPlaybackTimedSchedulerDelay delay,
        PlaybackLateReservationDispositionBoundary disposition,
        long videoGeneration,
        long audioGeneration,
        Func<bool> isSupportedPlatform)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(timing);
        ArgumentNullException.ThrowIfNull(delay);
        ArgumentNullException.ThrowIfNull(disposition);
        ArgumentNullException.ThrowIfNull(isSupportedPlatform);
        ArgumentOutOfRangeException.ThrowIfNegative(videoGeneration);
        ArgumentOutOfRangeException.ThrowIfNegative(audioGeneration);
        _source = source;
        _dispatcher = dispatcher;
        _timing = timing;
        _delay = delay;
        _disposition = disposition;
        _videoGeneration = videoGeneration;
        _audioGeneration = audioGeneration;
        _isSupportedPlatform = isSupportedPlatform;
    }

    public PlaybackLateAwareTimedDispatchSnapshot Snapshot
    {
        get
        {
            lock (_sync)
                return new(_videoGeneration, _audioGeneration, _preferVideoNext, _state);
        }
    }

    public async ValueTask<PlaybackLateAwareTimedDispatchResult> DispatchOneAsync(
        PlaybackTimedSchedulerDispatchOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!ValidateOptions(options))
            return PlaybackLateAwareTimedDispatchResult.InvalidConfiguration;
        lock (_sync)
        {
            if (_isDisposed) return PlaybackLateAwareTimedDispatchResult.Disposed;
            if (_state == PlaybackLateAwareTimedDispatchState.ResetRequired)
                return PlaybackLateAwareTimedDispatchResult.ResetRequired;
            if (_state == PlaybackLateAwareTimedDispatchState.Faulted)
                return PlaybackLateAwareTimedDispatchResult.Failure;
            if (!_isSupportedPlatform())
                return PlaybackLateAwareTimedDispatchResult.InvalidEnvironment;
        }
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return PlaybackLateAwareTimedDispatchResult.AlreadyRunning;

        CancellationTokenSource operation;
        lock (_sync)
        {
            if (_isDisposed)
            {
                _gate.Release();
                return PlaybackLateAwareTimedDispatchResult.Disposed;
            }
            _state = PlaybackLateAwareTimedDispatchState.Running;
            operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _operationCancellation = operation;
        }

        IPlaybackWindowsOutputSchedulerReservation? reservation = null;
        var completed = false;
        try
        {
            PlaybackSchedulerOutputKind preferred;
            lock (_sync)
                preferred = _preferVideoNext
                    ? PlaybackSchedulerOutputKind.Video
                    : PlaybackSchedulerOutputKind.Audio;
            var taken = await _source.ReserveAsync(preferred, operation.Token).ConfigureAwait(false);
            if (!Enum.IsDefined(taken.Result)) return Fault();
            if (taken.Result == PlaybackWindowsOutputSchedulerTakeResult.Empty)
                return PlaybackLateAwareTimedDispatchResult.Empty;
            if (taken.Result == PlaybackWindowsOutputSchedulerTakeResult.Cancelled)
                return PlaybackLateAwareTimedDispatchResult.Cancelled;
            if (taken.Result == PlaybackWindowsOutputSchedulerTakeResult.Disposed)
                return PlaybackLateAwareTimedDispatchResult.Disposed;
            if (taken.Result != PlaybackWindowsOutputSchedulerTakeResult.Reserved ||
                taken.Reservation is null)
                return Fault();

            reservation = taken.Reservation;
            if (!TryCreateTimingItem(reservation, out var item, out var generation))
                return PlaybackLateAwareTimedDispatchResult.InvalidItem;

            var totalWait = TimeSpan.Zero;
            for (var count = 1; count <= options.MaximumDecisionCount; count++)
            {
                var decision = await _timing.DecideAsync(item, operation.Token).ConfigureAwait(false);
                if (!Enum.IsDefined(decision.Result)) return Fault();
                switch (decision.Result)
                {
                    case PlaybackAvTimingDecisionResult.ProcessNow:
                        {
                            var result = await DispatchAsync(reservation, operation.Token)
                                .ConfigureAwait(false);
                            if (result == PlaybackLateAwareTimedDispatchResult.Failure)
                                return Fault();
                            if (result is PlaybackLateAwareTimedDispatchResult.VideoProcessed or
                                PlaybackLateAwareTimedDispatchResult.AudioProcessed)
                            {
                                reservation.Complete(true);
                                completed = true;
                                lock (_sync)
                                    _preferVideoNext = result == PlaybackLateAwareTimedDispatchResult.AudioProcessed;
                            }
                            return result;
                        }
                    case PlaybackAvTimingDecisionResult.VideoLate:
                    case PlaybackAvTimingDecisionResult.AudioLate:
                        {
                            var dispatchResult = decision.Result == PlaybackAvTimingDecisionResult.VideoLate
                                ? PlaybackTimedSchedulerDispatchResult.VideoLate
                                : PlaybackTimedSchedulerDispatchResult.AudioLate;
                            var disposition = _disposition.Apply(reservation, dispatchResult, generation);
                            completed = true;
                            if (disposition == PlaybackLateReservationDispositionResult.VideoDropConsumed)
                            {
                                lock (_sync) _preferVideoNext = false;
                            }
                            return MapDisposition(disposition);
                        }
                    case PlaybackAvTimingDecisionResult.Wait:
                        if (count == options.MaximumDecisionCount ||
                            decision.WaitDuration < options.MinimumDelay ||
                            decision.WaitDuration > options.MaximumSingleDelay)
                            return PlaybackLateAwareTimedDispatchResult.WaitLimitReached;
                        TimeSpan nextTotal;
                        try { nextTotal = totalWait + decision.WaitDuration; }
                        catch (OverflowException) { return Fault(); }
                        if (nextTotal > options.MaximumTotalWait)
                            return PlaybackLateAwareTimedDispatchResult.WaitLimitReached;
                        totalWait = nextTotal;
                        await _delay.DelayAsync(decision.WaitDuration, operation.Token)
                            .ConfigureAwait(false);
                        break;
                    case PlaybackAvTimingDecisionResult.ClockNotStarted:
                        return PlaybackLateAwareTimedDispatchResult.ClockNotStarted;
                    case PlaybackAvTimingDecisionResult.DiscontinuityResetRequired:
                    case PlaybackAvTimingDecisionResult.GenerationMismatch:
                        SetResetRequired();
                        return PlaybackLateAwareTimedDispatchResult.ResetRequired;
                    case PlaybackAvTimingDecisionResult.InvalidTimestamp:
                    case PlaybackAvTimingDecisionResult.InvalidConfiguration:
                    case PlaybackAvTimingDecisionResult.WaitLimitExceeded:
                        return PlaybackLateAwareTimedDispatchResult.InvalidItem;
                    case PlaybackAvTimingDecisionResult.Cancelled:
                        return PlaybackLateAwareTimedDispatchResult.Cancelled;
                    case PlaybackAvTimingDecisionResult.Disposed:
                        return PlaybackLateAwareTimedDispatchResult.Disposed;
                    default:
                        return Fault();
                }
            }
            return PlaybackLateAwareTimedDispatchResult.WaitLimitReached;
        }
        catch (OperationCanceledException)
        {
            return PlaybackLateAwareTimedDispatchResult.Cancelled;
        }
        catch
        {
            return Fault();
        }
        finally
        {
            if (reservation is not null)
            {
                if (!completed)
                {
                    try { reservation.Complete(false); }
                    catch { SetFaulted(); }
                }
                reservation.Dispose();
            }
            lock (_sync)
            {
                if (ReferenceEquals(_operationCancellation, operation))
                    _operationCancellation = null;
                if (!_isDisposed && _state == PlaybackLateAwareTimedDispatchState.Running)
                    _state = PlaybackLateAwareTimedDispatchState.Ready;
            }
            operation.Dispose();
            _gate.Release();
        }
    }

    public async ValueTask<bool> ResetAsync(
        long videoGeneration,
        long audioGeneration,
        CancellationToken cancellationToken = default)
    {
        if (videoGeneration < 0 || audioGeneration < 0) return false;
        lock (_sync)
        {
            if (_isDisposed) return false;
            _operationCancellation?.Cancel();
        }
        try { await _gate.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            SetResetRequired();
            return false;
        }
        try
        {
            if (!await ResetOutputAsync(video: false, audioGeneration, cancellationToken)
                    .ConfigureAwait(false) ||
                !await ResetOutputAsync(video: true, videoGeneration, cancellationToken)
                    .ConfigureAwait(false) ||
                await _timing.ResetAsync(videoGeneration, audioGeneration, cancellationToken)
                    .ConfigureAwait(false) != PlaybackAvTimingResetResult.Reset ||
                !_disposition.Reset(videoGeneration, audioGeneration))
            {
                SetFaulted();
                return false;
            }
            lock (_sync)
            {
                if (_isDisposed) return false;
                _videoGeneration = videoGeneration;
                _audioGeneration = audioGeneration;
                _preferVideoNext = true;
                _state = PlaybackLateAwareTimedDispatchState.Ready;
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            SetResetRequired();
            return false;
        }
        catch
        {
            SetFaulted();
            return false;
        }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _state = PlaybackLateAwareTimedDispatchState.Disposed;
            _operationCancellation?.Cancel();
        }
        _gate.Wait();
        _gate.Release();
    }

    private bool TryCreateTimingItem(
        IPlaybackWindowsOutputSchedulerReservation reservation,
        out PlaybackAvTimingItem item,
        out long generation)
    {
        item = default;
        generation = 0;
        if (!Enum.IsDefined(reservation.Kind) || reservation.Sequence < 0 ||
            !Enum.IsDefined(reservation.Discontinuity) || reservation.Frame.IsDisposed)
            return false;
        var metadata = reservation.Frame.Metadata;
        if (!metadata.PresentationTimestamp.IsKnown ||
            metadata.PresentationTimestamp.Value < TimeSpan.Zero)
            return false;
        var kind = reservation.Kind == PlaybackSchedulerOutputKind.Video
            ? PlaybackPolicyMediaKind.Video
            : PlaybackPolicyMediaKind.Audio;
        lock (_sync)
            generation = kind == PlaybackPolicyMediaKind.Video
                ? _videoGeneration
                : _audioGeneration;
        item = new(kind,
            PlaybackPolicyTime.Known(metadata.PresentationTimestamp.Value),
            metadata.Duration.IsKnown
                ? PlaybackPolicyTime.Known(metadata.Duration.Value)
                : PlaybackPolicyTime.Unknown,
            reservation.Sequence, reservation.Discontinuity, generation);
        return true;
    }

    private async ValueTask<PlaybackLateAwareTimedDispatchResult> DispatchAsync(
        IPlaybackWindowsOutputSchedulerReservation reservation,
        CancellationToken cancellationToken)
    {
        var metadata = reservation.Frame.Metadata;
        if (reservation.Frame.Planes.Count != 1)
            return PlaybackLateAwareTimedDispatchResult.InvalidItem;
        var plane = reservation.Frame.Planes[0];
        PlaybackWindowsOutputBufferProcessingResult output;
        if (reservation.Kind == PlaybackSchedulerOutputKind.Video)
        {
            if (metadata.MediaKind != PlaybackDecoderFfmpegFrameMediaKind.Video ||
                metadata.Format != PlaybackDecoderFfmpegFrameFormat.VideoBgra32 ||
                metadata.Width <= 0 || metadata.Height <= 0 || plane.Index != 0 ||
                plane.Rows != metadata.Height || plane.Stride != metadata.Width * 4 ||
                plane.LogicalRowBytes != plane.Stride ||
                plane.Buffer.Length != (long)plane.Stride * plane.Rows)
                return PlaybackLateAwareTimedDispatchResult.InvalidItem;
            output = await _dispatcher.ProcessVideoAsync(new(
                PlaybackDecodedVideoPixelFormat.Bgra32, metadata.Width, metadata.Height,
                plane.Stride, plane.Buffer, _videoGeneration), cancellationToken)
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
                metadata.ChannelCount <= 0 || metadata.SampleCount <= 0 || plane.Index != 0 ||
                plane.Rows != 1 ||
                plane.LogicalRowBytes != metadata.SampleCount * metadata.ChannelCount * bytes ||
                plane.Stride != plane.LogicalRowBytes ||
                plane.Buffer.Length != plane.LogicalRowBytes)
                return PlaybackLateAwareTimedDispatchResult.InvalidItem;
            output = await _dispatcher.ProcessAudioAsync(new(format, metadata.SampleRate,
                metadata.ChannelCount, metadata.SampleCount, plane.Buffer, _audioGeneration),
                cancellationToken).ConfigureAwait(false);
        }
        return MapOutput(output, reservation.Kind);
    }

    private async ValueTask<bool> ResetOutputAsync(
        bool video, long generation, CancellationToken cancellationToken)
    {
        var result = video
            ? await _dispatcher.ResetVideoAsync(generation, cancellationToken).ConfigureAwait(false)
            : await _dispatcher.ResetAudioAsync(generation, cancellationToken).ConfigureAwait(false);
        return result is PlaybackWindowsOutputBufferResetResult.Reset or
            PlaybackWindowsOutputBufferResetResult.DeviceLost;
    }

    private PlaybackLateAwareTimedDispatchResult Fault()
    {
        SetFaulted();
        return PlaybackLateAwareTimedDispatchResult.Failure;
    }

    private void SetFaulted()
    {
        lock (_sync)
            if (!_isDisposed) _state = PlaybackLateAwareTimedDispatchState.Faulted;
    }

    private void SetResetRequired()
    {
        lock (_sync)
            if (!_isDisposed) _state = PlaybackLateAwareTimedDispatchState.ResetRequired;
    }

    private static PlaybackLateAwareTimedDispatchResult MapOutput(
        PlaybackWindowsOutputBufferProcessingResult result,
        PlaybackSchedulerOutputKind kind) => result switch
        {
            PlaybackWindowsOutputBufferProcessingResult.Processed =>
                kind == PlaybackSchedulerOutputKind.Video
                    ? PlaybackLateAwareTimedDispatchResult.VideoProcessed
                    : PlaybackLateAwareTimedDispatchResult.AudioProcessed,
            PlaybackWindowsOutputBufferProcessingResult.WouldBlock =>
                PlaybackLateAwareTimedDispatchResult.WouldBlock,
            PlaybackWindowsOutputBufferProcessingResult.InitializationRequired =>
                PlaybackLateAwareTimedDispatchResult.InitializationRequired,
            PlaybackWindowsOutputBufferProcessingResult.BindingRequired =>
                PlaybackLateAwareTimedDispatchResult.BindingRequired,
            PlaybackWindowsOutputBufferProcessingResult.ResizeRequired =>
                PlaybackLateAwareTimedDispatchResult.ResizeRequired,
            PlaybackWindowsOutputBufferProcessingResult.DeviceLost =>
                PlaybackLateAwareTimedDispatchResult.DeviceLost,
            PlaybackWindowsOutputBufferProcessingResult.InvalidEnvironment =>
                PlaybackLateAwareTimedDispatchResult.InvalidEnvironment,
            PlaybackWindowsOutputBufferProcessingResult.Cancelled =>
                PlaybackLateAwareTimedDispatchResult.Cancelled,
            PlaybackWindowsOutputBufferProcessingResult.Disposed =>
                PlaybackLateAwareTimedDispatchResult.Disposed,
            PlaybackWindowsOutputBufferProcessingResult.FormatUnsupported or
            PlaybackWindowsOutputBufferProcessingResult.InvalidConfiguration =>
                PlaybackLateAwareTimedDispatchResult.InvalidItem,
            _ => PlaybackLateAwareTimedDispatchResult.Failure,
        };

    private PlaybackLateAwareTimedDispatchResult MapDisposition(
        PlaybackLateReservationDispositionResult result) => result switch
        {
            PlaybackLateReservationDispositionResult.RetainedVideoLate =>
                PlaybackLateAwareTimedDispatchResult.RetainedVideoLate,
            PlaybackLateReservationDispositionResult.RetainedAudioLate =>
                PlaybackLateAwareTimedDispatchResult.RetainedAudioLate,
            PlaybackLateReservationDispositionResult.VideoDropConsumed =>
                PlaybackLateAwareTimedDispatchResult.VideoDropConsumed,
            PlaybackLateReservationDispositionResult.AudioRecoveryRequired =>
                PlaybackLateAwareTimedDispatchResult.AudioRecoveryRequired,
            PlaybackLateReservationDispositionResult.ResetRequired =>
                SetAndReturnResetRequired(),
            PlaybackLateReservationDispositionResult.InvalidReservation =>
                PlaybackLateAwareTimedDispatchResult.InvalidItem,
            PlaybackLateReservationDispositionResult.Disposed =>
                PlaybackLateAwareTimedDispatchResult.Disposed,
            _ => Fault(),
        };

    private PlaybackLateAwareTimedDispatchResult SetAndReturnResetRequired()
    {
        SetResetRequired();
        return PlaybackLateAwareTimedDispatchResult.ResetRequired;
    }

    private static bool ValidateOptions(PlaybackTimedSchedulerDispatchOptions? options) =>
        options is not null && options.MaximumDecisionCount is >= 1 and <=
            PlaybackTimedSchedulerDispatchOptions.MaximumDecisionCountLimit &&
        options.MaximumTotalWait > TimeSpan.Zero &&
        options.MaximumTotalWait <= PlaybackTimedSchedulerDispatchOptions.MaximumTotalWaitLimit &&
        options.MinimumDelay > TimeSpan.Zero &&
        options.MaximumSingleDelay >= options.MinimumDelay &&
        options.MaximumSingleDelay <= PlaybackTimedSchedulerDispatchOptions.MaximumSingleDelayLimit &&
        options.MaximumSingleDelay <= options.MaximumTotalWait;
}
