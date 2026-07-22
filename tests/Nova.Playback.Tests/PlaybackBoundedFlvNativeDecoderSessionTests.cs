namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackBoundedFlvNativeDecoderSessionTests
{
    [Fact]
    public void Open_CreatesSourceBoundOpenedPairInStrictOrder()
    {
        using var source = CreateSession();
        var binding = CreateBinding(source);
        var videoGate = new FakeGate("video-gate");
        var audioGate = new FakeGate("audio-gate");
        var operations = new FakeOperations();
        var factory = new PlaybackBoundedFlvNativeDecoderSessionFactory(operations);

        var opening = factory.OpenCore(binding, videoGate, audioGate);

        Assert.Equal(PlaybackBoundedFlvNativeDecoderSessionResult.Opened, opening.Result);
        using var session = Assert.IsType<PlaybackBoundedFlvNativeDecoderSession>(opening.Session);
        Assert.Same(source, session.SourceSession);
        Assert.True(session.ReferencesSourceSession(source));
        Assert.Equal(0, session.VideoStreamId);
        Assert.Equal(1, session.AudioStreamId);
        Assert.Equal(new PlaybackBoundedDemuxTimeBase(1, 1_000), session.VideoTimeBase);
        Assert.Equal(new PlaybackBoundedDemuxTimeBase(1, 1_000), session.AudioTimeBase);
        Assert.True(binding.IsDisposed);
        Assert.Equal(
            [
                "create:H264", "transfer:H264", "time-base:H264:1/1000", "open:H264",
                "create:Aac", "transfer:Aac", "time-base:Aac:1/1000", "open:Aac",
            ],
            operations.Calls);
        Assert.False(videoGate.IsDisposed);
        Assert.False(audioGate.IsDisposed);

        session.Dispose();
        Assert.True(videoGate.IsDisposed);
        Assert.True(audioGate.IsDisposed);
        Assert.Equal(["audio", "video"], operations.Disposals);
        Assert.False(source.IsDisposed);
    }

    [Fact]
    public void Open_VideoTimeBaseFailureConsumesEverythingAndStopsBeforeOpen()
    {
        using var source = CreateSession();
        var binding = CreateBinding(source);
        var videoGate = new FakeGate("video-gate");
        var audioGate = new FakeGate("audio-gate");
        var operations = new FakeOperations
        {
            VideoTimeBaseResult =
                PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult.NativeRejected,
        };

        var opening = new PlaybackBoundedFlvNativeDecoderSessionFactory(operations)
            .OpenCore(binding, videoGate, audioGate);

        Assert.Equal(
            PlaybackBoundedFlvNativeDecoderSessionResult.VideoTimeBaseRejected,
            opening.Result);
        Assert.Null(opening.Session);
        Assert.True(binding.IsDisposed);
        Assert.True(videoGate.IsDisposed);
        Assert.True(audioGate.IsDisposed);
        Assert.Equal(
            ["create:H264", "transfer:H264", "time-base:H264:1/1000"],
            operations.Calls);
    }

    [Fact]
    public void Open_VideoTransferFailureConsumesEverythingAndStopsBeforeAudio()
    {
        using var source = CreateSession();
        var binding = CreateBinding(source);
        var videoGate = new FakeGate("video-gate");
        var audioGate = new FakeGate("audio-gate");
        var operations = new FakeOperations
        {
            VideoTransferResult = PlaybackDecoderFfmpegParameterTransferResult.NativeTransferRejected,
        };

        var opening = new PlaybackBoundedFlvNativeDecoderSessionFactory(operations)
            .OpenCore(binding, videoGate, audioGate);

        Assert.Equal(
            PlaybackBoundedFlvNativeDecoderSessionResult.VideoTransferRejected,
            opening.Result);
        Assert.Null(opening.Session);
        Assert.True(binding.IsDisposed);
        Assert.True(videoGate.IsDisposed);
        Assert.True(audioGate.IsDisposed);
        Assert.Equal(["create:H264", "transfer:H264"], operations.Calls);
    }

    [Fact]
    public void Open_AudioOpenFailureDisposesAudioBeforeVideo()
    {
        using var source = CreateSession();
        var binding = CreateBinding(source);
        var videoGate = new FakeGate("video-gate");
        var audioGate = new FakeGate("audio-gate");
        var operations = new FakeOperations
        {
            AudioOpenResult = PlaybackDecoderFfmpegCodecOpenResult.NativeOpenRejected,
        };

        var opening = new PlaybackBoundedFlvNativeDecoderSessionFactory(operations)
            .OpenCore(binding, videoGate, audioGate);

        Assert.Equal(PlaybackBoundedFlvNativeDecoderSessionResult.AudioOpenRejected, opening.Result);
        Assert.Null(opening.Session);
        Assert.True(binding.IsDisposed);
        Assert.True(videoGate.IsDisposed);
        Assert.True(audioGate.IsDisposed);
        Assert.Equal(["audio", "video"], operations.Disposals);
    }

    [Fact]
    public void Open_AudioTimeBaseFailureDisposesAudioBeforeVideoAndStopsBeforeOpen()
    {
        using var source = CreateSession();
        var binding = CreateBinding(source);
        var operations = new FakeOperations
        {
            AudioTimeBaseResult =
                PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult.NativeRejected,
        };

        var opening = new PlaybackBoundedFlvNativeDecoderSessionFactory(operations)
            .OpenCore(binding, new FakeGate("video-gate"), new FakeGate("audio-gate"));

        Assert.Equal(
            PlaybackBoundedFlvNativeDecoderSessionResult.AudioTimeBaseRejected,
            opening.Result);
        Assert.Null(opening.Session);
        Assert.Equal(["audio", "video"], operations.Disposals);
        Assert.Equal("time-base:Aac:1/1000", operations.Calls[^1]);
    }

    [Fact]
    public void Open_SourceDisposedBetweenStreamsStopsBeforeAudioCreation()
    {
        var source = CreateSession();
        var binding = CreateBinding(source);
        var videoGate = new FakeGate("video-gate");
        var audioGate = new FakeGate("audio-gate");
        var operations = new FakeOperations
        {
            AfterVideoOpen = source.Dispose,
        };

        var opening = new PlaybackBoundedFlvNativeDecoderSessionFactory(operations)
            .OpenCore(binding, videoGate, audioGate);

        Assert.Equal(PlaybackBoundedFlvNativeDecoderSessionResult.SourceDisposed, opening.Result);
        Assert.Null(opening.Session);
        Assert.True(binding.IsDisposed);
        Assert.True(videoGate.IsDisposed);
        Assert.True(audioGate.IsDisposed);
        Assert.Equal(
            ["create:H264", "transfer:H264", "time-base:H264:1/1000", "open:H264"],
            operations.Calls);
    }

    [Fact]
    public void Open_RejectsSharedGateAndConsumesBindingAndGate()
    {
        using var source = CreateSession();
        var binding = CreateBinding(source);
        var gate = new FakeGate("shared");
        var operations = new FakeOperations();

        var opening = new PlaybackBoundedFlvNativeDecoderSessionFactory(operations)
            .OpenCore(binding, gate, gate);

        Assert.Equal(
            PlaybackBoundedFlvNativeDecoderSessionResult.InvalidConfiguration,
            opening.Result);
        Assert.Null(opening.Session);
        Assert.True(binding.IsDisposed);
        Assert.True(gate.IsDisposed);
        Assert.Empty(operations.Calls);
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotentAndDoesNotDisposeSource()
    {
        using var source = CreateSession();
        var binding = CreateBinding(source);
        var operations = new FakeOperations();
        var opening = new PlaybackBoundedFlvNativeDecoderSessionFactory(operations)
            .OpenCore(binding, new FakeGate("video-gate"), new FakeGate("audio-gate"));
        var session = Assert.IsType<PlaybackBoundedFlvNativeDecoderSession>(opening.Session);

        await session.DisposeAsync();
        await session.DisposeAsync();

        Assert.True(session.IsDisposed);
        Assert.False(source.IsDisposed);
        Assert.Equal(["audio", "video"], operations.Disposals);
    }

    private static PlaybackBoundedFlvDecoderParameterBinding CreateBinding(
        PlaybackBoundedDemuxSession source)
    {
        var preparation = new PlaybackBoundedFlvDecoderParameterBindingAdapter().Prepare(source);
        Assert.Equal(PlaybackBoundedFlvDecoderParameterBindingResult.Prepared, preparation.Result);
        return Assert.IsType<PlaybackBoundedFlvDecoderParameterBinding>(preparation.Binding);
    }

    private static PlaybackBoundedDemuxSession CreateSession()
    {
        var timeBase = new PlaybackBoundedDemuxTimeBase(1, 1_000);
        var description = new PlaybackBoundedDemuxSessionDescriptionInput(
            PlaybackBoundedDemuxContainerKind.Flv,
            new PlaybackBoundedDemuxStreamDescriptionInput(
                0,
                PlaybackBoundedDemuxMediaKind.Video,
                PlaybackBoundedDemuxCodec.H264,
                1280,
                720,
                0,
                0,
                timeBase,
                new byte[] { 1, 100, 0, 31 }),
            new PlaybackBoundedDemuxStreamDescriptionInput(
                1,
                PlaybackBoundedDemuxMediaKind.Audio,
                PlaybackBoundedDemuxCodec.Aac,
                0,
                0,
                48_000,
                2,
                timeBase,
                new byte[] { 0x11, 0x90 }));
        Assert.Equal(
            PlaybackBoundedDemuxResult.StreamDescriptionAvailable,
            PlaybackBoundedDemuxSession.TryCreate(
                new PlaybackBoundedDemuxOptions(),
                description,
                out var session));
        return Assert.IsType<PlaybackBoundedDemuxSession>(session);
    }

    private sealed class FakeGate(string name)
    {
        public string Name { get; } = name;

        public bool IsDisposed { get; set; }
    }

    private sealed class FakeOwner(
        PlaybackDecoderFfmpegCodecId codecId,
        FakeGate gate,
        List<string> disposals) : IPlaybackBoundedFlvNativeContextOwner
    {
        private int _disposed;

        public PlaybackDecoderFfmpegCodecId CodecId { get; } = codecId;

        public PlaybackDecoderFfmpegContextLease? Context => null;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            gate.IsDisposed = true;
            disposals.Add(CodecId == PlaybackDecoderFfmpegCodecId.Aac ? "audio" : "video");
        }
    }

    private sealed class FakeOperations : IPlaybackBoundedFlvNativeDecoderSessionOperations
    {
        public List<string> Calls { get; } = [];

        public List<string> Disposals { get; } = [];

        public PlaybackDecoderFfmpegParameterTransferResult VideoTransferResult { get; init; } =
            PlaybackDecoderFfmpegParameterTransferResult.Applied;

        public PlaybackDecoderFfmpegParameterTransferResult AudioTransferResult { get; init; } =
            PlaybackDecoderFfmpegParameterTransferResult.Applied;

        public PlaybackDecoderFfmpegCodecOpenResult VideoOpenResult { get; init; } =
            PlaybackDecoderFfmpegCodecOpenResult.Opened;

        public PlaybackDecoderFfmpegCodecOpenResult AudioOpenResult { get; init; } =
            PlaybackDecoderFfmpegCodecOpenResult.Opened;

        public PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult VideoTimeBaseResult
        { get; init; } =
            PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult.Configured;

        public PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult AudioTimeBaseResult
        { get; init; } =
            PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult.Configured;

        public Action? AfterVideoOpen { get; init; }

        public PlaybackBoundedFlvNativeContextCreation CreateContext(
            object gate,
            PlaybackDecoderFfmpegCodecId codecId)
        {
            Calls.Add($"create:{codecId}");
            return new(
                PlaybackDecoderFfmpegContextLeaseResult.Created,
                new FakeOwner(codecId, (FakeGate)gate, Disposals));
        }

        public PlaybackDecoderFfmpegParameterTransferResult Transfer(
            IPlaybackBoundedFlvNativeContextOwner context,
            PlaybackDecoderFfmpegCodecParametersLease parameters)
        {
            Calls.Add($"transfer:{context.CodecId}");
            var result = context.CodecId == PlaybackDecoderFfmpegCodecId.H264
                ? VideoTransferResult
                : AudioTransferResult;
            if (result == PlaybackDecoderFfmpegParameterTransferResult.Applied)
            {
                parameters.Dispose();
            }

            return result;
        }

        public PlaybackDecoderFfmpegCodecOpenResult Open(
            IPlaybackBoundedFlvNativeContextOwner context)
        {
            Calls.Add($"open:{context.CodecId}");
            if (context.CodecId == PlaybackDecoderFfmpegCodecId.H264)
            {
                AfterVideoOpen?.Invoke();
                return VideoOpenResult;
            }

            return AudioOpenResult;
        }

        public PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult ConfigurePacketTimeBase(
            IPlaybackBoundedFlvNativeContextOwner context,
            PlaybackBoundedDemuxTimeBase timeBase)
        {
            Calls.Add(
                $"time-base:{context.CodecId}:{timeBase.Numerator}/{timeBase.Denominator}");
            return context.CodecId == PlaybackDecoderFfmpegCodecId.H264
                ? VideoTimeBaseResult
                : AudioTimeBaseResult;
        }

        public void DisposeUnusedGate(object gate)
        {
            ((FakeGate)gate).IsDisposed = true;
        }
    }
}
