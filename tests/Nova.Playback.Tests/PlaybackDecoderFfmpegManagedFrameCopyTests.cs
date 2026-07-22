using System.Runtime.InteropServices;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDecoderFfmpegManagedFrameCopyTests
{
    private static readonly string[] RequiredFrameSymbols =
        ["av_frame_alloc", "av_frame_free", "avcodec_receive_frame"];

    [Fact]
    public void ReceiveAndCopy_Yuv420CopiesLogicalRowsWithoutPadding()
    {
        using var owners = new TestOwners();
        var source = new FakeSource(
            Video(PlaybackDecoderFfmpegFrameFormat.VideoYuv420P, 4, 2),
            [
                new(0, PlaybackDecoderFfmpegFrameDataKind.VideoPlane, 6, 2, 4),
                new(1, PlaybackDecoderFfmpegFrameDataKind.VideoPlane, 4, 1, 2),
                new(2, PlaybackDecoderFfmpegFrameDataKind.VideoPlane, 4, 1, 2),
            ]);
        var consumer = new RecordingConsumer();

        var result = Create(source).ReceiveAndCopy(owners.Context, consumer);

        Assert.Equal(PlaybackDecoderFfmpegManagedFrameCopyResult.Copied, result);
        var frame = Assert.IsType<PlaybackDecoderFfmpegManagedFrame>(consumer.Frame);
        Assert.Equal(3, frame.Planes.Count);
        Assert.Equal((0, 6, 2, 4), PlaneShape(frame.Planes[0]));
        Assert.Equal([1, 2, 3, 4, 7, 8, 9, 10], frame.Planes[0].Buffer.ToArray());
        Assert.DoesNotContain((byte)5, frame.Planes[0].Buffer.ToArray());
        Assert.Equal(PlaybackDecoderFfmpegFrameFormat.VideoYuv420P, frame.Metadata.Format);
    }

    [Fact]
    public void ReceiveAndCopy_OddWidthNv12RetainsRoundedLogicalRowBytes()
    {
        using var owners = new TestOwners();
        var source = new FakeSource(
            Video(PlaybackDecoderFfmpegFrameFormat.VideoNv12, 5, 2),
            [
                new(0, PlaybackDecoderFfmpegFrameDataKind.VideoPlane, 7, 2, 5),
                new(1, PlaybackDecoderFfmpegFrameDataKind.VideoPlane, 8, 1, 6),
            ]);
        var consumer = new RecordingConsumer();

        Assert.Equal(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            Create(source).ReceiveAndCopy(owners.Context, consumer));
        Assert.Equal(6, consumer.Frame!.Planes[1].LogicalRowBytes);
        Assert.Equal(6, consumer.Frame.Planes[1].Buffer.Length);
    }

    [Theory]
    [InlineData((int)PlaybackDecoderFfmpegFrameFormat.AudioSigned16, 1, 16)]
    [InlineData((int)PlaybackDecoderFfmpegFrameFormat.AudioFloat32, 1, 32)]
    [InlineData((int)PlaybackDecoderFfmpegFrameFormat.AudioSigned16Planar, 2, 8)]
    [InlineData((int)PlaybackDecoderFfmpegFrameFormat.AudioFloat32Planar, 2, 16)]
    public void ReceiveAndCopy_SupportedAudioCopiesOnlyLogicalSampleBytes(
        int formatValue,
        int planeCount,
        int logicalBytes)
    {
        using var owners = new TestOwners();
        var format = (PlaybackDecoderFfmpegFrameFormat)formatValue;
        var specs = Enumerable.Range(0, planeCount)
            .Select(index => new PlaneSpec(
                index,
                PlaybackDecoderFfmpegFrameDataKind.AudioSamples,
                logicalBytes + 4,
                1,
                logicalBytes,
                ReportedLength: logicalBytes))
            .ToArray();
        var source = new FakeSource(Audio(format), specs);
        var consumer = new RecordingConsumer();

        Assert.Equal(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            Create(source).ReceiveAndCopy(owners.Context, consumer));
        Assert.Equal(planeCount, consumer.Frame!.Planes.Count);
        Assert.All(consumer.Frame.Planes, plane => Assert.Equal(logicalBytes, plane.Buffer.Length));
    }

    [Fact]
    public void ReceiveAndCopy_ObservedPaddedPlanarFloatFrameCopiesOrderedLogicalPlanesOnly()
    {
        using var owners = new TestOwners();
        var source = new FakeSource(
            new PlaybackDecoderFfmpegFrameMetadata(
                PlaybackDecoderFfmpegFrameMediaKind.Audio,
                PlaybackDecoderFfmpegFrameFormat.AudioFloat32Planar,
                0,
                0,
                48_000,
                2,
                1_024,
                default,
                default,
                default),
            [
                new(
                    0,
                    PlaybackDecoderFfmpegFrameDataKind.AudioSamples,
                    8_192,
                    1,
                    4_096,
                    ReportedLength: 4_096,
                    LogicalFillByte: 0x11,
                    PaddingByte: 0xEE),
                new(
                    1,
                    PlaybackDecoderFfmpegFrameDataKind.AudioSamples,
                    8_192,
                    1,
                    4_096,
                    ReportedLength: 4_096,
                    LogicalFillByte: 0x22,
                    PaddingByte: 0xEE),
            ]);
        var consumer = new RecordingConsumer();

        var result = Create(source).ReceiveAndCopy(owners.Context, consumer);

        Assert.Equal(PlaybackDecoderFfmpegManagedFrameCopyResult.Copied, result);
        var frame = Assert.IsType<PlaybackDecoderFfmpegManagedFrame>(consumer.Frame);
        Assert.Equal(2, frame.Planes.Count);
        Assert.Equal(4_096, frame.Planes[0].Buffer.Length);
        Assert.Equal(4_096, frame.Planes[1].Buffer.Length);
        Assert.All(frame.Planes[0].Buffer.ToArray(), value => Assert.Equal(0x11, value));
        Assert.All(frame.Planes[1].Buffer.ToArray(), value => Assert.Equal(0x22, value));
        Assert.DoesNotContain((byte)0xEE, frame.Planes[0].Buffer.ToArray());
        Assert.DoesNotContain((byte)0xEE, frame.Planes[1].Buffer.ToArray());
    }

    [Fact]
    public void ReceiveAndCopy_InsufficientViewFailsClosed()
    {
        using var owners = new TestOwners();
        var source = new FakeSource(
            Video(PlaybackDecoderFfmpegFrameFormat.VideoBgra32, 2, 2),
            [new(0, PlaybackDecoderFfmpegFrameDataKind.VideoPlane, 8, 2, 8, 8)]);

        Assert.Equal(
            PlaybackDecoderFfmpegManagedFrameCopyResult.InsufficientData,
            Create(source).ReceiveAndCopy(owners.Context, new RecordingConsumer()));
    }

    [Fact]
    public void ReceiveAndCopy_PaddedAudioFailureAllowsCompleteContextAndRuntimeCleanup()
    {
        var owners = new TestOwners();
        var source = new FakeSource(
            Audio(PlaybackDecoderFfmpegFrameFormat.AudioFloat32Planar),
            [
                new(
                    0,
                    PlaybackDecoderFfmpegFrameDataKind.AudioSamples,
                    16,
                    1,
                    16,
                    ReportedLength: 15),
            ]);

        Assert.Equal(
            PlaybackDecoderFfmpegManagedFrameCopyResult.InsufficientData,
            Create(source).ReceiveAndCopy(owners.Context, new RecordingConsumer()));
        Assert.False(owners.Context.IsDisposed);

        owners.Dispose();

        Assert.True(owners.Context.IsDisposed);
        Assert.True(owners.RuntimeIsDisposed);
    }

    [Fact]
    public void ReceiveAndCopy_OverflowingMetadataFailsBeforeAllocation()
    {
        using var owners = new TestOwners();
        var metadata = Video(
            PlaybackDecoderFfmpegFrameFormat.VideoBgra32,
            int.MaxValue,
            int.MaxValue);
        var source = new FakeSource(
            metadata,
            [new(0, PlaybackDecoderFfmpegFrameDataKind.VideoPlane, 1, 1, 1)]);
        var allocator = new RecordingAllocator();

        Assert.Equal(
            PlaybackDecoderFfmpegManagedFrameCopyResult.InvalidMetadata,
            Create(source, allocator).ReceiveAndCopy(owners.Context, new RecordingConsumer()));
        Assert.Equal(0, allocator.CallCount);
    }

    [Fact]
    public void ReceiveAndCopy_TotalCapacityAboveLimitFailsBeforeAllocation()
    {
        using var owners = new TestOwners();
        var source = new FakeSource(
            Video(
                PlaybackDecoderFfmpegFrameFormat.VideoBgra32,
                PlaybackDecodedVideoFrame.MaximumDimension,
                PlaybackDecodedVideoFrame.MaximumDimension),
            [new(0, PlaybackDecoderFfmpegFrameDataKind.VideoPlane, 1, 1, 1)]);
        var allocator = new RecordingAllocator();

        Assert.Equal(
            PlaybackDecoderFfmpegManagedFrameCopyResult.InvalidMetadata,
            Create(source, allocator).ReceiveAndCopy(owners.Context, new RecordingConsumer()));
        Assert.Equal(0, allocator.CallCount);
    }

    [Fact]
    public void ReceiveAndCopy_IncompletePlaneSequenceFailsClosed()
    {
        using var owners = new TestOwners();
        var source = new FakeSource(
            Video(PlaybackDecoderFfmpegFrameFormat.VideoYuv420P, 4, 2),
            [new(0, PlaybackDecoderFfmpegFrameDataKind.VideoPlane, 4, 2, 4)]);

        Assert.Equal(
            PlaybackDecoderFfmpegManagedFrameCopyResult.InsufficientData,
            Create(source).ReceiveAndCopy(owners.Context, new RecordingConsumer()));
    }

    [Fact]
    public void ReceiveAndCopy_AllocationFailureUsesFixedResult()
    {
        using var owners = new TestOwners();
        var source = new FakeSource(
            Video(PlaybackDecoderFfmpegFrameFormat.VideoBgra32, 2, 2),
            [new(0, PlaybackDecoderFfmpegFrameDataKind.VideoPlane, 8, 2, 8)]);

        var result = Create(source, new ShortAllocator())
            .ReceiveAndCopy(owners.Context, new RecordingConsumer());

        Assert.Equal(PlaybackDecoderFfmpegManagedFrameCopyResult.AllocationFailed, result);
        Assert.DoesNotContain("native", result.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReceiveAndCopy_ConsumerRejectionClearsOwnedBufferAndPreservesContext()
    {
        using var owners = new TestOwners();
        var source = new FakeSource(
            Video(PlaybackDecoderFfmpegFrameFormat.VideoBgra32, 2, 1),
            [new(0, PlaybackDecoderFfmpegFrameDataKind.VideoPlane, 8, 1, 8)]);
        var consumer = new RecordingConsumer(accept: false);

        Assert.Equal(
            PlaybackDecoderFfmpegManagedFrameCopyResult.ConsumerRejected,
            Create(source).ReceiveAndCopy(owners.Context, consumer));
        Assert.False(owners.Context.IsDisposed);
        Assert.True(consumer.Frame!.IsDisposed);
        Assert.All(consumer.Frame.Planes[0].Buffer.ToArray(), value => Assert.Equal(0, value));
    }

    [Fact]
    public void ReceiveAndCopy_UnexpectedNativeFailureDisposesContext()
    {
        using var owners = new TestOwners();
        var source = new FakeSource(
            Video(PlaybackDecoderFfmpegFrameFormat.VideoBgra32, 2, 1),
            [new(0, PlaybackDecoderFfmpegFrameDataKind.VideoPlane, 8, 1, 8)],
            disposeContextAndFail: true);

        Assert.Equal(
            PlaybackDecoderFfmpegManagedFrameCopyResult.NativeFailure,
            Create(source).ReceiveAndCopy(owners.Context, new RecordingConsumer()));
        Assert.True(owners.Context.IsDisposed);
    }

    [Fact]
    public void ReceiveAndCopy_UnsupportedSourceDoesNotExposeOrCopyNativeData()
    {
        using var owners = new TestOwners();
        var source = new FakeSource(
            Video(PlaybackDecoderFfmpegFrameFormat.VideoBgra32, 2, 1),
            [],
            fixedResult: PlaybackDecoderFfmpegFrameDataResult.Unsupported);

        Assert.Equal(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Unsupported,
            Create(source).ReceiveAndCopy(owners.Context, new RecordingConsumer()));
        Assert.Equal(0, source.NativeViewCount);
    }

    [Fact]
    public void ManagedFrame_DisposeIsIdempotentAndClearsCopiedBytes()
    {
        using var owners = new TestOwners();
        var source = new FakeSource(
            Video(PlaybackDecoderFfmpegFrameFormat.VideoBgra32, 2, 1),
            [new(0, PlaybackDecoderFfmpegFrameDataKind.VideoPlane, 8, 1, 8)]);
        var consumer = new RecordingConsumer();
        Assert.Equal(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            Create(source).ReceiveAndCopy(owners.Context, consumer));
        var frame = consumer.Frame!;

        frame.Dispose();
        frame.Dispose();

        Assert.True(frame.IsDisposed);
        Assert.All(frame.Planes[0].Buffer.ToArray(), value => Assert.Equal(0, value));
    }

    private static PlaybackDecoderFfmpegManagedFrameCopyBoundary Create(
        IPlaybackDecoderFfmpegFrameDataSource source,
        IPlaybackDecoderManagedBufferAllocator? allocator = null) =>
        new(source, allocator ?? new PlaybackDecoderManagedBufferAllocator());

    private static PlaybackDecoderFfmpegFrameMetadata Video(
        PlaybackDecoderFfmpegFrameFormat format,
        int width,
        int height) => new(
            PlaybackDecoderFfmpegFrameMediaKind.Video,
            format,
            width,
            height,
            0,
            0,
            0,
            default,
            default,
            default);

    private static PlaybackDecoderFfmpegFrameMetadata Audio(
        PlaybackDecoderFfmpegFrameFormat format) => new(
            PlaybackDecoderFfmpegFrameMediaKind.Audio,
            format,
            0,
            0,
            48_000,
            2,
            4,
            default,
            default,
            default);

    private static (int, int, int, int) PlaneShape(
        PlaybackDecoderFfmpegManagedPlane plane) =>
        (plane.Index, plane.Stride, plane.Rows, plane.LogicalRowBytes);

    private sealed class RecordingConsumer(bool accept = true) :
        IPlaybackDecoderFfmpegManagedFrameConsumer
    {
        public PlaybackDecoderFfmpegManagedFrame? Frame { get; private set; }

        public bool Consume(PlaybackDecoderFfmpegManagedFrame frame)
        {
            Frame = frame;
            return accept;
        }
    }

    private sealed record PlaneSpec(
        int Index,
        PlaybackDecoderFfmpegFrameDataKind Kind,
        int Stride,
        int Rows,
        int LogicalBytes,
        int? ReportedLength = null,
        byte? LogicalFillByte = null,
        byte? PaddingByte = null);

    private sealed class FakeSource(
        PlaybackDecoderFfmpegFrameMetadata metadata,
        PlaneSpec[] planes,
        bool disposeContextAndFail = false,
        PlaybackDecoderFfmpegFrameDataResult fixedResult =
            PlaybackDecoderFfmpegFrameDataResult.Processed) :
        IPlaybackDecoderFfmpegFrameDataSource
    {
        public int NativeViewCount { get; private set; }

        public PlaybackDecoderFfmpegFrameDataResult ReceiveAndConsume(
            PlaybackDecoderFfmpegContextLease contextLease,
            IPlaybackDecoderFfmpegFrameDataConsumer consumer)
        {
            if (disposeContextAndFail)
            {
                contextLease.Dispose();
                return PlaybackDecoderFfmpegFrameDataResult.NativeFailure;
            }

            if (fixedResult != PlaybackDecoderFfmpegFrameDataResult.Processed)
            {
                return fixedResult;
            }

            foreach (var plane in planes)
            {
                var length = checked(plane.Stride * plane.Rows);
                var pointer = Marshal.AllocHGlobal(length);
                try
                {
                    for (var index = 0; index < length; index++)
                    {
                        Marshal.WriteByte(
                            pointer,
                            index,
                            plane.LogicalFillByte ?? (byte)(index + 1));
                    }

                    if (plane.PaddingByte is { } padding)
                    {
                        for (var row = 0; row < plane.Rows; row++)
                        {
                            for (var index = plane.LogicalBytes; index < plane.Stride; index++)
                            {
                                Marshal.WriteByte(pointer, checked(row * plane.Stride + index), padding);
                            }
                        }
                    }

                    NativeViewCount++;
                    var view = new PlaybackDecoderFfmpegFrameDataView(
                        pointer,
                        plane.Kind,
                        plane.Index,
                        plane.ReportedLength ?? length,
                        plane.Stride,
                        plane.Rows);
                    if (!consumer.Consume(metadata, view))
                    {
                        return PlaybackDecoderFfmpegFrameDataResult.ConsumerRejected;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(pointer);
                }
            }

            return PlaybackDecoderFfmpegFrameDataResult.Processed;
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

        public bool RuntimeIsDisposed => _runtime.IsDisposed;

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
