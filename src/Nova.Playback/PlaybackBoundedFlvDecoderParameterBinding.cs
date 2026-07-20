namespace PeerCast.Nova.Playback;

public enum PlaybackBoundedFlvDecoderParameterBindingResult
{
    Prepared,
    InvalidSession,
    SourceDisposed,
    VideoParametersRejected,
    AudioParametersRejected,
    Faulted,
}

internal readonly record struct PlaybackBoundedFlvDecoderParameterCreation(
    PlaybackDecoderFfmpegCodecParametersResult Result,
    PlaybackDecoderFfmpegCodecParametersLease? Lease);

internal interface IPlaybackBoundedFlvDecoderParameterBindingOperations
{
    PlaybackBoundedFlvDecoderParameterCreation CreateVideo(
        PlaybackBoundedDemuxStreamDescription video);

    PlaybackBoundedFlvDecoderParameterCreation CreateAudio(
        PlaybackBoundedDemuxStreamDescription audio);
}

internal sealed class PlaybackBoundedFlvDecoderParameterBindingOperations :
    IPlaybackBoundedFlvDecoderParameterBindingOperations
{
    public PlaybackBoundedFlvDecoderParameterCreation CreateVideo(
        PlaybackBoundedDemuxStreamDescription video)
    {
        var creation = PlaybackDecoderFfmpegCodecParametersFactory.Create(
            PlaybackDecoderFfmpegCodecParametersInput.ForH264(
                video.Width,
                video.Height,
                video.CodecExtradata));
        return new(creation.Result, creation.Lease);
    }

    public PlaybackBoundedFlvDecoderParameterCreation CreateAudio(
        PlaybackBoundedDemuxStreamDescription audio)
    {
        var creation = PlaybackDecoderFfmpegCodecParametersFactory.Create(
            PlaybackDecoderFfmpegCodecParametersInput.ForAac(
                audio.SampleRate,
                audio.ChannelCount,
                audio.CodecExtradata));
        return new(creation.Result, creation.Lease);
    }
}

public sealed class PlaybackBoundedFlvDecoderParameterBinding : IDisposable, IAsyncDisposable
{
    private PlaybackDecoderFfmpegCodecParametersLease? _videoParameters;
    private PlaybackDecoderFfmpegCodecParametersLease? _audioParameters;

    internal PlaybackBoundedFlvDecoderParameterBinding(
        PlaybackBoundedDemuxSession sourceSession,
        PlaybackDecoderFfmpegCodecParametersLease videoParameters,
        PlaybackDecoderFfmpegCodecParametersLease audioParameters)
    {
        SourceSession = sourceSession;
        VideoStreamId = sourceSession.Video.StreamId;
        AudioStreamId = sourceSession.Audio.StreamId;
        VideoTimeBase = sourceSession.Video.TimeBase;
        AudioTimeBase = sourceSession.Audio.TimeBase;
        _videoParameters = videoParameters;
        _audioParameters = audioParameters;
    }

    public PlaybackBoundedDemuxSession SourceSession { get; }

    public int VideoStreamId { get; }

    public int AudioStreamId { get; }

    public PlaybackBoundedDemuxTimeBase VideoTimeBase { get; }

    public PlaybackBoundedDemuxTimeBase AudioTimeBase { get; }

    public bool IsDisposed =>
        Volatile.Read(ref _videoParameters) is null ||
        Volatile.Read(ref _audioParameters) is null;

    internal PlaybackDecoderFfmpegCodecParametersLease VideoParameters =>
        Volatile.Read(ref _videoParameters) ??
        throw new ObjectDisposedException(nameof(PlaybackBoundedFlvDecoderParameterBinding));

    internal PlaybackDecoderFfmpegCodecParametersLease AudioParameters =>
        Volatile.Read(ref _audioParameters) ??
        throw new ObjectDisposedException(nameof(PlaybackBoundedFlvDecoderParameterBinding));

    public bool ReferencesSourceSession(PlaybackBoundedDemuxSession? session) =>
        ReferenceEquals(SourceSession, session);

    public void Dispose()
    {
        var audio = Interlocked.Exchange(ref _audioParameters, null);
        var video = Interlocked.Exchange(ref _videoParameters, null);
        try
        {
            audio?.Dispose();
        }
        finally
        {
            video?.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed record PlaybackBoundedFlvDecoderParameterBindingPreparation(
    PlaybackBoundedFlvDecoderParameterBindingResult Result,
    PlaybackBoundedFlvDecoderParameterBinding? Binding);

public sealed class PlaybackBoundedFlvDecoderParameterBindingAdapter
{
    private static readonly PlaybackBoundedDemuxTimeBase FlvTimeBase = new(1, 1_000);

    private readonly IPlaybackBoundedFlvDecoderParameterBindingOperations _operations;

    public PlaybackBoundedFlvDecoderParameterBindingAdapter()
        : this(new PlaybackBoundedFlvDecoderParameterBindingOperations())
    {
    }

    internal PlaybackBoundedFlvDecoderParameterBindingAdapter(
        IPlaybackBoundedFlvDecoderParameterBindingOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations;
    }

    public PlaybackBoundedFlvDecoderParameterBindingPreparation Prepare(
        PlaybackBoundedDemuxSession? sourceSession)
    {
        if (sourceSession is null)
        {
            return new(PlaybackBoundedFlvDecoderParameterBindingResult.InvalidSession, null);
        }

        if (sourceSession.IsDisposed)
        {
            return new(PlaybackBoundedFlvDecoderParameterBindingResult.SourceDisposed, null);
        }

        if (!IsValidFlvSession(sourceSession))
        {
            return new(
                sourceSession.IsDisposed
                    ? PlaybackBoundedFlvDecoderParameterBindingResult.SourceDisposed
                    : PlaybackBoundedFlvDecoderParameterBindingResult.InvalidSession,
                null);
        }

        PlaybackDecoderFfmpegCodecParametersLease? video = null;
        PlaybackDecoderFfmpegCodecParametersLease? audio = null;
        try
        {
            var videoCreation = _operations.CreateVideo(sourceSession.Video);
            if (!Enum.IsDefined(videoCreation.Result))
            {
                videoCreation.Lease?.Dispose();
                return new(PlaybackBoundedFlvDecoderParameterBindingResult.Faulted, null);
            }

            if (videoCreation.Result != PlaybackDecoderFfmpegCodecParametersResult.Created ||
                videoCreation.Lease is null)
            {
                videoCreation.Lease?.Dispose();
                return new(
                    sourceSession.IsDisposed
                        ? PlaybackBoundedFlvDecoderParameterBindingResult.SourceDisposed
                        : PlaybackBoundedFlvDecoderParameterBindingResult.VideoParametersRejected,
                    null);
            }

            video = videoCreation.Lease;
            var audioCreation = _operations.CreateAudio(sourceSession.Audio);
            if (!Enum.IsDefined(audioCreation.Result))
            {
                audioCreation.Lease?.Dispose();
                video.Dispose();
                video = null;
                return new(PlaybackBoundedFlvDecoderParameterBindingResult.Faulted, null);
            }

            if (audioCreation.Result != PlaybackDecoderFfmpegCodecParametersResult.Created ||
                audioCreation.Lease is null)
            {
                audioCreation.Lease?.Dispose();
                video.Dispose();
                video = null;
                return new(
                    sourceSession.IsDisposed
                        ? PlaybackBoundedFlvDecoderParameterBindingResult.SourceDisposed
                        : PlaybackBoundedFlvDecoderParameterBindingResult.AudioParametersRejected,
                    null);
            }

            audio = audioCreation.Lease;
            if (sourceSession.IsDisposed)
            {
                audio.Dispose();
                audio = null;
                video.Dispose();
                video = null;
                return new(PlaybackBoundedFlvDecoderParameterBindingResult.SourceDisposed, null);
            }

            var binding = new PlaybackBoundedFlvDecoderParameterBinding(
                sourceSession,
                video,
                audio);
            video = null;
            audio = null;
            if (sourceSession.IsDisposed)
            {
                binding.Dispose();
                return new(PlaybackBoundedFlvDecoderParameterBindingResult.SourceDisposed, null);
            }

            return new(PlaybackBoundedFlvDecoderParameterBindingResult.Prepared, binding);
        }
        catch
        {
            try
            {
                audio?.Dispose();
            }
            finally
            {
                video?.Dispose();
            }

            return new(PlaybackBoundedFlvDecoderParameterBindingResult.Faulted, null);
        }
    }

    private static bool IsValidFlvSession(PlaybackBoundedDemuxSession session)
    {
        var video = session.Video;
        var audio = session.Audio;
        return Enum.IsDefined(session.ContainerKind) &&
            session.ContainerKind == PlaybackBoundedDemuxContainerKind.Flv &&
            Enum.IsDefined(video.MediaKind) &&
            video.MediaKind == PlaybackBoundedDemuxMediaKind.Video &&
            Enum.IsDefined(video.Codec) &&
            video.Codec == PlaybackBoundedDemuxCodec.H264 &&
            video.StreamId == 0 &&
            video.Width > 0 &&
            video.Height > 0 &&
            video.SampleRate == 0 &&
            video.ChannelCount == 0 &&
            video.TimeBase == FlvTimeBase &&
            !video.CodecExtradata.IsEmpty &&
            Enum.IsDefined(audio.MediaKind) &&
            audio.MediaKind == PlaybackBoundedDemuxMediaKind.Audio &&
            Enum.IsDefined(audio.Codec) &&
            audio.Codec == PlaybackBoundedDemuxCodec.Aac &&
            audio.StreamId == 1 &&
            audio.Width == 0 &&
            audio.Height == 0 &&
            audio.SampleRate > 0 &&
            audio.ChannelCount > 0 &&
            audio.TimeBase == FlvTimeBase &&
            !audio.CodecExtradata.IsEmpty &&
            video.StreamId != audio.StreamId;
    }
}
