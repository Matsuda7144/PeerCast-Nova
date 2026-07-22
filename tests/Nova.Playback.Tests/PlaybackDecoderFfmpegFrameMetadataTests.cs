namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDecoderFfmpegFrameMetadataTests
{
    private static readonly PlaybackDecoderFfmpegRawFrameMetadata ValidVideo = new(
        0, 1920, 1080, 0, 0, 0, 90_000, 90_000, 3_000, 1, 90_000);

    private static readonly PlaybackDecoderFfmpegRawFrameMetadata ValidAudio = new(
        8, 0, 0, 48_000, 2, 1024, 48_000, 48_000, 1024, 1, 48_000);

    [Fact]
    public void ReceiveAndExtract_ValidVideoProducesFixedManagedMetadata()
    {
        using var owners = new TestOwners(PlaybackDecoderFfmpegCodecId.H264, opened: true);
        var consumer = new RecordingConsumer();
        var boundary = CreateBoundary(new FakeMetadataNativeApi(ValidVideo));

        var result = boundary.ReceiveAndExtract(owners.Context, consumer);

        Assert.Equal(PlaybackDecoderFfmpegFrameMetadataResult.Extracted, result);
        var metadata = Assert.Single(consumer.Items);
        Assert.Equal(PlaybackDecoderFfmpegFrameMediaKind.Video, metadata.MediaKind);
        Assert.Equal(PlaybackDecoderFfmpegFrameFormat.VideoYuv420P, metadata.Format);
        Assert.Equal(1920, metadata.Width);
        Assert.Equal(1080, metadata.Height);
        Assert.True(metadata.PresentationTimestamp.IsKnown);
        Assert.Equal(TimeSpan.FromSeconds(1), metadata.PresentationTimestamp.Value);
        Assert.True(metadata.Duration.IsKnown);
        Assert.Equal(TimeSpan.FromSeconds(1.0 / 30), metadata.Duration.Value);
    }

    [Fact]
    public void ReceiveAndExtract_ValidAudioProducesFixedManagedMetadata()
    {
        using var owners = new TestOwners(PlaybackDecoderFfmpegCodecId.Aac, opened: true);
        var consumer = new RecordingConsumer();
        var boundary = CreateBoundary(new FakeMetadataNativeApi(ValidAudio));

        Assert.Equal(
            PlaybackDecoderFfmpegFrameMetadataResult.Extracted,
            boundary.ReceiveAndExtract(owners.Context, consumer));
        var metadata = Assert.Single(consumer.Items);
        Assert.Equal(PlaybackDecoderFfmpegFrameMediaKind.Audio, metadata.MediaKind);
        Assert.Equal(PlaybackDecoderFfmpegFrameFormat.AudioFloat32Planar, metadata.Format);
        Assert.Equal(48_000, metadata.SampleRate);
        Assert.Equal(2, metadata.ChannelCount);
        Assert.Equal(1024, metadata.SampleCount);
    }

    [Theory]
    [MemberData(nameof(InvalidVideoMetadata))]
    public void ReceiveAndExtract_InvalidVideoMetadataFailsClosed(
        object value)
    {
        var raw = Assert.IsType<PlaybackDecoderFfmpegRawFrameMetadata>(value);
        using var owners = new TestOwners(PlaybackDecoderFfmpegCodecId.H264, opened: true);
        var consumer = new RecordingConsumer();

        Assert.Equal(
            PlaybackDecoderFfmpegFrameMetadataResult.InvalidMetadata,
            CreateBoundary(new FakeMetadataNativeApi(raw))
                .ReceiveAndExtract(owners.Context, consumer));
        Assert.Empty(consumer.Items);
        Assert.False(owners.Context.IsDisposed);
    }

    public static TheoryData<object> InvalidVideoMetadata =>
        new()
        {
            ValidVideo with { Format = -1 },
            ValidVideo with { Format = int.MaxValue },
            ValidVideo with { Width = -1 },
            ValidVideo with { Width = 0 },
            ValidVideo with { Width = PlaybackDecodedVideoFrame.MaximumDimension + 1 },
            ValidVideo with { Height = -1 },
            ValidVideo with { Height = 0 },
            ValidVideo with { Height = PlaybackDecodedVideoFrame.MaximumDimension + 1 },
            ValidVideo with { Duration = -1 },
            ValidVideo with { Duration = 900_001 },
            ValidVideo with { TimeBaseNumerator = -1 },
            ValidVideo with { TimeBaseDenominator = 0 },
            ValidVideo with
            {
                PresentationTimestamp = long.MaxValue,
                TimeBaseNumerator = int.MaxValue,
                TimeBaseDenominator = 1,
            },
            ValidVideo with
            {
                Duration = long.MaxValue,
                TimeBaseNumerator = int.MaxValue,
                TimeBaseDenominator = 1,
            },
        };

    [Fact]
    public void ReceiveAndExtract_UnsetTimeBaseProducesFixedUnknownTimes()
    {
        using var owners = new TestOwners(PlaybackDecoderFfmpegCodecId.H264, opened: true);
        var consumer = new RecordingConsumer();
        var raw = ValidVideo with { TimeBaseNumerator = 0, TimeBaseDenominator = 1 };

        Assert.Equal(
            PlaybackDecoderFfmpegFrameMetadataResult.Extracted,
            CreateBoundary(new FakeMetadataNativeApi(raw))
                .ReceiveAndExtract(owners.Context, consumer));
        var metadata = Assert.Single(consumer.Items);
        Assert.Equal(PlaybackDecoderFfmpegFrameTime.Unknown, metadata.PresentationTimestamp);
        Assert.Equal(PlaybackDecoderFfmpegFrameTime.Unknown, metadata.BestEffortTimestamp);
        Assert.Equal(PlaybackDecoderFfmpegFrameTime.Unknown, metadata.Duration);
    }

    [Theory]
    [InlineData(PlaybackDecoderFfmpegCodecId.H264)]
    [InlineData(PlaybackDecoderFfmpegCodecId.Aac)]
    public void ReceiveAndExtract_UnsetRawTimeBaseUsesConfiguredPacketTimeBase(
        PlaybackDecoderFfmpegCodecId codecId)
    {
        using var owners = new TestOwners(
            codecId,
            opened: true,
            configuredPacketTimeBase: new(1, 1_000));
        var consumer = new RecordingConsumer();
        var source = codecId == PlaybackDecoderFfmpegCodecId.H264 ? ValidVideo : ValidAudio;
        var raw = source with
        {
            PresentationTimestamp = 621,
            BestEffortTimestamp = 621,
            Duration = 0,
            TimeBaseNumerator = 0,
            TimeBaseDenominator = 1,
        };

        Assert.Equal(
            PlaybackDecoderFfmpegFrameMetadataResult.Extracted,
            CreateBoundary(new FakeMetadataNativeApi(raw))
                .ReceiveAndExtract(owners.Context, consumer));
        var metadata = Assert.Single(consumer.Items);
        Assert.Equal(TimeSpan.FromMilliseconds(621), metadata.PresentationTimestamp.Value);
        Assert.Equal(TimeSpan.FromMilliseconds(621), metadata.BestEffortTimestamp.Value);
        Assert.False(metadata.Duration.IsKnown);
    }

    [Fact]
    public void ReceiveAndExtract_ValidRawTimeBaseRemainsAuthoritative()
    {
        using var owners = new TestOwners(
            PlaybackDecoderFfmpegCodecId.H264,
            opened: true,
            configuredPacketTimeBase: new(1, 1_000));
        var consumer = new RecordingConsumer();
        var raw = ValidVideo with
        {
            PresentationTimestamp = 621,
            BestEffortTimestamp = 621,
            Duration = 1,
            TimeBaseNumerator = 1,
            TimeBaseDenominator = 100,
        };

        Assert.Equal(
            PlaybackDecoderFfmpegFrameMetadataResult.Extracted,
            CreateBoundary(new FakeMetadataNativeApi(raw))
                .ReceiveAndExtract(owners.Context, consumer));
        var metadata = Assert.Single(consumer.Items);
        Assert.Equal(TimeSpan.FromMilliseconds(6_210), metadata.PresentationTimestamp.Value);
        Assert.Equal(TimeSpan.FromMilliseconds(10), metadata.Duration.Value);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(0, -1)]
    public void ReceiveAndExtract_InvalidRawTimeBaseNeverFallsBack(
        int numerator,
        int denominator)
    {
        using var owners = new TestOwners(
            PlaybackDecoderFfmpegCodecId.H264,
            opened: true,
            configuredPacketTimeBase: new(1, 1_000));
        var raw = ValidVideo with
        {
            TimeBaseNumerator = numerator,
            TimeBaseDenominator = denominator,
        };

        var result = CreateBoundary(new FakeMetadataNativeApi(raw)).ReceiveAndExtract(
            owners.Context,
            new RecordingConsumer());

        Assert.Equal(PlaybackDecoderFfmpegFrameMetadataResult.InvalidMetadata, result);
    }

    [Fact]
    public void ReceiveAndExtract_FfmpegUnknownTimestampsAndZeroDurationAreAllowed()
    {
        using var owners = new TestOwners(PlaybackDecoderFfmpegCodecId.H264, opened: true);
        var consumer = new RecordingConsumer();
        var raw = ValidVideo with
        {
            PresentationTimestamp = long.MinValue,
            BestEffortTimestamp = long.MinValue,
            Duration = 0,
        };

        Assert.Equal(
            PlaybackDecoderFfmpegFrameMetadataResult.Extracted,
            CreateBoundary(new FakeMetadataNativeApi(raw))
                .ReceiveAndExtract(owners.Context, consumer));
        var metadata = Assert.Single(consumer.Items);
        Assert.False(metadata.PresentationTimestamp.IsKnown);
        Assert.False(metadata.BestEffortTimestamp.IsKnown);
        Assert.False(metadata.Duration.IsKnown);
    }

    [Fact]
    public void ReceiveAndExtract_NegativeKnownTimestampUsesSignedFfmpegTimeBase()
    {
        using var owners = new TestOwners(PlaybackDecoderFfmpegCodecId.H264, opened: true);
        var consumer = new RecordingConsumer();
        var raw = ValidVideo with
        {
            PresentationTimestamp = -90_000,
            BestEffortTimestamp = -90_000,
        };

        Assert.Equal(
            PlaybackDecoderFfmpegFrameMetadataResult.Extracted,
            CreateBoundary(new FakeMetadataNativeApi(raw))
                .ReceiveAndExtract(owners.Context, consumer));
        var metadata = Assert.Single(consumer.Items);
        Assert.Equal(TimeSpan.FromSeconds(-1), metadata.PresentationTimestamp.Value);
        Assert.Equal(TimeSpan.FromSeconds(-1), metadata.BestEffortTimestamp.Value);
    }

    [Theory]
    [MemberData(nameof(InvalidAudioMetadata))]
    public void ReceiveAndExtract_InvalidAudioMetadataFailsClosed(
        object value)
    {
        var raw = Assert.IsType<PlaybackDecoderFfmpegRawFrameMetadata>(value);
        using var owners = new TestOwners(PlaybackDecoderFfmpegCodecId.Aac, opened: true);
        var consumer = new RecordingConsumer();

        Assert.Equal(
            PlaybackDecoderFfmpegFrameMetadataResult.InvalidMetadata,
            CreateBoundary(new FakeMetadataNativeApi(raw))
                .ReceiveAndExtract(owners.Context, consumer));
        Assert.Empty(consumer.Items);
    }

    public static TheoryData<object> InvalidAudioMetadata =>
        new()
        {
            ValidAudio with { Format = -1 },
            ValidAudio with { SampleRate = -1 },
            ValidAudio with { SampleRate = 0 },
            ValidAudio with { SampleRate = PlaybackDecodedAudioFrame.MaximumSampleRate + 1 },
            ValidAudio with { ChannelCount = -1 },
            ValidAudio with { ChannelCount = 0 },
            ValidAudio with { ChannelCount = PlaybackDecodedAudioFrame.MaximumChannelCount + 1 },
            ValidAudio with { SampleCount = -1 },
            ValidAudio with { SampleCount = 0 },
            ValidAudio with { SampleCount = PlaybackDecodedAudioFrame.MaximumSampleCount + 1 },
        };

    [Fact]
    public void ReceiveAndExtract_ConsumerRejectionPreservesContext()
    {
        using var owners = new TestOwners(PlaybackDecoderFfmpegCodecId.H264, opened: true);
        var boundary = CreateBoundary(new FakeMetadataNativeApi(ValidVideo));

        var result = boundary.ReceiveAndExtract(owners.Context, new RecordingConsumer(false));

        Assert.Equal(PlaybackDecoderFfmpegFrameMetadataResult.ConsumerRejected, result);
        Assert.False(owners.Context.IsDisposed);
    }

    [Fact]
    public void ReceiveAndExtract_NativeExceptionDisposesContextWithoutLeakingInput()
    {
        using var owners = new TestOwners(PlaybackDecoderFfmpegCodecId.H264, opened: true);
        var boundary = CreateBoundary(new ThrowingMetadataNativeApi());

        var result = boundary.ReceiveAndExtract(owners.Context, new RecordingConsumer());

        Assert.Equal(PlaybackDecoderFfmpegFrameMetadataResult.NativeFailure, result);
        Assert.True(owners.Context.IsDisposed);
        Assert.DoesNotContain("sensitive", result.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0x", result.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false, null, PlaybackDecoderFfmpegFrameMetadataResult.ContextNotOpened)]
    [InlineData(true, "av_frame_free", PlaybackDecoderFfmpegFrameMetadataResult.GateRejected)]
    public void ReceiveAndExtract_UnavailableBoundaryAvoidsMetadataRead(
        bool opened,
        string? omittedSymbol,
        PlaybackDecoderFfmpegFrameMetadataResult expected)
    {
        using var owners = new TestOwners(
            PlaybackDecoderFfmpegCodecId.H264,
            opened,
            omittedSymbol);
        var metadataApi = new FakeMetadataNativeApi(ValidVideo);

        Assert.Equal(
            expected,
            CreateBoundary(metadataApi).ReceiveAndExtract(owners.Context, new RecordingConsumer()));
        Assert.Equal(0, metadataApi.CallCount);
    }

    [Fact]
    public void ReceiveAndExtract_DisposedContextIsRejected()
    {
        using var owners = new TestOwners(PlaybackDecoderFfmpegCodecId.H264, opened: true);
        owners.Context.Dispose();
        var metadataApi = new FakeMetadataNativeApi(ValidVideo);

        Assert.Equal(
            PlaybackDecoderFfmpegFrameMetadataResult.ContextUnavailable,
            CreateBoundary(metadataApi).ReceiveAndExtract(owners.Context, new RecordingConsumer()));
        Assert.Equal(0, metadataApi.CallCount);
    }

    [Fact]
    public void ReceiveAndExtract_UnsupportedPlatformNeverCallsNativeBoundary()
    {
        using var owners = new TestOwners(PlaybackDecoderFfmpegCodecId.H264, opened: true);
        var frameApi = new FakeFrameNativeApi();
        var metadataApi = new FakeMetadataNativeApi(ValidVideo);
        var frameBoundary = new PlaybackDecoderFfmpegNativeFrameBoundary(
            frameApi,
            new RejectingPlaybackDecoderFfmpegNativeFrameConsumer(),
            true);
        var boundary = new PlaybackDecoderFfmpegFrameMetadataBoundary(
            frameBoundary,
            metadataApi,
            false);

        Assert.Equal(
            PlaybackDecoderFfmpegFrameMetadataResult.Unsupported,
            boundary.ReceiveAndExtract(owners.Context, new RecordingConsumer()));
        Assert.Equal(0, frameApi.CallCount);
        Assert.Equal(0, metadataApi.CallCount);
    }

    private static PlaybackDecoderFfmpegFrameMetadataBoundary CreateBoundary(
        IPlaybackDecoderFfmpegFrameMetadataNativeApi metadataApi)
    {
        var frameBoundary = new PlaybackDecoderFfmpegNativeFrameBoundary(
            new FakeFrameNativeApi(),
            new RejectingPlaybackDecoderFfmpegNativeFrameConsumer(),
            true);
        return new(frameBoundary, metadataApi, true);
    }

    private sealed class RecordingConsumer(bool accept = true) :
        IPlaybackDecoderFfmpegFrameMetadataConsumer
    {
        public List<PlaybackDecoderFfmpegFrameMetadata> Items { get; } = [];

        public bool Consume(PlaybackDecoderFfmpegFrameMetadata metadata)
        {
            Items.Add(metadata);
            return accept;
        }
    }

    private sealed class FakeMetadataNativeApi(PlaybackDecoderFfmpegRawFrameMetadata metadata) :
        IPlaybackDecoderFfmpegFrameMetadataNativeApi
    {
        public int CallCount { get; private set; }

        public PlaybackDecoderFfmpegRawFrameMetadata Read(nint frame)
        {
            Assert.Equal((nint)400, frame);
            CallCount++;
            return metadata;
        }
    }

    private sealed class ThrowingMetadataNativeApi :
        IPlaybackDecoderFfmpegFrameMetadataNativeApi
    {
        public PlaybackDecoderFfmpegRawFrameMetadata Read(nint frame) =>
            throw new InvalidOperationException("sensitive pointer 0x1234");
    }

    private sealed class FakeFrameNativeApi : IPlaybackDecoderFfmpegNativeFrameApi
    {
        public int CallCount { get; private set; }

        public PlaybackDecoderFfmpegNativeFrameReceiveResult ReceiveAndConsume(
            nint frameAllocFunction,
            nint frameFreeFunction,
            nint receiveFrameFunction,
            nint context,
            IPlaybackDecoderFfmpegNativeFrameConsumer consumer)
        {
            CallCount++;
            if (!consumer.Consume((nint)400))
            {
                throw new PlaybackDecoderFfmpegFrameConsumerRejectedException();
            }

            return PlaybackDecoderFfmpegNativeFrameReceiveResult.Success;
        }
    }

    private sealed class TestOwners : IDisposable
    {
        private readonly string _directory;
        private readonly VerifiedPlaybackDecoderRuntimeLease _runtime;

        public TestOwners(
            PlaybackDecoderFfmpegCodecId codecId,
            bool opened,
            string? omitSymbol = null,
            PlaybackBoundedDemuxTimeBase? configuredPacketTimeBase = null)
        {
            _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, "runtime.bin");
            File.WriteAllBytes(path, [1]);
            _runtime = new VerifiedPlaybackDecoderRuntimeLease(
                new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase)
                {
                    ["runtime.bin"] = new(path, FileMode.Open, FileAccess.Read, FileShare.Read),
                },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["runtime.bin"] = path,
                },
                []);

            var names = new[] { "av_frame_alloc", "av_frame_free", "avcodec_receive_frame" };
            var symbols = names
                .Where(name => !string.Equals(name, omitSymbol, StringComparison.Ordinal))
                .Select((name, index) => (name, value: (nint)(index + 1)))
                .ToDictionary(pair => pair.name, pair => pair.value, StringComparer.Ordinal);
            var gate = new PlaybackDecoderFfmpegAbiSymbolGateLease(
                _runtime,
                new FakeLoader(),
                [(nint)100],
                symbols);
            Context = new PlaybackDecoderFfmpegContextLease(
                _runtime,
                gate,
                new FakeContextNativeApi(),
                codecId,
                (nint)200,
                (nint)300);
            if (opened)
            {
                Context.MarkParametersTransferred();
                if (configuredPacketTimeBase is { } timeBase)
                {
                    Context.MarkPacketTimeBaseConfigured(timeBase);
                }

                Context.MarkOpened();
            }
        }

        public PlaybackDecoderFfmpegContextLease Context { get; }

        public void Dispose()
        {
            Context.Dispose();
            _runtime.Dispose();
            Directory.Delete(_directory, true);
        }
    }

    private sealed class FakeLoader : IPlaybackDecoderNativeLibraryLoader
    {
        public nint Load(string verifiedPath, PlaybackDecoderNativeSearchScope searchScope) => 0;
        public nint GetSymbol(nint module, string symbol) => 0;
        public uint InvokeVersion(nint symbol) => 0;
        public void Free(nint module) { }
    }

    private sealed class FakeContextNativeApi : IPlaybackDecoderFfmpegContextNativeApi
    {
        public nint FindDecoder(nint function, PlaybackDecoderFfmpegCodecId codecId) => 0;
        public nint AllocateContext(nint function, nint decoder) => 0;
        public void FreeContext(nint function, ref nint context) => context = 0;
    }
}
