namespace PeerCast.Nova.Playback;

public enum PlaybackBoundedFlvNativeDecoderSessionResult
{
    Opened,
    InvalidConfiguration,
    SourceDisposed,
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

internal interface IPlaybackBoundedFlvNativeContextOwner : IDisposable
{
    PlaybackDecoderFfmpegCodecId CodecId { get; }

    PlaybackDecoderFfmpegContextLease? Context { get; }
}

internal sealed class PlaybackBoundedFlvNativeContextOwner(
    PlaybackDecoderFfmpegContextLease context) : IPlaybackBoundedFlvNativeContextOwner
{
    public PlaybackDecoderFfmpegCodecId CodecId => context.CodecId;

    public PlaybackDecoderFfmpegContextLease Context => context;

    public void Dispose() => context.Dispose();
}

internal readonly record struct PlaybackBoundedFlvNativeContextCreation(
    PlaybackDecoderFfmpegContextLeaseResult Result,
    IPlaybackBoundedFlvNativeContextOwner? Owner);

internal interface IPlaybackBoundedFlvNativeDecoderSessionOperations
{
    PlaybackBoundedFlvNativeContextCreation CreateContext(
        object gate,
        PlaybackDecoderFfmpegCodecId codecId);

    PlaybackDecoderFfmpegParameterTransferResult Transfer(
        IPlaybackBoundedFlvNativeContextOwner context,
        PlaybackDecoderFfmpegCodecParametersLease parameters);

    PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult ConfigurePacketTimeBase(
        IPlaybackBoundedFlvNativeContextOwner context,
        PlaybackBoundedDemuxTimeBase timeBase);

    PlaybackDecoderFfmpegCodecOpenResult Open(
        IPlaybackBoundedFlvNativeContextOwner context);

    void DisposeUnusedGate(object gate);
}

internal sealed class PlaybackBoundedFlvNativeDecoderSessionOperations :
    IPlaybackBoundedFlvNativeDecoderSessionOperations
{
    private readonly PlaybackDecoderFfmpegContextLeaseFactory _contextFactory = new();
    private readonly PlaybackDecoderFfmpegParameterTransfer _parameterTransfer = new();
    private readonly PlaybackDecoderFfmpegPacketTimeBaseConfigurationBoundary
        _packetTimeBaseConfiguration = new();
    private readonly PlaybackDecoderFfmpegCodecOpen _codecOpen = new();

    public PlaybackBoundedFlvNativeContextCreation CreateContext(
        object gate,
        PlaybackDecoderFfmpegCodecId codecId)
    {
        if (gate is not PlaybackDecoderFfmpegAbiSymbolGateLease gateLease)
        {
            return new(PlaybackDecoderFfmpegContextLeaseResult.ParentUnavailable, null);
        }

        var creation = _contextFactory.Create(gateLease, codecId);
        return new(
            creation.Result,
            creation.Lease is null
                ? null
                : new PlaybackBoundedFlvNativeContextOwner(creation.Lease));
    }

    public PlaybackDecoderFfmpegParameterTransferResult Transfer(
        IPlaybackBoundedFlvNativeContextOwner context,
        PlaybackDecoderFfmpegCodecParametersLease parameters) =>
        context.Context is { } lease
            ? _parameterTransfer.Apply(lease, parameters)
            : PlaybackDecoderFfmpegParameterTransferResult.ContextUnavailable;

    public PlaybackDecoderFfmpegCodecOpenResult Open(
        IPlaybackBoundedFlvNativeContextOwner context) =>
        context.Context is { } lease
            ? _codecOpen.Open(lease)
            : PlaybackDecoderFfmpegCodecOpenResult.ContextUnavailable;

    public PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult ConfigurePacketTimeBase(
        IPlaybackBoundedFlvNativeContextOwner context,
        PlaybackBoundedDemuxTimeBase timeBase) =>
        context.Context is { } lease
            ? _packetTimeBaseConfiguration.Configure(lease, timeBase)
            : PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult.ContextUnavailable;

    public void DisposeUnusedGate(object gate)
    {
        if (gate is PlaybackDecoderFfmpegAbiSymbolGateLease lease)
        {
            lease.Dispose();
        }
    }
}

public sealed class PlaybackBoundedFlvNativeDecoderSession : IDisposable, IAsyncDisposable
{
    private IPlaybackBoundedFlvNativeContextOwner? _video;
    private IPlaybackBoundedFlvNativeContextOwner? _audio;

    internal PlaybackBoundedFlvNativeDecoderSession(
        PlaybackBoundedDemuxSession sourceSession,
        int videoStreamId,
        int audioStreamId,
        PlaybackBoundedDemuxTimeBase videoTimeBase,
        PlaybackBoundedDemuxTimeBase audioTimeBase,
        IPlaybackBoundedFlvNativeContextOwner video,
        IPlaybackBoundedFlvNativeContextOwner audio)
    {
        SourceSession = sourceSession;
        VideoStreamId = videoStreamId;
        AudioStreamId = audioStreamId;
        VideoTimeBase = videoTimeBase;
        AudioTimeBase = audioTimeBase;
        _video = video;
        _audio = audio;
    }

    public PlaybackBoundedDemuxSession SourceSession { get; }

    public int VideoStreamId { get; }

    public int AudioStreamId { get; }

    public PlaybackBoundedDemuxTimeBase VideoTimeBase { get; }

    public PlaybackBoundedDemuxTimeBase AudioTimeBase { get; }

    public bool IsDisposed =>
        Volatile.Read(ref _video) is null || Volatile.Read(ref _audio) is null;

    internal PlaybackDecoderFfmpegContextLease VideoContext =>
        Volatile.Read(ref _video)?.Context ??
        throw new ObjectDisposedException(nameof(PlaybackBoundedFlvNativeDecoderSession));

    internal PlaybackDecoderFfmpegContextLease AudioContext =>
        Volatile.Read(ref _audio)?.Context ??
        throw new ObjectDisposedException(nameof(PlaybackBoundedFlvNativeDecoderSession));

    public bool ReferencesSourceSession(PlaybackBoundedDemuxSession? session) =>
        ReferenceEquals(SourceSession, session);

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

public sealed record PlaybackBoundedFlvNativeDecoderSessionOpening(
    PlaybackBoundedFlvNativeDecoderSessionResult Result,
    PlaybackBoundedFlvNativeDecoderSession? Session);

public sealed class PlaybackBoundedFlvNativeDecoderSessionFactory
{
    private readonly IPlaybackBoundedFlvNativeDecoderSessionOperations _operations;

    public PlaybackBoundedFlvNativeDecoderSessionFactory()
        : this(new PlaybackBoundedFlvNativeDecoderSessionOperations())
    {
    }

    internal PlaybackBoundedFlvNativeDecoderSessionFactory(
        IPlaybackBoundedFlvNativeDecoderSessionOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations;
    }

    public PlaybackBoundedFlvNativeDecoderSessionOpening Open(
        PlaybackBoundedFlvDecoderParameterBinding? binding,
        PlaybackDecoderFfmpegAbiSymbolGateLease? videoGate,
        PlaybackDecoderFfmpegAbiSymbolGateLease? audioGate)
    {
        if (videoGate is null || audioGate is null)
        {
            binding?.Dispose();
            videoGate?.Dispose();
            audioGate?.Dispose();
            return new(PlaybackBoundedFlvNativeDecoderSessionResult.InvalidConfiguration, null);
        }

        return OpenCore(binding, videoGate, audioGate);
    }

    internal PlaybackBoundedFlvNativeDecoderSessionOpening OpenCore(
        PlaybackBoundedFlvDecoderParameterBinding? binding,
        object? videoGate,
        object? audioGate)
    {
        if (binding is null || videoGate is null || audioGate is null ||
            ReferenceEquals(videoGate, audioGate) || binding.IsDisposed)
        {
            binding?.Dispose();
            DisposeGate(videoGate);
            DisposeGate(audioGate);
            return new(PlaybackBoundedFlvNativeDecoderSessionResult.InvalidConfiguration, null);
        }

        var ownedBinding = binding;
        var source = ownedBinding.SourceSession;
        var videoStreamId = ownedBinding.VideoStreamId;
        var audioStreamId = ownedBinding.AudioStreamId;
        var videoTimeBase = ownedBinding.VideoTimeBase;
        var audioTimeBase = ownedBinding.AudioTimeBase;
        if (source.IsDisposed)
        {
            ownedBinding.Dispose();
            DisposeGate(videoGate);
            DisposeGate(audioGate);
            return new(PlaybackBoundedFlvNativeDecoderSessionResult.SourceDisposed, null);
        }

        IPlaybackBoundedFlvNativeContextOwner? video = null;
        IPlaybackBoundedFlvNativeContextOwner? audio = null;
        var videoGateConsumed = false;
        var audioGateConsumed = false;
        try
        {
            var videoCreation = _operations.CreateContext(videoGate, PlaybackDecoderFfmpegCodecId.H264);
            videoGateConsumed = true;
            if (!IsCreated(videoCreation, PlaybackDecoderFfmpegCodecId.H264))
            {
                videoCreation.Owner?.Dispose();
                return Fail(PlaybackBoundedFlvNativeDecoderSessionResult.VideoContextRejected);
            }

            video = videoCreation.Owner;
            if (_operations.Transfer(video!, ownedBinding.VideoParameters) !=
                PlaybackDecoderFfmpegParameterTransferResult.Applied)
            {
                return Fail(PlaybackBoundedFlvNativeDecoderSessionResult.VideoTransferRejected);
            }

            if (_operations.ConfigurePacketTimeBase(video!, videoTimeBase) !=
                PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult.Configured)
            {
                return Fail(PlaybackBoundedFlvNativeDecoderSessionResult.VideoTimeBaseRejected);
            }

            if (_operations.Open(video!) != PlaybackDecoderFfmpegCodecOpenResult.Opened)
            {
                return Fail(PlaybackBoundedFlvNativeDecoderSessionResult.VideoOpenRejected);
            }

            if (source.IsDisposed)
            {
                return Fail(PlaybackBoundedFlvNativeDecoderSessionResult.SourceDisposed);
            }

            var audioCreation = _operations.CreateContext(audioGate, PlaybackDecoderFfmpegCodecId.Aac);
            audioGateConsumed = true;
            if (!IsCreated(audioCreation, PlaybackDecoderFfmpegCodecId.Aac))
            {
                audioCreation.Owner?.Dispose();
                return Fail(PlaybackBoundedFlvNativeDecoderSessionResult.AudioContextRejected);
            }

            audio = audioCreation.Owner;
            if (_operations.Transfer(audio!, ownedBinding.AudioParameters) !=
                PlaybackDecoderFfmpegParameterTransferResult.Applied)
            {
                return Fail(PlaybackBoundedFlvNativeDecoderSessionResult.AudioTransferRejected);
            }

            if (_operations.ConfigurePacketTimeBase(audio!, audioTimeBase) !=
                PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult.Configured)
            {
                return Fail(PlaybackBoundedFlvNativeDecoderSessionResult.AudioTimeBaseRejected);
            }

            if (_operations.Open(audio!) != PlaybackDecoderFfmpegCodecOpenResult.Opened)
            {
                return Fail(PlaybackBoundedFlvNativeDecoderSessionResult.AudioOpenRejected);
            }

            if (source.IsDisposed)
            {
                return Fail(PlaybackBoundedFlvNativeDecoderSessionResult.SourceDisposed);
            }

            ownedBinding.Dispose();
            var session = new PlaybackBoundedFlvNativeDecoderSession(
                source,
                videoStreamId,
                audioStreamId,
                videoTimeBase,
                audioTimeBase,
                video!,
                audio!);
            video = null;
            audio = null;
            return new(PlaybackBoundedFlvNativeDecoderSessionResult.Opened, session);
        }
        catch
        {
            return Fail(PlaybackBoundedFlvNativeDecoderSessionResult.Faulted);
        }
        finally
        {
            if (!videoGateConsumed)
            {
                DisposeGate(videoGate);
            }

            if (!audioGateConsumed)
            {
                DisposeGate(audioGate);
            }
        }

        PlaybackBoundedFlvNativeDecoderSessionOpening Fail(
            PlaybackBoundedFlvNativeDecoderSessionResult result)
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
                    ownedBinding.Dispose();
                }
            }

            return new(result, null);
        }
    }

    private static bool IsCreated(
        PlaybackBoundedFlvNativeContextCreation creation,
        PlaybackDecoderFfmpegCodecId codecId) =>
        Enum.IsDefined(creation.Result) &&
        creation.Result == PlaybackDecoderFfmpegContextLeaseResult.Created &&
        creation.Owner is { } owner &&
        owner.CodecId == codecId;

    private void DisposeGate(object? gate)
    {
        if (gate is not null)
        {
            _operations.DisposeUnusedGate(gate);
        }
    }
}
