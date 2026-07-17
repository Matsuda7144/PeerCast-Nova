namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDecoderFfmpegNativeFrameBoundaryTests
{
    [Fact]
    public void ReceiveAndProcess_OpenedContextConsumesFrame()
    {
        using var owners = new TestOwners(opened: true);
        var native = new FakeNativeApi();
        var consumer = new FakeConsumer();
        var boundary = new PlaybackDecoderFfmpegNativeFrameBoundary(native, consumer, true);

        var result = boundary.ReceiveAndProcess(owners.Context);

        Assert.Equal(PlaybackDecoderFfmpegNativeFrameResult.Processed, result);
        Assert.Equal(1, native.CallCount);
        Assert.Equal(1, consumer.CallCount);
        Assert.False(owners.Context.IsDisposed);
    }

    [Fact]
    public void ReceiveAndProcess_UnopenedContextRejectsBeforeNativeAccess()
    {
        using var owners = new TestOwners(opened: false);
        var native = new FakeNativeApi();
        var boundary = new PlaybackDecoderFfmpegNativeFrameBoundary(
            native,
            new FakeConsumer(),
            true);

        var result = boundary.ReceiveAndProcess(owners.Context);

        Assert.Equal(PlaybackDecoderFfmpegNativeFrameResult.ContextNotOpened, result);
        Assert.Equal(0, native.CallCount);
    }

    [Theory]
    [InlineData((int)PlaybackDecoderFfmpegNativeFrameReceiveResult.Again,
        PlaybackDecoderFfmpegNativeFrameResult.NeedMoreInput)]
    [InlineData((int)PlaybackDecoderFfmpegNativeFrameReceiveResult.EndOfStream,
        PlaybackDecoderFfmpegNativeFrameResult.EndOfStream)]
    [InlineData((int)PlaybackDecoderFfmpegNativeFrameReceiveResult.AllocationFailed,
        PlaybackDecoderFfmpegNativeFrameResult.NativeAllocationFailed)]
    [InlineData((int)PlaybackDecoderFfmpegNativeFrameReceiveResult.Rejected,
        PlaybackDecoderFfmpegNativeFrameResult.NativeRejected)]
    public void ReceiveAndProcess_MapsBoundedNativeResults(
        int nativeResult,
        PlaybackDecoderFfmpegNativeFrameResult expected)
    {
        using var owners = new TestOwners(opened: true);
        var native = new FakeNativeApi
        {
            Result = (PlaybackDecoderFfmpegNativeFrameReceiveResult)nativeResult,
        };
        var boundary = new PlaybackDecoderFfmpegNativeFrameBoundary(
            native,
            new FakeConsumer(),
            true);

        Assert.Equal(expected, boundary.ReceiveAndProcess(owners.Context));
    }

    [Fact]
    public void ReceiveAndProcess_ConsumerRejectionIsFixedAndContextRemainsUsable()
    {
        using var owners = new TestOwners(opened: true);
        var boundary = new PlaybackDecoderFfmpegNativeFrameBoundary(
            new FakeNativeApi { ThrowConsumerRejected = true },
            new FakeConsumer(),
            true);

        var result = boundary.ReceiveAndProcess(owners.Context);

        Assert.Equal(PlaybackDecoderFfmpegNativeFrameResult.ConsumerRejected, result);
        Assert.False(owners.Context.IsDisposed);
    }

    [Fact]
    public void ReceiveAndProcess_MissingSymbolRejectsBeforeNativeAccess()
    {
        using var owners = new TestOwners(opened: true, omitSymbol: "av_frame_free");
        var native = new FakeNativeApi();
        var boundary = new PlaybackDecoderFfmpegNativeFrameBoundary(
            native,
            new FakeConsumer(),
            true);

        var result = boundary.ReceiveAndProcess(owners.Context);

        Assert.Equal(PlaybackDecoderFfmpegNativeFrameResult.GateRejected, result);
        Assert.Equal(0, native.CallCount);
    }

    [Fact]
    public void ReceiveAndProcess_NativeFailureDisposesContextWithoutLeakingText()
    {
        using var owners = new TestOwners(opened: true);
        var boundary = new PlaybackDecoderFfmpegNativeFrameBoundary(
            new FakeNativeApi { ThrowNativeFailure = true },
            new FakeConsumer(),
            true);

        var result = boundary.ReceiveAndProcess(owners.Context);

        Assert.Equal(PlaybackDecoderFfmpegNativeFrameResult.NativeFailure, result);
        Assert.True(owners.Context.IsDisposed);
        Assert.DoesNotContain("sensitive", result.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReceiveAndProcess_UnsupportedPlatformAvoidsNativeAccess()
    {
        using var owners = new TestOwners(opened: true);
        var native = new FakeNativeApi();
        var boundary = new PlaybackDecoderFfmpegNativeFrameBoundary(
            native,
            new FakeConsumer(),
            false);

        Assert.Equal(
            PlaybackDecoderFfmpegNativeFrameResult.Unsupported,
            boundary.ReceiveAndProcess(owners.Context));
        Assert.Equal(0, native.CallCount);
    }

    private sealed class FakeConsumer : IPlaybackDecoderFfmpegNativeFrameConsumer
    {
        public int CallCount { get; private set; }

        public bool Consume(nint frame)
        {
            CallCount++;
            Assert.NotEqual(0, frame);
            return true;
        }
    }

    private sealed class FakeNativeApi : IPlaybackDecoderFfmpegNativeFrameApi
    {
        public PlaybackDecoderFfmpegNativeFrameReceiveResult Result { get; init; } =
            PlaybackDecoderFfmpegNativeFrameReceiveResult.Success;
        public bool ThrowConsumerRejected { get; init; }
        public bool ThrowNativeFailure { get; init; }
        public int CallCount { get; private set; }

        public PlaybackDecoderFfmpegNativeFrameReceiveResult ReceiveAndConsume(
            nint frameAllocFunction,
            nint frameFreeFunction,
            nint receiveFrameFunction,
            nint context,
            IPlaybackDecoderFfmpegNativeFrameConsumer consumer)
        {
            CallCount++;
            Assert.All(
                new[] { frameAllocFunction, frameFreeFunction, receiveFrameFunction, context },
                value => Assert.NotEqual(0, value));
            if (ThrowNativeFailure)
            {
                throw new InvalidOperationException("sensitive native failure");
            }

            if (ThrowConsumerRejected)
            {
                throw new PlaybackDecoderFfmpegFrameConsumerRejectedException();
            }

            if (Result == PlaybackDecoderFfmpegNativeFrameReceiveResult.Success)
            {
                Assert.True(consumer.Consume((nint)400));
            }

            return Result;
        }
    }

    private sealed class TestOwners : IDisposable
    {
        private readonly string _directory;
        private readonly VerifiedPlaybackDecoderRuntimeLease _runtime;

        public TestOwners(bool opened, string? omitSymbol = null)
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
                PlaybackDecoderFfmpegCodecId.H264,
                (nint)200,
                (nint)300);
            if (opened)
            {
                Context.MarkParametersTransferred();
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
        public nint Load(string verifiedPath, PlaybackDecoderNativeSearchScope searchScope) =>
            throw new NotSupportedException();
        public nint GetSymbol(nint module, string symbol) => throw new NotSupportedException();
        public uint InvokeVersion(nint symbol) => throw new NotSupportedException();
        public void Free(nint module) { }
    }

    private sealed class FakeContextNativeApi : IPlaybackDecoderFfmpegContextNativeApi
    {
        public nint FindDecoder(nint function, PlaybackDecoderFfmpegCodecId codecId) => 0;
        public nint AllocateContext(nint function, nint decoder) => 0;
        public void FreeContext(nint function, ref nint context) => context = 0;
    }
}
