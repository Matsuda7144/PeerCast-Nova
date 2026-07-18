namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDecoderFfmpegManagedFormatConversionTests
{
    private static readonly string[] RequiredFrameSymbols =
        ["av_frame_alloc", "av_frame_free", "avcodec_receive_frame"];

    [Theory]
    [InlineData((int)PlaybackDecoderFfmpegFrameFormat.VideoYuv420P)]
    [InlineData((int)PlaybackDecoderFfmpegFrameFormat.VideoNv12)]
    public void Convert_OddVideoDimensionsProduceBoundedBgra(int formatValue)
    {
        using var owners = new TestOwners();
        using var input = OddVideo((PlaybackDecoderFfmpegFrameFormat)formatValue);
        var consumer = new RecordingConsumer();

        var result = Create().Convert(
            owners.Context,
            input,
            3,
            PlaybackDecodedFrameDiscontinuityKind.None,
            consumer);

        Assert.Equal(PlaybackDecoderFfmpegManagedFormatConversionResult.Converted, result);
        Assert.Equal(3, consumer.Video.Width);
        Assert.Equal(3, consumer.Video.Height);
        Assert.Equal(12, consumer.Video.Stride);
        Assert.Equal(36, consumer.Video.Payload.Length);
        Assert.All(
            consumer.Snapshot.Where((_, index) => index % 4 == 3),
            alpha => Assert.Equal(255, alpha));
        Assert.All(consumer.Video.Payload.ToArray(), value => Assert.Equal(0, value));
        Assert.False(input.IsDisposed);
    }

    [Theory]
    [InlineData(16, 128, 128, 0, 0, 0)]
    [InlineData(235, 128, 128, 255, 255, 255)]
    [InlineData(81, 90, 240, 0, 0, 255)]
    public void Convert_Yuv420UsesFixedBt601LimitedRange(
        byte y,
        byte u,
        byte v,
        byte expectedB,
        byte expectedG,
        byte expectedR)
    {
        using var owners = new TestOwners();
        using var input = Video(
            PlaybackDecoderFfmpegFrameFormat.VideoYuv420P,
            1,
            1,
            [Plane(0, 1, 1, 1, [y]), Plane(1, 1, 1, 1, [u]), Plane(2, 1, 1, 1, [v])]);
        var consumer = new RecordingConsumer();

        Assert.Equal(
            PlaybackDecoderFfmpegManagedFormatConversionResult.Converted,
            Create().Convert(
                owners.Context,
                input,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None,
                consumer));
        Assert.Equal([expectedB, expectedG, expectedR, (byte)255], consumer.Snapshot);
    }

    [Fact]
    public void Convert_S16PlanarInterleavesBySampleThenChannel()
    {
        using var owners = new TestOwners();
        using var input = Audio(
            PlaybackDecoderFfmpegFrameFormat.AudioSigned16Planar,
            sampleCount: 2,
            [
                Plane(0, 4, 1, 4, [1, 2, 3, 4]),
                Plane(1, 4, 1, 4, [5, 6, 7, 8]),
            ]);
        var consumer = new RecordingConsumer();

        Assert.Equal(
            PlaybackDecoderFfmpegManagedFormatConversionResult.Converted,
            Create().Convert(
                owners.Context,
                input,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None,
                consumer));
        Assert.Equal([1, 2, 5, 6, 3, 4, 7, 8], consumer.Snapshot);
        Assert.Equal(
            PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
            consumer.Audio.SampleFormat);
    }

    [Fact]
    public void Convert_F32PlanarInterleavesRawSampleBytes()
    {
        using var owners = new TestOwners();
        using var input = Audio(
            PlaybackDecoderFfmpegFrameFormat.AudioFloat32Planar,
            sampleCount: 2,
            [
                Plane(0, 8, 1, 8, [1, 2, 3, 4, 5, 6, 7, 8]),
                Plane(1, 8, 1, 8, [9, 10, 11, 12, 13, 14, 15, 16]),
            ]);
        var consumer = new RecordingConsumer();

        Assert.Equal(
            PlaybackDecoderFfmpegManagedFormatConversionResult.Converted,
            Create().Convert(
                owners.Context,
                input,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None,
                consumer));
        Assert.Equal(
            [1, 2, 3, 4, 9, 10, 11, 12, 5, 6, 7, 8, 13, 14, 15, 16],
            consumer.Snapshot);
        Assert.Equal(
            PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved,
            consumer.Audio.SampleFormat);
    }

    [Fact]
    public void Convert_UnknownTimesMatchDecodedOutputAdapter()
    {
        using var owners = new TestOwners();
        using var input = OddVideo(PlaybackDecoderFfmpegFrameFormat.VideoNv12);
        var consumer = new RecordingConsumer();

        Assert.Equal(
            PlaybackDecoderFfmpegManagedFormatConversionResult.Converted,
            Create().Convert(
                owners.Context,
                input,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None,
                consumer));
        Assert.Equal(TimeSpan.Zero, consumer.Video.Timestamp);
        Assert.Equal(TimeSpan.FromTicks(1), consumer.Video.Duration);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Convert_InconsistentPlaneContractFailsClosed(int defect)
    {
        using var owners = new TestOwners();
        var planes = defect switch
        {
            0 => Array.Empty<PlaybackDecoderFfmpegManagedPlane>(),
            1 => [Plane(1, 1, 1, 1, [16]), Plane(1, 2, 1, 2, [128, 128])],
            2 => [Plane(0, 1, 2, 1, [16, 16]), Plane(1, 2, 1, 2, [128, 128])],
            3 => [Plane(0, 1, 1, 2, [16, 16]), Plane(1, 2, 1, 2, [128, 128])],
            4 => [Plane(0, 1, 1, 1, []), Plane(1, 2, 1, 2, [128, 128])],
            _ => throw new InvalidOperationException(),
        };
        using var input = Video(
            PlaybackDecoderFfmpegFrameFormat.VideoNv12,
            1,
            1,
            planes);

        Assert.Equal(
            PlaybackDecoderFfmpegManagedFormatConversionResult.InvalidFrame,
            Create().Convert(
                owners.Context,
                input,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None,
                new RecordingConsumer()));
    }

    [Fact]
    public void Convert_NonConversionFormatIsFixedUnsupported()
    {
        using var owners = new TestOwners();
        using var input = Video(
            PlaybackDecoderFfmpegFrameFormat.VideoBgra32,
            1,
            1,
            [Plane(0, 4, 1, 4, [0, 0, 0, 255])]);

        Assert.Equal(
            PlaybackDecoderFfmpegManagedFormatConversionResult.UnsupportedFormat,
            Create().Convert(
                owners.Context,
                input,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None,
                new RecordingConsumer()));
    }

    [Fact]
    public void Convert_ExcessiveOutputCapacityFailsBeforeAllocation()
    {
        using var owners = new TestOwners();
        using var input = Video(
            PlaybackDecoderFfmpegFrameFormat.VideoNv12,
            PlaybackDecodedVideoFrame.MaximumDimension,
            PlaybackDecodedVideoFrame.MaximumDimension,
            []);
        var allocator = new RecordingAllocator();

        Assert.Equal(
            PlaybackDecoderFfmpegManagedFormatConversionResult.InvalidFrame,
            Create(allocator: allocator).Convert(
                owners.Context,
                input,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None,
                new RecordingConsumer()));
        Assert.Equal(0, allocator.CallCount);
    }

    [Fact]
    public void Convert_OverflowingTargetMetadataFailsBeforeAllocation()
    {
        using var owners = new TestOwners();
        using var input = Video(
            PlaybackDecoderFfmpegFrameFormat.VideoNv12,
            int.MaxValue,
            int.MaxValue,
            []);
        var allocator = new RecordingAllocator();

        Assert.Equal(
            PlaybackDecoderFfmpegManagedFormatConversionResult.InvalidFrame,
            Create(allocator: allocator).Convert(
                owners.Context,
                input,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None,
                new RecordingConsumer()));
        Assert.Equal(0, allocator.CallCount);
    }

    [Fact]
    public void Convert_DisposedFrameIsRejectedBeforeAllocation()
    {
        using var owners = new TestOwners();
        var input = OddVideo(PlaybackDecoderFfmpegFrameFormat.VideoYuv420P);
        input.Dispose();
        var allocator = new RecordingAllocator();

        Assert.Equal(
            PlaybackDecoderFfmpegManagedFormatConversionResult.DisposedFrame,
            Create(allocator: allocator).Convert(
                owners.Context,
                input,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None,
                new RecordingConsumer()));
        Assert.Equal(0, allocator.CallCount);
    }

    [Fact]
    public void Convert_AllocationFailureIsFixedAndClearsReturnedBuffer()
    {
        using var owners = new TestOwners();
        using var input = OddVideo(PlaybackDecoderFfmpegFrameFormat.VideoNv12);
        var allocator = new ShortAllocator();

        Assert.Equal(
            PlaybackDecoderFfmpegManagedFormatConversionResult.AllocationFailed,
            Create(allocator: allocator).Convert(
                owners.Context,
                input,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None,
                new RecordingConsumer()));
        Assert.All(allocator.Buffer!, value => Assert.Equal(0, value));
    }

    [Fact]
    public void Convert_WorkerFailureIsFixedAndClearsTemporaryBuffer()
    {
        using var owners = new TestOwners();
        using var input = OddVideo(PlaybackDecoderFfmpegFrameFormat.VideoNv12);
        var allocator = new RecordingAllocator();

        Assert.Equal(
            PlaybackDecoderFfmpegManagedFormatConversionResult.ConversionFailed,
            Create(allocator, new ThrowingWorker()).Convert(
                owners.Context,
                input,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None,
                new RecordingConsumer()));
        Assert.All(allocator.Buffer!, value => Assert.Equal(0, value));
    }

    [Fact]
    public void Convert_ConsumerRejectionClearsPayloadAndPreservesContext()
    {
        using var owners = new TestOwners();
        using var input = OddVideo(PlaybackDecoderFfmpegFrameFormat.VideoYuv420P);
        var consumer = new RecordingConsumer(accept: false);

        Assert.Equal(
            PlaybackDecoderFfmpegManagedFormatConversionResult.ConsumerRejected,
            Create().Convert(
                owners.Context,
                input,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None,
                consumer));
        Assert.False(owners.Context.IsDisposed);
        Assert.All(consumer.Video.Payload.ToArray(), value => Assert.Equal(0, value));
        Assert.False(input.IsDisposed);
    }

    [Fact]
    public void Convert_ConsumerExceptionStillClearsTemporaryPayload()
    {
        using var owners = new TestOwners();
        using var input = OddVideo(PlaybackDecoderFfmpegFrameFormat.VideoNv12);
        var consumer = new ThrowingConsumer();

        Assert.Throws<NotSupportedException>(() => Create().Convert(
            owners.Context,
            input,
            0,
            PlaybackDecodedFrameDiscontinuityKind.None,
            consumer));
        Assert.All(consumer.Payload.ToArray(), value => Assert.Equal(0, value));
    }

    private static PlaybackDecoderFfmpegManagedFormatConversionBoundary Create(
        IPlaybackDecoderManagedBufferAllocator? allocator = null,
        IPlaybackDecoderManagedFormatConversionWorker? worker = null) => new(
            allocator ?? new PlaybackDecoderManagedBufferAllocator(),
            worker ?? new PlaybackDecoderManagedFormatConversionWorker());

    private static PlaybackDecoderFfmpegManagedFrame OddVideo(
        PlaybackDecoderFfmpegFrameFormat format) => format switch
        {
            PlaybackDecoderFfmpegFrameFormat.VideoYuv420P => Video(
                format,
                3,
                3,
                [
                    Plane(0, 3, 3, 3, Enumerable.Repeat((byte)16, 9).ToArray()),
                    Plane(1, 2, 2, 2, Enumerable.Repeat((byte)128, 4).ToArray()),
                    Plane(2, 2, 2, 2, Enumerable.Repeat((byte)128, 4).ToArray()),
                ]),
            PlaybackDecoderFfmpegFrameFormat.VideoNv12 => Video(
                format,
                3,
                3,
                [
                    Plane(0, 3, 3, 3, Enumerable.Repeat((byte)16, 9).ToArray()),
                    Plane(1, 4, 2, 4, Enumerable.Repeat((byte)128, 8).ToArray()),
                ]),
            _ => throw new InvalidOperationException(),
        };

    private static PlaybackDecoderFfmpegManagedFrame Video(
        PlaybackDecoderFfmpegFrameFormat format,
        int width,
        int height,
        PlaybackDecoderFfmpegManagedPlane[] planes) => new(
            new PlaybackDecoderFfmpegFrameMetadata(
                PlaybackDecoderFfmpegFrameMediaKind.Video,
                format,
                width,
                height,
                0,
                0,
                0,
                default,
                default,
                default),
            planes);

    private static PlaybackDecoderFfmpegManagedFrame Audio(
        PlaybackDecoderFfmpegFrameFormat format,
        int sampleCount,
        PlaybackDecoderFfmpegManagedPlane[] planes) => new(
            new PlaybackDecoderFfmpegFrameMetadata(
                PlaybackDecoderFfmpegFrameMediaKind.Audio,
                format,
                0,
                0,
                48_000,
                2,
                sampleCount,
                default,
                default,
                default),
            planes);

    private static PlaybackDecoderFfmpegManagedPlane Plane(
        int index,
        int stride,
        int rows,
        int logicalRowBytes,
        byte[] buffer) => new(index, stride, rows, logicalRowBytes, buffer);

    private sealed class RecordingConsumer(bool accept = true) :
        IPlaybackDecoderFfmpegDecodedOutputConsumer
    {
        public PlaybackDecodedAudioFrame Audio { get; private set; }
        public PlaybackDecodedVideoFrame Video { get; private set; }
        public byte[] Snapshot { get; private set; } = [];

        public bool ConsumeAudio(PlaybackDecodedAudioFrame frame)
        {
            Audio = frame;
            Snapshot = frame.Payload.ToArray();
            return accept;
        }

        public bool ConsumeVideo(PlaybackDecodedVideoFrame frame)
        {
            Video = frame;
            Snapshot = frame.Payload.ToArray();
            return accept;
        }
    }

    private sealed class ThrowingConsumer : IPlaybackDecoderFfmpegDecodedOutputConsumer
    {
        public ReadOnlyMemory<byte> Payload { get; private set; }

        public bool ConsumeAudio(PlaybackDecodedAudioFrame frame) => throw new NotSupportedException();

        public bool ConsumeVideo(PlaybackDecodedVideoFrame frame)
        {
            Payload = frame.Payload;
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingAllocator : IPlaybackDecoderManagedBufferAllocator
    {
        public int CallCount { get; private set; }
        public byte[]? Buffer { get; private set; }

        public byte[] Allocate(int length)
        {
            CallCount++;
            Buffer = new byte[length];
            return Buffer;
        }
    }

    private sealed class ShortAllocator : IPlaybackDecoderManagedBufferAllocator
    {
        public byte[]? Buffer { get; private set; }

        public byte[] Allocate(int length)
        {
            Buffer = Enumerable.Repeat((byte)123, length - 1).ToArray();
            return Buffer;
        }
    }

    private sealed class ThrowingWorker : IPlaybackDecoderManagedFormatConversionWorker
    {
        public void Convert(
            PlaybackDecoderFfmpegManagedFrame frame,
            Span<byte> destination)
        {
            destination.Fill(123);
            throw new InvalidOperationException("sensitive");
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
