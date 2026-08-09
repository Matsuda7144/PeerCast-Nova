namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffRegularMovieNativeDecoderSessionResult
{
    Opened,
    MissingBinding,
    BindingUnavailable,
    InvalidTrackTimeBase,
    VideoContextRejected,
    VideoTransferRejected,
    VideoTimeBaseRejected,
    VideoOpenRejected,
    AudioContextRejected,
    AudioTransferRejected,
    AudioTimeBaseRejected,
    AudioOpenRejected,
    Faulted,
}

internal interface IPlaybackIsoBmffRegularMovieNativeDecoderContextOwner :
    IDisposable
{
    PlaybackDecoderFfmpegCodecId CodecId { get; }

    PlaybackDecoderFfmpegContextLease? NativeLease { get; }
}

internal sealed class PlaybackIsoBmffRegularMovieNativeDecoderContextOwner(
    PlaybackDecoderFfmpegContextLease lease) :
    IPlaybackIsoBmffRegularMovieNativeDecoderContextOwner
{
    public PlaybackDecoderFfmpegCodecId CodecId => lease.CodecId;

    public PlaybackDecoderFfmpegContextLease NativeLease => lease;

    public void Dispose() => lease.Dispose();
}

internal readonly record struct
    PlaybackIsoBmffRegularMovieNativeDecoderContextCreation(
        PlaybackDecoderFfmpegContextLeaseResult Result,
        IPlaybackIsoBmffRegularMovieNativeDecoderContextOwner? Owner);

internal interface IPlaybackIsoBmffRegularMovieNativeDecoderSessionOperations
{
    PlaybackIsoBmffRegularMovieNativeDecoderContextCreation CreateContext(
        object gate,
        PlaybackDecoderFfmpegCodecId codecId);

    PlaybackDecoderFfmpegParameterTransferResult Transfer(
        IPlaybackIsoBmffRegularMovieNativeDecoderContextOwner context,
        PlaybackDecoderFfmpegCodecParametersLease parameters);

    PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult ConfigureTimeBase(
        IPlaybackIsoBmffRegularMovieNativeDecoderContextOwner context,
        PlaybackBoundedDemuxTimeBase timeBase);

    PlaybackDecoderFfmpegCodecOpenResult Open(
        IPlaybackIsoBmffRegularMovieNativeDecoderContextOwner context);

    void DisposeUnusedGate(object gate);
}

internal sealed class PlaybackIsoBmffRegularMovieNativeDecoderSessionOperations :
    IPlaybackIsoBmffRegularMovieNativeDecoderSessionOperations
{
    private readonly PlaybackDecoderFfmpegContextLeaseFactory _contextFactory =
        new();
    private readonly PlaybackDecoderFfmpegParameterTransfer _parameterTransfer =
        new();
    private readonly PlaybackDecoderFfmpegPacketTimeBaseConfigurationBoundary
        _timeBaseConfiguration = new();
    private readonly PlaybackDecoderFfmpegCodecOpen _codecOpen = new();

    public PlaybackIsoBmffRegularMovieNativeDecoderContextCreation CreateContext(
        object gate,
        PlaybackDecoderFfmpegCodecId codecId)
    {
        if (gate is not PlaybackDecoderFfmpegAbiSymbolGateLease gateLease)
        {
            return new(
                PlaybackDecoderFfmpegContextLeaseResult.ParentUnavailable,
                null);
        }

        var creation = _contextFactory.Create(gateLease, codecId);
        return new(
            creation.Result,
            creation.Lease is null
                ? null
                : new PlaybackIsoBmffRegularMovieNativeDecoderContextOwner(
                    creation.Lease));
    }

    public PlaybackDecoderFfmpegParameterTransferResult Transfer(
        IPlaybackIsoBmffRegularMovieNativeDecoderContextOwner context,
        PlaybackDecoderFfmpegCodecParametersLease parameters) =>
        context.NativeLease is { } lease
            ? _parameterTransfer.Apply(lease, parameters)
            : PlaybackDecoderFfmpegParameterTransferResult.ContextUnavailable;

    public PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult
        ConfigureTimeBase(
            IPlaybackIsoBmffRegularMovieNativeDecoderContextOwner context,
            PlaybackBoundedDemuxTimeBase timeBase) =>
        context.NativeLease is { } lease
            ? _timeBaseConfiguration.Configure(lease, timeBase)
            : PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult
                .ContextUnavailable;

    public PlaybackDecoderFfmpegCodecOpenResult Open(
        IPlaybackIsoBmffRegularMovieNativeDecoderContextOwner context) =>
        context.NativeLease is { } lease
            ? _codecOpen.Open(lease)
            : PlaybackDecoderFfmpegCodecOpenResult.ContextUnavailable;

    public void DisposeUnusedGate(object gate)
    {
        if (gate is PlaybackDecoderFfmpegAbiSymbolGateLease lease)
        {
            lease.Dispose();
        }
    }
}

public sealed class PlaybackIsoBmffRegularMovieNativeDecoderSession :
    IDisposable,
    IAsyncDisposable
{
    private readonly bool _hasVideo;
    private readonly bool _hasAudio;
    private IPlaybackIsoBmffRegularMovieNativeDecoderContextOwner? _video;
    private IPlaybackIsoBmffRegularMovieNativeDecoderContextOwner? _audio;

    internal PlaybackIsoBmffRegularMovieNativeDecoderSession(
        PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBinding source,
        PlaybackBoundedDemuxTimeBase? videoTimeBase,
        PlaybackBoundedDemuxTimeBase? audioTimeBase,
        IPlaybackIsoBmffRegularMovieNativeDecoderContextOwner? video,
        IPlaybackIsoBmffRegularMovieNativeDecoderContextOwner? audio)
    {
        Source = source;
        VideoTimeBase = videoTimeBase;
        AudioTimeBase = audioTimeBase;
        _hasVideo = video is not null;
        _hasAudio = audio is not null;
        _video = video;
        _audio = audio;
    }

    public PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBinding Source
    { get; }

    public PlaybackBoundedDemuxTimeBase? VideoTimeBase { get; }

    public PlaybackBoundedDemuxTimeBase? AudioTimeBase { get; }

    public bool HasVideo => _hasVideo;

    public bool HasAudio => _hasAudio;

    public bool IsDisposed =>
        (_hasVideo && Volatile.Read(ref _video) is null) ||
        (_hasAudio && Volatile.Read(ref _audio) is null);

    internal PlaybackDecoderFfmpegContextLease? VideoContext =>
        Volatile.Read(ref _video)?.NativeLease;

    internal PlaybackDecoderFfmpegContextLease? AudioContext =>
        Volatile.Read(ref _audio)?.NativeLease;

    public void Dispose()
    {
        var audio = Interlocked.Exchange(ref _audio, null);
        var video = Interlocked.Exchange(ref _video, null);
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

public sealed record PlaybackIsoBmffRegularMovieNativeDecoderSessionOpening(
    PlaybackIsoBmffRegularMovieNativeDecoderSessionResult Result,
    PlaybackIsoBmffRegularMovieNativeDecoderSession? Session);

public sealed class PlaybackIsoBmffRegularMovieNativeDecoderSessionFactory
{
    private readonly
        IPlaybackIsoBmffRegularMovieNativeDecoderSessionOperations _operations;

    public PlaybackIsoBmffRegularMovieNativeDecoderSessionFactory()
        : this(new PlaybackIsoBmffRegularMovieNativeDecoderSessionOperations())
    {
    }

    internal PlaybackIsoBmffRegularMovieNativeDecoderSessionFactory(
        IPlaybackIsoBmffRegularMovieNativeDecoderSessionOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations;
    }

    public PlaybackIsoBmffRegularMovieNativeDecoderSessionOpening Open(
        PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBinding? binding)
    {
        if (binding is null)
        {
            return new(
                PlaybackIsoBmffRegularMovieNativeDecoderSessionResult
                    .MissingBinding,
                null);
        }

        if (!binding.IsAvailable)
        {
            binding.Dispose();
            return new(
                PlaybackIsoBmffRegularMovieNativeDecoderSessionResult
                    .BindingUnavailable,
                null);
        }

        if (!TryGetTimeBase(binding, binding.Video, out var videoTimeBase) ||
            !TryGetTimeBase(binding, binding.Audio, out var audioTimeBase))
        {
            binding.Dispose();
            return new(
                PlaybackIsoBmffRegularMovieNativeDecoderSessionResult
                    .InvalidTrackTimeBase,
                null);
        }

        return OpenCore(binding, videoTimeBase, audioTimeBase);
    }

    private PlaybackIsoBmffRegularMovieNativeDecoderSessionOpening OpenCore(
        PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBinding binding,
        PlaybackBoundedDemuxTimeBase? videoTimeBase,
        PlaybackBoundedDemuxTimeBase? audioTimeBase)
    {
        IPlaybackIsoBmffRegularMovieNativeDecoderContextOwner? video = null;
        IPlaybackIsoBmffRegularMovieNativeDecoderContextOwner? audio = null;
        var videoGateConsumed = false;
        var audioGateConsumed = false;
        try
        {
            if (binding.Video is { } videoBinding)
            {
                var creation = _operations.CreateContext(
                    videoBinding.Gate,
                    PlaybackDecoderFfmpegCodecId.H264);
                videoGateConsumed = true;
                if (!IsCreated(creation, PlaybackDecoderFfmpegCodecId.H264))
                {
                    creation.Owner?.Dispose();
                    return Fail(
                        PlaybackIsoBmffRegularMovieNativeDecoderSessionResult
                            .VideoContextRejected);
                }

                video = creation.Owner;
                if (_operations.Transfer(video!, videoBinding.Parameters) !=
                    PlaybackDecoderFfmpegParameterTransferResult.Applied)
                {
                    return Fail(
                        PlaybackIsoBmffRegularMovieNativeDecoderSessionResult
                            .VideoTransferRejected);
                }

                if (_operations.ConfigureTimeBase(video!, videoTimeBase!.Value) !=
                    PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult
                        .Configured)
                {
                    return Fail(
                        PlaybackIsoBmffRegularMovieNativeDecoderSessionResult
                            .VideoTimeBaseRejected);
                }

                if (_operations.Open(video!) !=
                    PlaybackDecoderFfmpegCodecOpenResult.Opened)
                {
                    return Fail(
                        PlaybackIsoBmffRegularMovieNativeDecoderSessionResult
                            .VideoOpenRejected);
                }
            }

            if (binding.Audio is { } audioBinding)
            {
                var creation = _operations.CreateContext(
                    audioBinding.Gate,
                    PlaybackDecoderFfmpegCodecId.Aac);
                audioGateConsumed = true;
                if (!IsCreated(creation, PlaybackDecoderFfmpegCodecId.Aac))
                {
                    creation.Owner?.Dispose();
                    return Fail(
                        PlaybackIsoBmffRegularMovieNativeDecoderSessionResult
                            .AudioContextRejected);
                }

                audio = creation.Owner;
                if (_operations.Transfer(audio!, audioBinding.Parameters) !=
                    PlaybackDecoderFfmpegParameterTransferResult.Applied)
                {
                    return Fail(
                        PlaybackIsoBmffRegularMovieNativeDecoderSessionResult
                            .AudioTransferRejected);
                }

                if (_operations.ConfigureTimeBase(audio!, audioTimeBase!.Value) !=
                    PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult
                        .Configured)
                {
                    return Fail(
                        PlaybackIsoBmffRegularMovieNativeDecoderSessionResult
                            .AudioTimeBaseRejected);
                }

                if (_operations.Open(audio!) !=
                    PlaybackDecoderFfmpegCodecOpenResult.Opened)
                {
                    return Fail(
                        PlaybackIsoBmffRegularMovieNativeDecoderSessionResult
                            .AudioOpenRejected);
                }
            }

            binding.Dispose();
            var session = new PlaybackIsoBmffRegularMovieNativeDecoderSession(
                binding,
                videoTimeBase,
                audioTimeBase,
                video,
                audio);
            video = null;
            audio = null;
            return new(
                PlaybackIsoBmffRegularMovieNativeDecoderSessionResult.Opened,
                session);
        }
        catch
        {
            return Fail(
                PlaybackIsoBmffRegularMovieNativeDecoderSessionResult.Faulted);
        }
        finally
        {
            if (!videoGateConsumed && binding.Video is { } videoBinding)
            {
                _operations.DisposeUnusedGate(videoBinding.Gate);
            }

            if (!audioGateConsumed && binding.Audio is { } audioBinding)
            {
                _operations.DisposeUnusedGate(audioBinding.Gate);
            }
        }

        PlaybackIsoBmffRegularMovieNativeDecoderSessionOpening Fail(
            PlaybackIsoBmffRegularMovieNativeDecoderSessionResult result)
        {
            try
            {
                audio?.Dispose();
            }
            finally
            {
                try
                {
                    video?.Dispose();
                }
                finally
                {
                    binding.Dispose();
                }
            }

            audio = null;
            video = null;
            return new(result, null);
        }
    }

    private static bool TryGetTimeBase(
        PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBinding source,
        PlaybackIsoBmffRegularMovieVerifiedRuntimeTrackCapabilityBinding? track,
        out PlaybackBoundedDemuxTimeBase? timeBase)
    {
        timeBase = null;
        if (track is null)
        {
            return true;
        }

        var movie = source.Source.Source;
        var selectedSource = track.Role ==
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Video
                ? movie.Selection.VideoSource
                : movie.Selection.AudioSource;
        if (selectedSource is null)
        {
            return false;
        }

        if (!ReferenceEquals(track.Track.SequenceProjection, selectedSource))
        {
            return false;
        }

        var timescale = track.Track.Sources.MediaTiming?.Timescale;
        if (timescale is null or 0 or
                > PlaybackBoundedDemuxOptions.MaximumTimeBaseComponent)
        {
            return false;
        }

        timeBase = new(1, checked((int)timescale.Value));
        return PlaybackBoundedDemuxSession.IsValidTimeBase(timeBase.Value);
    }

    private static bool IsCreated(
        PlaybackIsoBmffRegularMovieNativeDecoderContextCreation creation,
        PlaybackDecoderFfmpegCodecId codecId) =>
        creation.Result == PlaybackDecoderFfmpegContextLeaseResult.Created &&
        creation.Owner is not null &&
        creation.Owner.CodecId == codecId;
}
