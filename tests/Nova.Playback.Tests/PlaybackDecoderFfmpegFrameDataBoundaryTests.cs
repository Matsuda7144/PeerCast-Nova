using System.Runtime.InteropServices;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDecoderFfmpegFrameDataBoundaryTests
{
    private static readonly string[] RequiredFrameSymbols =
        ["av_frame_alloc", "av_frame_free", "avcodec_receive_frame"];

    private static readonly PlaybackDecoderFfmpegFrameMetadata VideoMetadata = new(
        PlaybackDecoderFfmpegFrameMediaKind.Video,
        PlaybackDecoderFfmpegFrameFormat.VideoYuv420P,
        4,
        2,
        0,
        0,
        0,
        PlaybackDecoderFfmpegFrameTime.Unknown,
        PlaybackDecoderFfmpegFrameTime.Unknown,
        PlaybackDecoderFfmpegFrameTime.Unknown);

    private static readonly PlaybackDecoderFfmpegFrameMetadata AudioMetadata = new(
        PlaybackDecoderFfmpegFrameMediaKind.Audio,
        PlaybackDecoderFfmpegFrameFormat.AudioSigned16Planar,
        0,
        0,
        48_000,
        2,
        4,
        PlaybackDecoderFfmpegFrameTime.Unknown,
        PlaybackDecoderFfmpegFrameTime.Unknown,
        PlaybackDecoderFfmpegFrameTime.Unknown);

    [Fact]
    public void ValidateAndConsume_ValidVideoExposesCallbackScopedPlanes()
    {
        using var memory = new FakeMemoryReader([4, 2, 2], 3);
        memory.WriteByte(0, 0x42);
        var consumer = new RecordingConsumer(readFirstByte: true);

        var result = CreateNative(memory).ValidateAndConsume((nint)400, VideoMetadata, consumer);

        Assert.Equal(PlaybackDecoderFfmpegFrameDataVisitResult.Consumed, result);
        Assert.Equal(
            [
                (PlaybackDecoderFfmpegFrameDataKind.VideoPlane, 0, 8, 4, 2),
                (PlaybackDecoderFfmpegFrameDataKind.VideoPlane, 1, 2, 2, 1),
                (PlaybackDecoderFfmpegFrameDataKind.VideoPlane, 2, 2, 2, 1),
            ],
            consumer.Items);
        Assert.Equal((byte)0x42, consumer.FirstByte);
        Assert.All(consumer.Metadata, item =>
        {
            Assert.False(item.PresentationTimestamp.IsKnown);
            Assert.False(item.Duration.IsKnown);
        });
    }

    [Fact]
    public void ValidateAndConsume_ValidPlanarAudioExposesOnePlanePerChannel()
    {
        using var memory = new FakeMemoryReader([8], 2);
        var consumer = new RecordingConsumer();

        var result = CreateNative(memory).ValidateAndConsume((nint)400, AudioMetadata, consumer);

        Assert.Equal(PlaybackDecoderFfmpegFrameDataVisitResult.Consumed, result);
        Assert.Equal(
            [
                (PlaybackDecoderFfmpegFrameDataKind.AudioSamples, 0, 8, 8, 1),
                (PlaybackDecoderFfmpegFrameDataKind.AudioSamples, 1, 8, 8, 1),
            ],
            consumer.Items);
    }

    [Theory]
    [InlineData(5, (int)PlaybackDecoderFfmpegFrameDataVisitResult.InvalidData)]
    [InlineData(6, (int)PlaybackDecoderFfmpegFrameDataVisitResult.Consumed)]
    public void ValidateAndConsume_OddWidthNv12RequiresRoundedChromaStride(
        int chromaStride,
        int expected)
    {
        using var memory = new FakeMemoryReader([5, chromaStride], 2);
        var metadata = new PlaybackDecoderFfmpegFrameMetadata(
            PlaybackDecoderFfmpegFrameMediaKind.Video,
            PlaybackDecoderFfmpegFrameFormat.VideoNv12,
            5,
            2,
            0,
            0,
            0,
            default,
            default,
            default);

        Assert.Equal(
            (PlaybackDecoderFfmpegFrameDataVisitResult)expected,
            CreateNative(memory).ValidateAndConsume(
                (nint)400,
                metadata,
                new RecordingConsumer()));
    }

    [Fact]
    public void ValidateAndConsume_Nv12StrideCalculationOverflowFailsClosed()
    {
        using var memory = new FakeMemoryReader([1, 1], 2);
        var metadata = new PlaybackDecoderFfmpegFrameMetadata(
            PlaybackDecoderFfmpegFrameMediaKind.Video,
            PlaybackDecoderFfmpegFrameFormat.VideoNv12,
            int.MaxValue,
            2,
            0,
            0,
            0,
            default,
            default,
            default);

        Assert.Equal(
            PlaybackDecoderFfmpegFrameDataVisitResult.InvalidData,
            CreateNative(memory).ValidateAndConsume(
                (nint)400,
                metadata,
                new RecordingConsumer()));
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 1)]
    [InlineData(1, PlaybackDecodedVideoFrame.MaximumStride + 1)]
    public void ValidateAndConsume_InvalidVideoPointerOrLineSizeFailsClosed(
        int pointerIndex,
        int lineSize)
    {
        using var memory = new FakeMemoryReader([4, 2, 2], 3);
        memory.LineSizes[pointerIndex] = lineSize;
        if (lineSize == 4)
        {
            memory.SetNull(pointerIndex);
        }

        Assert.Equal(
            PlaybackDecoderFfmpegFrameDataVisitResult.InvalidData,
            CreateNative(memory).ValidateAndConsume(
                (nint)400,
                VideoMetadata,
                new RecordingConsumer()));
    }

    [Fact]
    public void ValidateAndConsume_TotalSizeAboveLimitFailsClosed()
    {
        using var memory = new FakeMemoryReader(
            [PlaybackDecodedVideoFrame.MaximumStride],
            1);
        var metadata = new PlaybackDecoderFfmpegFrameMetadata(
            PlaybackDecoderFfmpegFrameMediaKind.Video,
            PlaybackDecoderFfmpegFrameFormat.VideoBgra32,
            PlaybackDecodedVideoFrame.MaximumDimension,
            PlaybackDecodedVideoFrame.MaximumDimension,
            0,
            0,
            0,
            default,
            default,
            default);

        Assert.Equal(
            PlaybackDecoderFfmpegFrameDataVisitResult.InvalidData,
            CreateNative(memory).ValidateAndConsume(
                (nint)400,
                metadata,
                new RecordingConsumer()));
    }

    [Fact]
    public void ValidateAndConsume_MismatchedMediaKindAndFormatFailsClosed()
    {
        using var memory = new FakeMemoryReader([8], 1);
        var mismatched = new PlaybackDecoderFfmpegFrameMetadata(
            PlaybackDecoderFfmpegFrameMediaKind.Video,
            PlaybackDecoderFfmpegFrameFormat.AudioSigned16,
            4,
            2,
            0,
            0,
            0,
            default,
            default,
            default);

        Assert.Equal(
            PlaybackDecoderFfmpegFrameDataVisitResult.InvalidData,
            CreateNative(memory).ValidateAndConsume(
                (nint)400,
                mismatched,
                new RecordingConsumer()));
    }

    [Fact]
    public void ReceiveAndConsume_ConsumerRejectionPreservesContext()
    {
        using var owners = new TestOwners();
        var boundary = CreateBoundary(
            new FixedMetadataNativeApi(),
            new FixedDataNativeApi(PlaybackDecoderFfmpegFrameDataVisitResult.ConsumerRejected),
            true);

        Assert.Equal(
            PlaybackDecoderFfmpegFrameDataResult.ConsumerRejected,
            boundary.ReceiveAndConsume(owners.Context, new RecordingConsumer()));
        Assert.False(owners.Context.IsDisposed);
    }

    [Fact]
    public void ReceiveAndConsume_NativeExceptionDisposesContextWithoutLeakingInput()
    {
        using var owners = new TestOwners();
        var boundary = CreateBoundary(
            new FixedMetadataNativeApi(),
            new ThrowingDataNativeApi(),
            true);

        var result = boundary.ReceiveAndConsume(owners.Context, new RecordingConsumer());

        Assert.Equal(PlaybackDecoderFfmpegFrameDataResult.NativeFailure, result);
        Assert.True(owners.Context.IsDisposed);
        Assert.DoesNotContain("sensitive", result.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0x", result.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReceiveAndConsume_UnsupportedPlatformAvoidsAllNativeAccess()
    {
        using var owners = new TestOwners();
        var metadata = new FixedMetadataNativeApi();
        var data = new FixedDataNativeApi(PlaybackDecoderFfmpegFrameDataVisitResult.Consumed);
        var boundary = CreateBoundary(metadata, data, false);

        Assert.Equal(
            PlaybackDecoderFfmpegFrameDataResult.Unsupported,
            boundary.ReceiveAndConsume(owners.Context, new RecordingConsumer()));
        Assert.Equal(0, metadata.CallCount);
        Assert.Equal(0, data.CallCount);
    }

    private static WindowsFfmpeg81FrameDataNativeApi CreateNative(
        IPlaybackDecoderFfmpegFrameMemoryReader memory) => new(memory, true);

    private static PlaybackDecoderFfmpegFrameDataBoundary CreateBoundary(
        IPlaybackDecoderFfmpegFrameMetadataNativeApi metadataApi,
        IPlaybackDecoderFfmpegFrameDataNativeApi dataApi,
        bool supported)
    {
        var frames = new PlaybackDecoderFfmpegNativeFrameBoundary(
            new FakeFrameNativeApi(),
            new RejectingPlaybackDecoderFfmpegNativeFrameConsumer(),
            true);
        var metadata = new PlaybackDecoderFfmpegFrameMetadataBoundary(
            frames,
            metadataApi,
            true);
        return new(frames, metadata, dataApi, supported);
    }

    private sealed class RecordingConsumer(bool accept = true, bool readFirstByte = false) :
        IPlaybackDecoderFfmpegFrameDataConsumer
    {
        public List<(PlaybackDecoderFfmpegFrameDataKind, int, int, int, int)> Items { get; } = [];
        public List<PlaybackDecoderFfmpegFrameMetadata> Metadata { get; } = [];
        public byte? FirstByte { get; private set; }

        public bool Consume(
            PlaybackDecoderFfmpegFrameMetadata metadata,
            PlaybackDecoderFfmpegFrameDataView data)
        {
            Metadata.Add(metadata);
            Items.Add((data.Kind, data.Index, data.Length, data.Stride, data.Rows));
            if (readFirstByte && FirstByte is null)
            {
                FirstByte = data.ReadByte(0);
            }

            return accept;
        }
    }

    private sealed class FakeMemoryReader : IPlaybackDecoderFfmpegFrameMemoryReader, IDisposable
    {
        private readonly nint[] _pointers;
        private readonly nint[] _allocations;

        public FakeMemoryReader(int[] lineSizes, int pointerCount)
        {
            LineSizes = lineSizes;
            _pointers = Enumerable.Range(0, pointerCount)
                .Select(_ => Marshal.AllocHGlobal(16))
                .ToArray();
            _allocations = [.. _pointers];
        }

        public int[] LineSizes { get; }

        public nint ReadVideoDataPointer(nint frame, int planeIndex) => _pointers[planeIndex];
        public nint ReadAudioDataPointer(nint frame, int planeIndex) => _pointers[planeIndex];
        public int ReadLineSize(nint frame, int planeIndex) => LineSizes[planeIndex];
        public void SetNull(int index) => _pointers[index] = 0;
        public void WriteByte(int index, byte value) => Marshal.WriteByte(_pointers[index], value);

        public void Dispose()
        {
            foreach (var pointer in _allocations)
            {
                Marshal.FreeHGlobal(pointer);
            }
        }
    }

    private sealed class FixedMetadataNativeApi : IPlaybackDecoderFfmpegFrameMetadataNativeApi
    {
        public int CallCount { get; private set; }

        public PlaybackDecoderFfmpegRawFrameMetadata Read(nint frame)
        {
            CallCount++;
            return new(0, 4, 2, 0, 0, 0, long.MinValue, long.MinValue, 0, 0, 1);
        }
    }

    private sealed class FixedDataNativeApi(PlaybackDecoderFfmpegFrameDataVisitResult result) :
        IPlaybackDecoderFfmpegFrameDataNativeApi
    {
        public int CallCount { get; private set; }

        public PlaybackDecoderFfmpegFrameDataVisitResult ValidateAndConsume(
            nint frame,
            PlaybackDecoderFfmpegFrameMetadata metadata,
            IPlaybackDecoderFfmpegFrameDataConsumer consumer)
        {
            CallCount++;
            return result;
        }
    }

    private sealed class ThrowingDataNativeApi : IPlaybackDecoderFfmpegFrameDataNativeApi
    {
        public PlaybackDecoderFfmpegFrameDataVisitResult ValidateAndConsume(
            nint frame,
            PlaybackDecoderFfmpegFrameMetadata metadata,
            IPlaybackDecoderFfmpegFrameDataConsumer consumer) =>
            throw new InvalidOperationException("sensitive pointer 0x1234");
    }

    private sealed class FakeFrameNativeApi : IPlaybackDecoderFfmpegNativeFrameApi
    {
        public PlaybackDecoderFfmpegNativeFrameReceiveResult ReceiveAndConsume(
            nint frameAllocFunction,
            nint frameFreeFunction,
            nint receiveFrameFunction,
            nint context,
            IPlaybackDecoderFfmpegNativeFrameConsumer consumer)
        {
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
