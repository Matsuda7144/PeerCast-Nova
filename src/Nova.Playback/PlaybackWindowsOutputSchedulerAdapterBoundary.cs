namespace PeerCast.Nova.Playback;

public enum PlaybackWindowsOutputSchedulerAdapterResult
{
    VideoProcessed,
    AudioProcessed,
    WouldBlock,
    Empty,
    InitializationRequired,
    BindingRequired,
    ResizeRequired,
    DeviceLost,
    FormatUnsupported,
    InvalidItem,
    InvalidEnvironment,
    Cancelled,
    Disposed,
    Failure,
    ResetRequired,
}

public enum PlaybackWindowsOutputSchedulerAdapterResetResult
{
    Reset,
    Cancelled,
    Disposed,
    Failure,
    PartialResetFault,
}

public enum PlaybackWindowsOutputSchedulerAdapterState
{
    Ready,
    ResetFaulted,
    Disposed,
}

public readonly record struct PlaybackWindowsOutputSchedulerAdapterSnapshot(
    long VideoGeneration,
    long AudioGeneration,
    bool PreferVideoNext,
    PlaybackWindowsOutputSchedulerAdapterState State);

public sealed class PlaybackWindowsOutputSchedulerAdapterBoundary : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IPlaybackWindowsOutputSchedulerSource _source;
    private readonly IPlaybackWindowsOutputBufferDispatcher _dispatcher;
    private readonly Func<bool> _isSupportedPlatform;
    private long _videoGeneration;
    private long _audioGeneration;
    private bool _preferVideoNext = true;
    private PlaybackWindowsOutputSchedulerAdapterState _state;
    private volatile bool _isDisposed;

    public PlaybackWindowsOutputSchedulerAdapterBoundary(
        BoundedPlaybackScheduler scheduler,
        PlaybackWindowsOutputBufferProcessingBoundary processor,
        long videoGeneration,
        long audioGeneration)
        : this(new BoundedPlaybackWindowsOutputSchedulerSource(scheduler),
            new PlaybackWindowsOutputBufferDispatcher(processor), videoGeneration,
            audioGeneration, static () => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22_000))
    {
    }

    internal PlaybackWindowsOutputSchedulerAdapterBoundary(
        IPlaybackWindowsOutputSchedulerSource source,
        IPlaybackWindowsOutputBufferDispatcher dispatcher,
        long videoGeneration,
        long audioGeneration,
        Func<bool> isSupportedPlatform)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(isSupportedPlatform);
        ArgumentOutOfRangeException.ThrowIfNegative(videoGeneration);
        ArgumentOutOfRangeException.ThrowIfNegative(audioGeneration);
        _source = source;
        _dispatcher = dispatcher;
        _videoGeneration = videoGeneration;
        _audioGeneration = audioGeneration;
        _isSupportedPlatform = isSupportedPlatform;
    }

    public PlaybackWindowsOutputSchedulerAdapterSnapshot Snapshot => new(
        _videoGeneration, _audioGeneration, _preferVideoNext, _state);

    public async ValueTask<PlaybackWindowsOutputSchedulerAdapterResult> PumpOneAsync(
        CancellationToken cancellationToken)
    {
        if (_isDisposed) return PlaybackWindowsOutputSchedulerAdapterResult.Disposed;
        if (!await EnterAsync(cancellationToken).ConfigureAwait(false))
            return PlaybackWindowsOutputSchedulerAdapterResult.Cancelled;
        try
        {
            if (_isDisposed) return PlaybackWindowsOutputSchedulerAdapterResult.Disposed;
            if (_state == PlaybackWindowsOutputSchedulerAdapterState.ResetFaulted)
                return PlaybackWindowsOutputSchedulerAdapterResult.ResetRequired;
            if (!_isSupportedPlatform())
                return PlaybackWindowsOutputSchedulerAdapterResult.InvalidEnvironment;

            var preferred = _preferVideoNext
                ? PlaybackSchedulerOutputKind.Video
                : PlaybackSchedulerOutputKind.Audio;
            var taken = await _source.ReserveAsync(preferred, cancellationToken)
                .ConfigureAwait(false);
            if (!Enum.IsDefined(taken.Result))
                return PlaybackWindowsOutputSchedulerAdapterResult.Failure;
            if (taken.Result == PlaybackWindowsOutputSchedulerTakeResult.Empty)
                return PlaybackWindowsOutputSchedulerAdapterResult.Empty;
            if (taken.Result == PlaybackWindowsOutputSchedulerTakeResult.Cancelled)
                return PlaybackWindowsOutputSchedulerAdapterResult.Cancelled;
            if (taken.Result == PlaybackWindowsOutputSchedulerTakeResult.Disposed)
                return PlaybackWindowsOutputSchedulerAdapterResult.Disposed;
            if (taken.Result != PlaybackWindowsOutputSchedulerTakeResult.Reserved ||
                taken.Reservation is null)
                return PlaybackWindowsOutputSchedulerAdapterResult.Failure;

            using var reservation = taken.Reservation;
            var dispatch = await DispatchAsync(reservation, cancellationToken)
                .ConfigureAwait(false);
            var consume = dispatch is PlaybackWindowsOutputSchedulerAdapterResult.VideoProcessed or
                PlaybackWindowsOutputSchedulerAdapterResult.AudioProcessed;
            reservation.Complete(consume);
            if (consume)
                _preferVideoNext = dispatch == PlaybackWindowsOutputSchedulerAdapterResult.AudioProcessed;
            return dispatch;
        }
        catch (OperationCanceledException)
        {
            return PlaybackWindowsOutputSchedulerAdapterResult.Cancelled;
        }
        catch
        {
            return PlaybackWindowsOutputSchedulerAdapterResult.Failure;
        }
        finally { _gate.Release(); }
    }

    public async ValueTask<PlaybackWindowsOutputSchedulerAdapterResetResult> ResetAsync(
        long videoGeneration, long audioGeneration, CancellationToken cancellationToken)
    {
        if (_isDisposed) return PlaybackWindowsOutputSchedulerAdapterResetResult.Disposed;
        if (videoGeneration < 0 || audioGeneration < 0)
            return PlaybackWindowsOutputSchedulerAdapterResetResult.Failure;
        if (!await EnterAsync(cancellationToken).ConfigureAwait(false))
            return PlaybackWindowsOutputSchedulerAdapterResetResult.Cancelled;
        try
        {
            if (_isDisposed) return PlaybackWindowsOutputSchedulerAdapterResetResult.Disposed;
            var audio = await InvokeResetAsync(video: false, audioGeneration,
                cancellationToken).ConfigureAwait(false);
            if (!IsReset(audio)) return MapReset(audio);
            var video = await InvokeResetAsync(video: true, videoGeneration,
                cancellationToken).ConfigureAwait(false);
            if (!IsReset(video))
            {
                var rollback = await InvokeResetAsync(video: false, _audioGeneration,
                    CancellationToken.None).ConfigureAwait(false);
                if (IsReset(rollback)) return MapReset(video);
                _audioGeneration = audioGeneration;
                _state = PlaybackWindowsOutputSchedulerAdapterState.ResetFaulted;
                return PlaybackWindowsOutputSchedulerAdapterResetResult.PartialResetFault;
            }
            _videoGeneration = videoGeneration;
            _audioGeneration = audioGeneration;
            _preferVideoNext = true;
            _state = PlaybackWindowsOutputSchedulerAdapterState.Ready;
            return PlaybackWindowsOutputSchedulerAdapterResetResult.Reset;
        }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _state = PlaybackWindowsOutputSchedulerAdapterState.Disposed;
        _gate.Wait();
        try
        {
            TryCleanup(() => _dispatcher.ResetAudioAsync(_audioGeneration, default)
                .AsTask().GetAwaiter().GetResult());
            TryCleanup(() => _dispatcher.ResetVideoAsync(_videoGeneration, default)
                .AsTask().GetAwaiter().GetResult());
        }
        finally { _gate.Release(); }
    }

    private async ValueTask<PlaybackWindowsOutputSchedulerAdapterResult> DispatchAsync(
        IPlaybackWindowsOutputSchedulerReservation reservation,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(reservation.Kind) || reservation.Sequence < 0 ||
            !Enum.IsDefined(reservation.Discontinuity) || reservation.Frame.IsDisposed)
            return PlaybackWindowsOutputSchedulerAdapterResult.InvalidItem;
        var metadata = reservation.Frame.Metadata;
        if (!Enum.IsDefined(metadata.MediaKind) || !Enum.IsDefined(metadata.Format) ||
            !metadata.PresentationTimestamp.IsKnown || metadata.PresentationTimestamp.Value < TimeSpan.Zero ||
            reservation.Frame.Planes.Count != 1)
            return PlaybackWindowsOutputSchedulerAdapterResult.InvalidItem;
        var plane = reservation.Frame.Planes[0];
        if (plane.Index != 0 || plane.Buffer.IsEmpty || plane.Stride <= 0 ||
            plane.Rows <= 0 || plane.LogicalRowBytes <= 0)
            return PlaybackWindowsOutputSchedulerAdapterResult.InvalidItem;

        PlaybackWindowsOutputBufferProcessingResult result;
        if (reservation.Kind == PlaybackSchedulerOutputKind.Video)
        {
            if (metadata.MediaKind != PlaybackDecoderFfmpegFrameMediaKind.Video ||
                metadata.Format != PlaybackDecoderFfmpegFrameFormat.VideoBgra32 ||
                metadata.Width <= 0 || metadata.Height <= 0 || plane.Rows != metadata.Height ||
                plane.Stride != metadata.Width * 4 || plane.LogicalRowBytes != plane.Stride ||
                plane.Buffer.Length != (long)plane.Stride * plane.Rows)
                return PlaybackWindowsOutputSchedulerAdapterResult.InvalidItem;
            result = await _dispatcher.ProcessVideoAsync(new(
                PlaybackDecodedVideoPixelFormat.Bgra32, metadata.Width, metadata.Height,
                plane.Stride, plane.Buffer, _videoGeneration), cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            var format = metadata.Format switch
            {
                PlaybackDecoderFfmpegFrameFormat.AudioSigned16 => PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
                PlaybackDecoderFfmpegFrameFormat.AudioFloat32 => PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved,
                _ => (PlaybackDecodedAudioSampleFormat)(-1),
            };
            var bytes = format == PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved ? 2 : 4;
            if (metadata.MediaKind != PlaybackDecoderFfmpegFrameMediaKind.Audio ||
                !Enum.IsDefined(format) || metadata.SampleRate <= 0 ||
                metadata.ChannelCount <= 0 || metadata.SampleCount <= 0 || plane.Rows != 1 ||
                plane.LogicalRowBytes != metadata.SampleCount * metadata.ChannelCount * bytes ||
                plane.Stride != plane.LogicalRowBytes || plane.Buffer.Length != plane.LogicalRowBytes)
                return PlaybackWindowsOutputSchedulerAdapterResult.InvalidItem;
            result = await _dispatcher.ProcessAudioAsync(new(format, metadata.SampleRate,
                metadata.ChannelCount, metadata.SampleCount, plane.Buffer, _audioGeneration),
                cancellationToken).ConfigureAwait(false);
        }
        return Map(result, reservation.Kind);
    }

    private static PlaybackWindowsOutputSchedulerAdapterResult Map(
        PlaybackWindowsOutputBufferProcessingResult result, PlaybackSchedulerOutputKind kind) =>
        result switch
        {
            PlaybackWindowsOutputBufferProcessingResult.Processed => kind == PlaybackSchedulerOutputKind.Video
                ? PlaybackWindowsOutputSchedulerAdapterResult.VideoProcessed
                : PlaybackWindowsOutputSchedulerAdapterResult.AudioProcessed,
            PlaybackWindowsOutputBufferProcessingResult.WouldBlock => PlaybackWindowsOutputSchedulerAdapterResult.WouldBlock,
            PlaybackWindowsOutputBufferProcessingResult.InitializationRequired => PlaybackWindowsOutputSchedulerAdapterResult.InitializationRequired,
            PlaybackWindowsOutputBufferProcessingResult.BindingRequired => PlaybackWindowsOutputSchedulerAdapterResult.BindingRequired,
            PlaybackWindowsOutputBufferProcessingResult.FormatUnsupported => PlaybackWindowsOutputSchedulerAdapterResult.FormatUnsupported,
            PlaybackWindowsOutputBufferProcessingResult.ResizeRequired => PlaybackWindowsOutputSchedulerAdapterResult.ResizeRequired,
            PlaybackWindowsOutputBufferProcessingResult.DeviceLost => PlaybackWindowsOutputSchedulerAdapterResult.DeviceLost,
            PlaybackWindowsOutputBufferProcessingResult.InvalidConfiguration => PlaybackWindowsOutputSchedulerAdapterResult.InvalidItem,
            PlaybackWindowsOutputBufferProcessingResult.InvalidEnvironment => PlaybackWindowsOutputSchedulerAdapterResult.InvalidEnvironment,
            PlaybackWindowsOutputBufferProcessingResult.Cancelled => PlaybackWindowsOutputSchedulerAdapterResult.Cancelled,
            PlaybackWindowsOutputBufferProcessingResult.Disposed => PlaybackWindowsOutputSchedulerAdapterResult.Disposed,
            _ => PlaybackWindowsOutputSchedulerAdapterResult.Failure,
        };

    private static bool IsReset(PlaybackWindowsOutputBufferResetResult result) =>
        result is PlaybackWindowsOutputBufferResetResult.Reset or
            PlaybackWindowsOutputBufferResetResult.DeviceLost;

    private static PlaybackWindowsOutputSchedulerAdapterResetResult MapReset(
        PlaybackWindowsOutputBufferResetResult result) => result switch
        {
            PlaybackWindowsOutputBufferResetResult.Cancelled => PlaybackWindowsOutputSchedulerAdapterResetResult.Cancelled,
            PlaybackWindowsOutputBufferResetResult.Disposed => PlaybackWindowsOutputSchedulerAdapterResetResult.Disposed,
            _ => PlaybackWindowsOutputSchedulerAdapterResetResult.Failure,
        };

    private async ValueTask<PlaybackWindowsOutputBufferResetResult> InvokeResetAsync(
        bool video, long generation, CancellationToken cancellationToken)
    {
        try
        {
            return video
                ? await _dispatcher.ResetVideoAsync(generation, cancellationToken).ConfigureAwait(false)
                : await _dispatcher.ResetAudioAsync(generation, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return PlaybackWindowsOutputBufferResetResult.Cancelled;
        }
        catch
        {
            return PlaybackWindowsOutputBufferResetResult.Failure;
        }
    }

    private static void TryCleanup(Action cleanup)
    {
        try { cleanup(); } catch { }
    }

    private async ValueTask<bool> EnterAsync(CancellationToken cancellationToken)
    {
        try { await _gate.WaitAsync(cancellationToken).ConfigureAwait(false); return true; }
        catch (OperationCanceledException) { return false; }
    }
}

internal enum PlaybackWindowsOutputSchedulerTakeResult
{
    Reserved,
    Empty,
    Cancelled,
    Disposed,
    Failure,
}

internal readonly record struct PlaybackWindowsOutputSchedulerTake(
    PlaybackWindowsOutputSchedulerTakeResult Result,
    IPlaybackWindowsOutputSchedulerReservation? Reservation);

internal interface IPlaybackWindowsOutputSchedulerReservation : IDisposable
{
    PlaybackSchedulerOutputKind Kind { get; }
    PlaybackDecoderFfmpegManagedFrame Frame { get; }
    long Sequence { get; }
    PlaybackDecodedFrameDiscontinuityKind Discontinuity { get; }
    void Complete(bool consume);
}

internal interface IPlaybackWindowsOutputSchedulerSource
{
    ValueTask<PlaybackWindowsOutputSchedulerTake> ReserveAsync(
        PlaybackSchedulerOutputKind preferredKind, CancellationToken cancellationToken);
}

internal interface IPlaybackWindowsOutputBufferDispatcher
{
    ValueTask<PlaybackWindowsOutputBufferProcessingResult> ProcessVideoAsync(
        PlaybackWindowsVideoFrameBuffer frame, CancellationToken cancellationToken);
    ValueTask<PlaybackWindowsOutputBufferProcessingResult> ProcessAudioAsync(
        PlaybackWindowsAudioSampleBuffer samples, CancellationToken cancellationToken);
    ValueTask<PlaybackWindowsOutputBufferResetResult> ResetVideoAsync(
        long generation, CancellationToken cancellationToken);
    ValueTask<PlaybackWindowsOutputBufferResetResult> ResetAudioAsync(
        long generation, CancellationToken cancellationToken);
}

internal sealed class BoundedPlaybackWindowsOutputSchedulerSource(
    BoundedPlaybackScheduler scheduler) : IPlaybackWindowsOutputSchedulerSource
{
    public async ValueTask<PlaybackWindowsOutputSchedulerTake> ReserveAsync(
        PlaybackSchedulerOutputKind preferredKind, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(preferredKind))
            return new(PlaybackWindowsOutputSchedulerTakeResult.Failure, null);
        var reservation = await scheduler.ReserveOutputAsync(preferredKind, cancellationToken)
            .ConfigureAwait(false);
        if (reservation is null)
            return new(cancellationToken.IsCancellationRequested
                ? PlaybackWindowsOutputSchedulerTakeResult.Cancelled
                : scheduler.IsDisposed
                    ? PlaybackWindowsOutputSchedulerTakeResult.Disposed
                : scheduler.GetSnapshot().QueueLength == 0
                    ? PlaybackWindowsOutputSchedulerTakeResult.Empty
                    : PlaybackWindowsOutputSchedulerTakeResult.Disposed, null);
        return new(PlaybackWindowsOutputSchedulerTakeResult.Reserved,
            new Reservation(reservation));
    }

    private sealed class Reservation(PlaybackSchedulerOutputReservation reservation) :
        IPlaybackWindowsOutputSchedulerReservation
    {
        public PlaybackSchedulerOutputKind Kind => reservation.Frame.Metadata.MediaKind ==
            PlaybackDecoderFfmpegFrameMediaKind.Video
            ? PlaybackSchedulerOutputKind.Video : PlaybackSchedulerOutputKind.Audio;
        public PlaybackDecoderFfmpegManagedFrame Frame => reservation.Frame;
        public long Sequence => reservation.Sequence;
        public PlaybackDecodedFrameDiscontinuityKind Discontinuity => reservation.Discontinuity;
        public void Complete(bool consume) => reservation.Complete(consume);
        public void Dispose() => reservation.Dispose();
    }
}

internal sealed class PlaybackWindowsOutputBufferDispatcher(
    PlaybackWindowsOutputBufferProcessingBoundary processor) :
    IPlaybackWindowsOutputBufferDispatcher
{
    public ValueTask<PlaybackWindowsOutputBufferProcessingResult> ProcessVideoAsync(
        PlaybackWindowsVideoFrameBuffer frame, CancellationToken cancellationToken) =>
        processor.ProcessVideoAsync(frame, cancellationToken);
    public ValueTask<PlaybackWindowsOutputBufferProcessingResult> ProcessAudioAsync(
        PlaybackWindowsAudioSampleBuffer samples, CancellationToken cancellationToken) =>
        processor.ProcessAudioAsync(samples, cancellationToken);
    public ValueTask<PlaybackWindowsOutputBufferResetResult> ResetVideoAsync(
        long generation, CancellationToken cancellationToken) =>
        processor.ResetVideoAsync(generation, cancellationToken);
    public ValueTask<PlaybackWindowsOutputBufferResetResult> ResetAudioAsync(
        long generation, CancellationToken cancellationToken) =>
        processor.ResetAudioAsync(generation, cancellationToken);
}
