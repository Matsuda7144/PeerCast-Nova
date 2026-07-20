namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackBoundedFlvDecoderParameterBindingTests
{
    [Fact]
    public void Prepare_CreatesExactOwnedVideoAndAudioParameters()
    {
        using var session = CreateSession();
        var adapter = new PlaybackBoundedFlvDecoderParameterBindingAdapter();

        var preparation = adapter.Prepare(session);

        Assert.Equal(PlaybackBoundedFlvDecoderParameterBindingResult.Prepared, preparation.Result);
        using var binding = Assert.IsType<PlaybackBoundedFlvDecoderParameterBinding>(
            preparation.Binding);
        Assert.Same(session, binding.SourceSession);
        Assert.True(binding.ReferencesSourceSession(session));
        Assert.Equal(0, binding.VideoStreamId);
        Assert.Equal(1, binding.AudioStreamId);
        Assert.Equal(new PlaybackBoundedDemuxTimeBase(1, 1_000), binding.VideoTimeBase);
        Assert.Equal(new PlaybackBoundedDemuxTimeBase(1, 1_000), binding.AudioTimeBase);

        binding.VideoParameters.UseOwnedParameters(parameters =>
        {
            Assert.Equal(PlaybackDecoderFfmpegCodecId.H264, parameters.CodecId);
            Assert.Equal(1280, parameters.Width);
            Assert.Equal(720, parameters.Height);
            Assert.Equal(new byte[] { 1, 100, 0, 31 }, parameters.Extradata.ToArray());
            Assert.All(parameters.Padding.ToArray(), value => Assert.Equal(0, value));
            return 0;
        });
        binding.AudioParameters.UseOwnedParameters(parameters =>
        {
            Assert.Equal(PlaybackDecoderFfmpegCodecId.Aac, parameters.CodecId);
            Assert.Equal(48_000, parameters.SampleRate);
            Assert.Equal(2, parameters.ChannelCount);
            Assert.Equal(new byte[] { 0x11, 0x90 }, parameters.Extradata.ToArray());
            Assert.All(parameters.Padding.ToArray(), value => Assert.Equal(0, value));
            return 0;
        });
    }

    [Fact]
    public void Binding_OwnsParametersIndependentlyFromSourceSession()
    {
        var session = CreateSession();
        var preparation = new PlaybackBoundedFlvDecoderParameterBindingAdapter().Prepare(session);
        using var binding = Assert.IsType<PlaybackBoundedFlvDecoderParameterBinding>(
            preparation.Binding);

        session.Dispose();

        Assert.True(session.IsDisposed);
        Assert.False(binding.IsDisposed);
        binding.VideoParameters.UseOwnedParameters(parameters =>
        {
            Assert.Equal(new byte[] { 1, 100, 0, 31 }, parameters.Extradata.ToArray());
            return 0;
        });
        binding.AudioParameters.UseOwnedParameters(parameters =>
        {
            Assert.Equal(new byte[] { 0x11, 0x90 }, parameters.Extradata.ToArray());
            return 0;
        });
    }

    [Fact]
    public void BindingDisposal_DoesNotDisposeSourceAndIsIdempotent()
    {
        using var session = CreateSession();
        var preparation = new PlaybackBoundedFlvDecoderParameterBindingAdapter().Prepare(session);
        var binding = Assert.IsType<PlaybackBoundedFlvDecoderParameterBinding>(preparation.Binding);
        var video = binding.VideoParameters;
        var audio = binding.AudioParameters;

        binding.Dispose();
        binding.Dispose();

        Assert.True(binding.IsDisposed);
        Assert.True(video.IsDisposed);
        Assert.True(audio.IsDisposed);
        Assert.False(session.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => _ = binding.VideoParameters);
        Assert.Throws<ObjectDisposedException>(() => _ = binding.AudioParameters);
    }

    [Fact]
    public void Prepare_RejectsNullAndDisposedSources()
    {
        var adapter = new PlaybackBoundedFlvDecoderParameterBindingAdapter();
        Assert.Equal(
            PlaybackBoundedFlvDecoderParameterBindingResult.InvalidSession,
            adapter.Prepare(null).Result);

        var session = CreateSession();
        session.Dispose();
        var disposed = adapter.Prepare(session);
        Assert.Equal(PlaybackBoundedFlvDecoderParameterBindingResult.SourceDisposed, disposed.Result);
        Assert.Null(disposed.Binding);
    }

    [Theory]
    [InlineData(PlaybackBoundedDemuxContainerKind.Mp4, 0, 1, 1, 1_000)]
    [InlineData(PlaybackBoundedDemuxContainerKind.Flv, 2, 3, 1, 1_000)]
    [InlineData(PlaybackBoundedDemuxContainerKind.Flv, 0, 1, 1, 90_000)]
    public void Prepare_RejectsSessionsOutsideExactFlvContract(
        PlaybackBoundedDemuxContainerKind container,
        int videoStreamId,
        int audioStreamId,
        int timeBaseNumerator,
        int timeBaseDenominator)
    {
        using var session = CreateSession(
            container,
            videoStreamId,
            audioStreamId,
            new PlaybackBoundedDemuxTimeBase(timeBaseNumerator, timeBaseDenominator));

        var preparation = new PlaybackBoundedFlvDecoderParameterBindingAdapter().Prepare(session);

        Assert.Equal(
            PlaybackBoundedFlvDecoderParameterBindingResult.InvalidSession,
            preparation.Result);
        Assert.Null(preparation.Binding);
    }

    [Fact]
    public void Prepare_VideoRejectionStopsBeforeAudio()
    {
        using var session = CreateSession();
        var operations = new FakeOperations
        {
            VideoResult = PlaybackDecoderFfmpegCodecParametersResult.EmptyExtradata,
        };
        var adapter = new PlaybackBoundedFlvDecoderParameterBindingAdapter(operations);

        var preparation = adapter.Prepare(session);

        Assert.Equal(
            PlaybackBoundedFlvDecoderParameterBindingResult.VideoParametersRejected,
            preparation.Result);
        Assert.Null(preparation.Binding);
        Assert.Equal(["video"], operations.Calls);
    }

    [Fact]
    public void Prepare_AudioRejectionDisposesCreatedVideoLease()
    {
        using var session = CreateSession();
        var operations = new FakeOperations
        {
            AudioResult = PlaybackDecoderFfmpegCodecParametersResult.EmptyExtradata,
        };
        var adapter = new PlaybackBoundedFlvDecoderParameterBindingAdapter(operations);

        var preparation = adapter.Prepare(session);

        Assert.Equal(
            PlaybackBoundedFlvDecoderParameterBindingResult.AudioParametersRejected,
            preparation.Result);
        Assert.Null(preparation.Binding);
        Assert.Equal(["video", "audio"], operations.Calls);
        var video = Assert.IsType<PlaybackDecoderFfmpegCodecParametersLease>(
            operations.CreatedVideoLease);
        Assert.True(video.IsDisposed);
    }

    [Fact]
    public void Prepare_ExceptionAfterVideoCreationDisposesPartialOwnership()
    {
        using var session = CreateSession();
        var operations = new FakeOperations
        {
            ThrowOnAudio = true,
        };
        var adapter = new PlaybackBoundedFlvDecoderParameterBindingAdapter(operations);

        var preparation = adapter.Prepare(session);

        Assert.Equal(PlaybackBoundedFlvDecoderParameterBindingResult.Faulted, preparation.Result);
        Assert.Null(preparation.Binding);
        var video = Assert.IsType<PlaybackDecoderFfmpegCodecParametersLease>(
            operations.CreatedVideoLease);
        Assert.True(video.IsDisposed);
    }

    [Fact]
    public void Prepare_UndefinedFactoryResultFailsClosedAndDisposesUnexpectedLease()
    {
        using var session = CreateSession();
        var operations = new FakeOperations
        {
            VideoResult = (PlaybackDecoderFfmpegCodecParametersResult)int.MaxValue,
            ReturnLeaseForRejectedVideo = true,
        };
        var adapter = new PlaybackBoundedFlvDecoderParameterBindingAdapter(operations);

        var preparation = adapter.Prepare(session);

        Assert.Equal(PlaybackBoundedFlvDecoderParameterBindingResult.Faulted, preparation.Result);
        Assert.Null(preparation.Binding);
        var video = Assert.IsType<PlaybackDecoderFfmpegCodecParametersLease>(
            operations.CreatedVideoLease);
        Assert.True(video.IsDisposed);
        Assert.Equal(["video"], operations.Calls);
    }

    [Fact]
    public void Prepare_SourceDisposedAfterAudioCreationDisposesBothLeases()
    {
        var session = CreateSession();
        var operations = new FakeOperations
        {
            AfterAudio = session.Dispose,
        };
        var adapter = new PlaybackBoundedFlvDecoderParameterBindingAdapter(operations);

        var preparation = adapter.Prepare(session);

        Assert.Equal(
            PlaybackBoundedFlvDecoderParameterBindingResult.SourceDisposed,
            preparation.Result);
        Assert.Null(preparation.Binding);
        var video = Assert.IsType<PlaybackDecoderFfmpegCodecParametersLease>(
            operations.CreatedVideoLease);
        var audio = Assert.IsType<PlaybackDecoderFfmpegCodecParametersLease>(
            operations.CreatedAudioLease);
        Assert.True(video.IsDisposed);
        Assert.True(audio.IsDisposed);
    }

    [Fact]
    public async Task DisposeAsync_ClearsBothParameterLeases()
    {
        using var session = CreateSession();
        var preparation = new PlaybackBoundedFlvDecoderParameterBindingAdapter().Prepare(session);
        var binding = Assert.IsType<PlaybackBoundedFlvDecoderParameterBinding>(preparation.Binding);
        var video = binding.VideoParameters;
        var audio = binding.AudioParameters;

        await binding.DisposeAsync();

        Assert.True(binding.IsDisposed);
        Assert.True(video.IsDisposed);
        Assert.True(audio.IsDisposed);
    }

    private static PlaybackBoundedDemuxSession CreateSession(
        PlaybackBoundedDemuxContainerKind container = PlaybackBoundedDemuxContainerKind.Flv,
        int videoStreamId = 0,
        int audioStreamId = 1,
        PlaybackBoundedDemuxTimeBase? timeBase = null)
    {
        var selectedTimeBase = timeBase ?? new PlaybackBoundedDemuxTimeBase(1, 1_000);
        var input = new PlaybackBoundedDemuxSessionDescriptionInput(
            container,
            new PlaybackBoundedDemuxStreamDescriptionInput(
                videoStreamId,
                PlaybackBoundedDemuxMediaKind.Video,
                PlaybackBoundedDemuxCodec.H264,
                Width: 1280,
                Height: 720,
                SampleRate: 0,
                ChannelCount: 0,
                selectedTimeBase,
                new byte[] { 1, 100, 0, 31 }),
            new PlaybackBoundedDemuxStreamDescriptionInput(
                audioStreamId,
                PlaybackBoundedDemuxMediaKind.Audio,
                PlaybackBoundedDemuxCodec.Aac,
                Width: 0,
                Height: 0,
                SampleRate: 48_000,
                ChannelCount: 2,
                selectedTimeBase,
                new byte[] { 0x11, 0x90 }));

        Assert.Equal(
            PlaybackBoundedDemuxResult.StreamDescriptionAvailable,
            PlaybackBoundedDemuxSession.TryCreate(
                new PlaybackBoundedDemuxOptions(),
                input,
                out var session));
        return Assert.IsType<PlaybackBoundedDemuxSession>(session);
    }

    private sealed class FakeOperations :
        IPlaybackBoundedFlvDecoderParameterBindingOperations
    {
        public List<string> Calls { get; } = [];

        public PlaybackDecoderFfmpegCodecParametersResult VideoResult { get; init; } =
            PlaybackDecoderFfmpegCodecParametersResult.Created;

        public PlaybackDecoderFfmpegCodecParametersResult AudioResult { get; init; } =
            PlaybackDecoderFfmpegCodecParametersResult.Created;

        public bool ReturnLeaseForRejectedVideo { get; init; }

        public bool ThrowOnAudio { get; init; }

        public Action? AfterAudio { get; init; }

        public PlaybackDecoderFfmpegCodecParametersLease? CreatedVideoLease { get; private set; }

        public PlaybackDecoderFfmpegCodecParametersLease? CreatedAudioLease { get; private set; }

        public PlaybackBoundedFlvDecoderParameterCreation CreateVideo(
            PlaybackBoundedDemuxStreamDescription video)
        {
            Calls.Add("video");
            var valid = PlaybackDecoderFfmpegCodecParametersFactory.Create(
                PlaybackDecoderFfmpegCodecParametersInput.ForH264(
                    video.Width,
                    video.Height,
                    video.CodecExtradata));
            CreatedVideoLease = valid.Lease;
            if (VideoResult == PlaybackDecoderFfmpegCodecParametersResult.Created)
            {
                return new(VideoResult, CreatedVideoLease);
            }

            return new(
                VideoResult,
                ReturnLeaseForRejectedVideo ? CreatedVideoLease : DisposeAndReturnNull(CreatedVideoLease));
        }

        public PlaybackBoundedFlvDecoderParameterCreation CreateAudio(
            PlaybackBoundedDemuxStreamDescription audio)
        {
            Calls.Add("audio");
            if (ThrowOnAudio)
            {
                throw new InvalidOperationException("Injected audio creation failure.");
            }

            var valid = PlaybackDecoderFfmpegCodecParametersFactory.Create(
                PlaybackDecoderFfmpegCodecParametersInput.ForAac(
                    audio.SampleRate,
                    audio.ChannelCount,
                    audio.CodecExtradata));
            CreatedAudioLease = valid.Lease;
            AfterAudio?.Invoke();
            if (AudioResult == PlaybackDecoderFfmpegCodecParametersResult.Created)
            {
                return new(AudioResult, CreatedAudioLease);
            }

            CreatedAudioLease?.Dispose();
            return new(AudioResult, null);
        }

        private static PlaybackDecoderFfmpegCodecParametersLease? DisposeAndReturnNull(
            PlaybackDecoderFfmpegCodecParametersLease? lease)
        {
            lease?.Dispose();
            return null;
        }
    }
}
