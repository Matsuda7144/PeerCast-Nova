namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDecoderFfmpegDecodedOutputAdapterTests
{
    private static readonly string[] RequiredFrameSymbols =
        ["av_frame_alloc", "av_frame_free", "avcodec_receive_frame"];

    [Fact]
    public void Adapt_BgraUsesPaddingFreeStrideAndClearsCallbackPayload()
    {
        using var owners = new TestOwners();
        using var input = VideoFrame(
            PlaybackDecoderFfmpegFrameFormat.VideoBgra32,
            2,
            2,
            [Plane(0, stride: 12, rows: 2, logicalRowBytes: 8)]);
        var consumer = new RecordingConsumer();

        var result = new PlaybackDecoderFfmpegDecodedOutputAdapter().Adapt(
            owners.Context,
            input,
            sequence: 7,
            PlaybackDecodedFrameDiscontinuityKind.DecoderReset,
            consumer);

        Assert.Equal(PlaybackDecoderFfmpegDecodedOutputAdapterResult.Converted, result);
        Assert.Equal(8, consumer.Video.Stride);
        Assert.Equal(16, consumer.Video.Payload.Length);
        Assert.Equal(7, consumer.Video.Sequence);
        Assert.All(consumer.Video.Payload.ToArray(), value => Assert.Equal(0, value));
        Assert.False(input.IsDisposed);
        Assert.Contains(input.Planes[0].Buffer.ToArray(), value => value != 0);
    }

    [Theory]
    [InlineData((int)PlaybackDecoderFfmpegFrameFormat.AudioSigned16,
        (int)PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved, 8)]
    [InlineData((int)PlaybackDecoderFfmpegFrameFormat.AudioFloat32,
        (int)PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved, 16)]
    public void Adapt_PackedAudioReusesExistingOutputContract(
        int inputFormatValue,
        int outputFormatValue,
        int byteCount)
    {
        using var owners = new TestOwners();
        using var input = AudioFrame(
            (PlaybackDecoderFfmpegFrameFormat)inputFormatValue,
            [Plane(0, byteCount + 4, 1, byteCount)]);
        var consumer = new RecordingConsumer();

        Assert.Equal(
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.Converted,
            new PlaybackDecoderFfmpegDecodedOutputAdapter().Adapt(
                owners.Context,
                input,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None,
                consumer));
        Assert.Equal((PlaybackDecodedAudioSampleFormat)outputFormatValue, consumer.Audio.SampleFormat);
        Assert.Equal(byteCount, consumer.Audio.Payload.Length);
        Assert.Equal(2, consumer.Audio.ChannelCount);
        Assert.Equal(2, consumer.Audio.SampleCount);
        Assert.All(consumer.Audio.Payload.ToArray(), value => Assert.Equal(0, value));
    }

    [Theory]
    [InlineData((int)PlaybackDecoderFfmpegFrameFormat.VideoYuv420P)]
    [InlineData((int)PlaybackDecoderFfmpegFrameFormat.VideoNv12)]
    [InlineData((int)PlaybackDecoderFfmpegFrameFormat.AudioSigned16Planar)]
    [InlineData((int)PlaybackDecoderFfmpegFrameFormat.AudioFloat32Planar)]
    public void Adapt_ValidNonCanonicalLayoutReturnsConversionRequired(
        int formatValue)
    {
        using var owners = new TestOwners();
        using var input = CreateConversionRequiredFrame(
            (PlaybackDecoderFfmpegFrameFormat)formatValue);
        var consumer = new RecordingConsumer();

        Assert.Equal(
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.ConversionRequired,
            new PlaybackDecoderFfmpegDecodedOutputAdapter().Adapt(
                owners.Context,
                input,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None,
                consumer));
        Assert.Equal(0, consumer.CallCount);
    }

    [Fact]
    public void Adapt_UnknownTimesUseFixedExistingContractValues()
    {
        using var owners = new TestOwners();
        using var input = VideoFrame(
            PlaybackDecoderFfmpegFrameFormat.VideoBgra32,
            2,
            1,
            [Plane(0, 8, 1, 8)]);
        var consumer = new RecordingConsumer();

        Assert.Equal(
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.Converted,
            new PlaybackDecoderFfmpegDecodedOutputAdapter().Adapt(
                owners.Context,
                input,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None,
                consumer));
        Assert.Equal(TimeSpan.Zero, consumer.Video.Timestamp);
        Assert.Equal(TimeSpan.FromTicks(1), consumer.Video.Duration);
    }

    [Fact]
    public void Adapt_KnownTimesArePreserved()
    {
        using var owners = new TestOwners();
        using var input = VideoFrame(
            PlaybackDecoderFfmpegFrameFormat.VideoBgra32,
            2,
            1,
            [Plane(0, 8, 1, 8)],
            PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.FromSeconds(2)),
            PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.FromMilliseconds(40)));
        var consumer = new RecordingConsumer();

        Assert.Equal(
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.Converted,
            new PlaybackDecoderFfmpegDecodedOutputAdapter().Adapt(
                owners.Context,
                input,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None,
                consumer));
        Assert.Equal(TimeSpan.FromSeconds(2), consumer.Video.Timestamp);
        Assert.Equal(TimeSpan.FromMilliseconds(40), consumer.Video.Duration);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Adapt_InconsistentPlaneContractFailsClosed(int defect)
    {
        using var owners = new TestOwners();
        var planes = defect switch
        {
            0 => Array.Empty<PlaybackDecoderFfmpegManagedPlane>(),
            1 => [Plane(1, 8, 1, 8)],
            2 => [Plane(0, 8, 2, 8)],
            3 => [Plane(0, 8, 1, 7)],
            4 => [Plane(0, 7, 1, 8)],
            _ => throw new InvalidOperationException(),
        };
        using var input = VideoFrame(
            PlaybackDecoderFfmpegFrameFormat.VideoBgra32,
            2,
            1,
            planes);

        Assert.Equal(
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.InvalidFrame,
            new PlaybackDecoderFfmpegDecodedOutputAdapter().Adapt(
                owners.Context,
                input,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None,
                new RecordingConsumer()));
    }

    [Fact]
    public void Adapt_BufferLengthMismatchFailsClosed()
    {
        using var owners = new TestOwners();
        using var input = VideoFrame(
            PlaybackDecoderFfmpegFrameFormat.VideoBgra32,
            2,
            1,
            [Plane(0, 8, 1, 8, bufferLength: 7)]);

        Assert.Equal(
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.InvalidFrame,
            new PlaybackDecoderFfmpegDecodedOutputAdapter().Adapt(
                owners.Context,
                input,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None,
                new RecordingConsumer()));
    }

    [Fact]
    public void Adapt_OverflowingOrExcessiveMetadataFailsBeforeAllocation()
    {
        using var owners = new TestOwners();
        var metadata = new PlaybackDecoderFfmpegFrameMetadata(
            PlaybackDecoderFfmpegFrameMediaKind.Video,
            PlaybackDecoderFfmpegFrameFormat.VideoBgra32,
            int.MaxValue,
            int.MaxValue,
            0,
            0,
            0,
            default,
            default,
            default);
        using var input = new PlaybackDecoderFfmpegManagedFrame(metadata, []);
        var allocator = new RecordingAllocator();

        Assert.Equal(
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.InvalidFrame,
            new PlaybackDecoderFfmpegDecodedOutputAdapter(allocator).Adapt(
                owners.Context,
                input,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None,
                new RecordingConsumer()));
        Assert.Equal(0, allocator.CallCount);
    }

    [Fact]
    public void Adapt_TotalCapacityAboveLimitFailsBeforeAllocation()
    {
        using var owners = new TestOwners();
        using var input = VideoFrame(
            PlaybackDecoderFfmpegFrameFormat.VideoBgra32,
            PlaybackDecodedVideoFrame.MaximumDimension,
            PlaybackDecodedVideoFrame.MaximumDimension,
            [Plane(0, PlaybackDecodedVideoFrame.MaximumStride, 1, 1)]);
        var allocator = new RecordingAllocator();

        Assert.Equal(
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.InvalidFrame,
            new PlaybackDecoderFfmpegDecodedOutputAdapter(allocator).Adapt(
                owners.Context,
                input,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None,
                new RecordingConsumer()));
        Assert.Equal(0, allocator.CallCount);
    }

    [Fact]
    public void Adapt_UndefinedFormatFailsClosed()
    {
        using var owners = new TestOwners();
        using var input = VideoFrame(
            (PlaybackDecoderFfmpegFrameFormat)int.MaxValue,
            2,
            1,
            [Plane(0, 8, 1, 8)]);

        Assert.Equal(
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.InvalidFrame,
            new PlaybackDecoderFfmpegDecodedOutputAdapter().Adapt(
                owners.Context,
                input,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None,
                new RecordingConsumer()));
    }

    [Fact]
    public void Adapt_DisposedFrameIsFixedAndAvoidsAllocation()
    {
        using var owners = new TestOwners();
        var input = VideoFrame(
            PlaybackDecoderFfmpegFrameFormat.VideoBgra32,
            2,
            1,
            [Plane(0, 8, 1, 8)]);
        input.Dispose();
        var allocator = new RecordingAllocator();

        Assert.Equal(
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.DisposedFrame,
            new PlaybackDecoderFfmpegDecodedOutputAdapter(allocator).Adapt(
                owners.Context,
                input,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None,
                new RecordingConsumer()));
        Assert.Equal(0, allocator.CallCount);
    }

    [Fact]
    public void Adapt_AllocationFailureIsFixed()
    {
        using var owners = new TestOwners();
        using var input = VideoFrame(
            PlaybackDecoderFfmpegFrameFormat.VideoBgra32,
            2,
            1,
            [Plane(0, 8, 1, 8)]);

        Assert.Equal(
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.AllocationFailed,
            new PlaybackDecoderFfmpegDecodedOutputAdapter(new ShortAllocator()).Adapt(
                owners.Context,
                input,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None,
                new RecordingConsumer()));
    }

    [Fact]
    public void Adapt_ConsumerRejectionClearsTemporaryPayloadAndPreservesContext()
    {
        using var owners = new TestOwners();
        using var input = VideoFrame(
            PlaybackDecoderFfmpegFrameFormat.VideoBgra32,
            2,
            1,
            [Plane(0, 8, 1, 8)]);
        var consumer = new RecordingConsumer(accept: false);

        Assert.Equal(
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.ConsumerRejected,
            new PlaybackDecoderFfmpegDecodedOutputAdapter().Adapt(
                owners.Context,
                input,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None,
                consumer));
        Assert.False(owners.Context.IsDisposed);
        Assert.All(consumer.Video.Payload.ToArray(), value => Assert.Equal(0, value));
    }

    [Fact]
    public void Adapt_OutputContractFailureClearsTemporaryPayload()
    {
        using var owners = new TestOwners();
        using var input = VideoFrame(
            PlaybackDecoderFfmpegFrameFormat.VideoBgra32,
            2,
            1,
            [Plane(0, 8, 1, 8)]);
        var allocator = new TrackingAllocator();

        Assert.Equal(
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.InvalidFrame,
            new PlaybackDecoderFfmpegDecodedOutputAdapter(allocator).Adapt(
                owners.Context,
                input,
                sequence: -1,
                PlaybackDecodedFrameDiscontinuityKind.None,
                new RecordingConsumer()));
        Assert.All(allocator.LastBuffer!, value => Assert.Equal(0, value));
    }

    [Fact]
    public void Adapt_DisposedContextIsRejectedWithoutChangingInputOwnership()
    {
        using var owners = new TestOwners();
        using var input = VideoFrame(
            PlaybackDecoderFfmpegFrameFormat.VideoBgra32,
            2,
            1,
            [Plane(0, 8, 1, 8)]);
        owners.Context.Dispose();

        Assert.Equal(
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.ContextUnavailable,
            new PlaybackDecoderFfmpegDecodedOutputAdapter().Adapt(
                owners.Context,
                input,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None,
                new RecordingConsumer()));
        Assert.False(input.IsDisposed);
    }

    private static PlaybackDecoderFfmpegManagedFrame CreateConversionRequiredFrame(
        PlaybackDecoderFfmpegFrameFormat format) => format switch
        {
            PlaybackDecoderFfmpegFrameFormat.VideoYuv420P => VideoFrame(
                format,
                4,
                2,
                [Plane(0, 4, 2, 4), Plane(1, 2, 1, 2), Plane(2, 2, 1, 2)]),
            PlaybackDecoderFfmpegFrameFormat.VideoNv12 => VideoFrame(
                format,
                5,
                2,
                [Plane(0, 5, 2, 5), Plane(1, 6, 1, 6)]),
            PlaybackDecoderFfmpegFrameFormat.AudioSigned16Planar => AudioFrame(
                format,
                [Plane(0, 4, 1, 4), Plane(1, 4, 1, 4)]),
            PlaybackDecoderFfmpegFrameFormat.AudioFloat32Planar => AudioFrame(
                format,
                [Plane(0, 8, 1, 8), Plane(1, 8, 1, 8)]),
            _ => throw new InvalidOperationException(),
        };

    private static PlaybackDecoderFfmpegManagedFrame VideoFrame(
        PlaybackDecoderFfmpegFrameFormat format,
        int width,
        int height,
        PlaybackDecoderFfmpegManagedPlane[] planes,
        PlaybackDecoderFfmpegFrameTime timestamp = default,
        PlaybackDecoderFfmpegFrameTime duration = default) => new(
            new PlaybackDecoderFfmpegFrameMetadata(
                PlaybackDecoderFfmpegFrameMediaKind.Video,
                format,
                width,
                height,
                0,
                0,
                0,
                timestamp,
                timestamp,
                duration),
            planes);

    private static PlaybackDecoderFfmpegManagedFrame AudioFrame(
        PlaybackDecoderFfmpegFrameFormat format,
        PlaybackDecoderFfmpegManagedPlane[] planes) => new(
            new PlaybackDecoderFfmpegFrameMetadata(
                PlaybackDecoderFfmpegFrameMediaKind.Audio,
                format,
                0,
                0,
                48_000,
                2,
                2,
                default,
                default,
                default),
            planes);

    private static PlaybackDecoderFfmpegManagedPlane Plane(
        int index,
        int stride,
        int rows,
        int logicalRowBytes,
        int? bufferLength = null)
    {
        var buffer = Enumerable.Range(1, bufferLength ?? checked(rows * logicalRowBytes))
            .Select(value => (byte)value)
            .ToArray();
        return new(index, stride, rows, logicalRowBytes, buffer);
    }

    private sealed class RecordingConsumer(bool accept = true) :
        IPlaybackDecoderFfmpegDecodedOutputConsumer
    {
        public int CallCount { get; private set; }
        public PlaybackDecodedAudioFrame Audio { get; private set; }
        public PlaybackDecodedVideoFrame Video { get; private set; }

        public bool ConsumeAudio(PlaybackDecodedAudioFrame frame)
        {
            CallCount++;
            Audio = frame;
            return accept;
        }

        public bool ConsumeVideo(PlaybackDecodedVideoFrame frame)
        {
            CallCount++;
            Video = frame;
            return accept;
        }
    }

    private sealed class RecordingAllocator : IPlaybackDecoderManagedBufferAllocator
    {
        public int CallCount { get; private set; }

        public byte[] Allocate(int length)
        {
            CallCount++;
            return new byte[length];
        }
    }

    private sealed class ShortAllocator : IPlaybackDecoderManagedBufferAllocator
    {
        public byte[] Allocate(int length) => new byte[length - 1];
    }

    private sealed class TrackingAllocator : IPlaybackDecoderManagedBufferAllocator
    {
        public byte[]? LastBuffer { get; private set; }

        public byte[] Allocate(int length)
        {
            LastBuffer = new byte[length];
            return LastBuffer;
        }
    }

    private sealed class TestOwners : IDisposable
    {
        private readonly string _directory;
        private readonly VerifiedPlaybackDecoderRuntimeLease _runtime;

        public TestOwners()
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
            var symbols = RequiredFrameSymbols
                .Select((name, index) => (name, pointer: (nint)(index + 1)))
                .ToDictionary(item => item.name, item => item.pointer, StringComparer.Ordinal);
            var gate = new PlaybackDecoderFfmpegAbiSymbolGateLease(
                _runtime,
                new FakeLoader(),
                [(nint)100],
                symbols);
            Context = new PlaybackDecoderFfmpegContextLease(
                _runtime,
                gate,
                new FakeContextNativeApi(),
                PlaybackDecoderFfmpegCodecId.H264,
                (nint)200,
                (nint)300);
            Context.MarkParametersTransferred();
            Context.MarkOpened();
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
