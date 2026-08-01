namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDecoderFfmpegDiscontinuityResetTests
{
    [Theory]
    [InlineData(PlaybackDecoderFfmpegCodecId.H264)]
    [InlineData(PlaybackDecoderFfmpegCodecId.Aac)]
    public void Reset_OpenedContextFlushesAndRetainsOwnership(
        PlaybackDecoderFfmpegCodecId codecId)
    {
        using var owners = new TestOwners(codecId);
        owners.MarkOpened();
        var native = new FakeResetNativeApi();

        var result = CreateBoundary(native).Reset(owners.Context);

        Assert.Equal(PlaybackDecoderFfmpegDiscontinuityResetResult.Reset, result);
        Assert.Equal(1, native.CallCount);
        Assert.Equal((nint)6, native.Function);
        Assert.Equal((nint)900, native.Context);
        Assert.True(owners.Context.IsOpened);
        Assert.False(owners.Context.IsDisposed);
        Assert.Equal(0, owners.ContextNative.FreeCount);
    }

    [Fact]
    public void Reset_UnopenedContextRejectsBeforeNativeAccess()
    {
        using var owners = new TestOwners(PlaybackDecoderFfmpegCodecId.H264);
        var native = new FakeResetNativeApi();

        var result = CreateBoundary(native).Reset(owners.Context);

        Assert.Equal(
            PlaybackDecoderFfmpegDiscontinuityResetResult.ContextNotOpened,
            result);
        Assert.Equal(0, native.CallCount);
        Assert.False(owners.Context.IsDisposed);
    }

    [Fact]
    public void Reset_MissingSymbolFailsClosedWithoutNativeAccess()
    {
        using var owners = new TestOwners(
            PlaybackDecoderFfmpegCodecId.Aac,
            includeFlushSymbol: false);
        owners.MarkOpened();
        var native = new FakeResetNativeApi();

        var result = CreateBoundary(native).Reset(owners.Context);

        Assert.Equal(PlaybackDecoderFfmpegDiscontinuityResetResult.GateRejected, result);
        Assert.Equal(0, native.CallCount);
        Assert.True(owners.Context.IsOpened);
        Assert.False(owners.Context.IsDisposed);
    }

    [Fact]
    public void Reset_UnsupportedRejectsBeforeNativeAccessAndRetainsContext()
    {
        using var owners = new TestOwners(PlaybackDecoderFfmpegCodecId.H264);
        owners.MarkOpened();
        var native = new FakeResetNativeApi();

        var result = new PlaybackDecoderFfmpegDiscontinuityResetBoundary(
            native,
            isSupportedPlatform: false).Reset(owners.Context);

        Assert.Equal(PlaybackDecoderFfmpegDiscontinuityResetResult.Unsupported, result);
        Assert.Equal(0, native.CallCount);
        Assert.False(owners.Context.IsDisposed);
    }

    [Fact]
    public void Reset_DisposedContextRejectsBeforeNativeAccess()
    {
        using var owners = new TestOwners(PlaybackDecoderFfmpegCodecId.H264);
        var native = new FakeResetNativeApi();
        owners.Context.Dispose();

        var result = CreateBoundary(native).Reset(owners.Context);

        Assert.Equal(
            PlaybackDecoderFfmpegDiscontinuityResetResult.ContextUnavailable,
            result);
        Assert.Equal(0, native.CallCount);
    }

    [Fact]
    public void Reset_NativeExceptionDiscardsContextFailClosed()
    {
        using var owners = new TestOwners(PlaybackDecoderFfmpegCodecId.Aac);
        owners.MarkOpened();
        var native = new FakeResetNativeApi { ThrowOnFlush = true };

        var result = CreateBoundary(native).Reset(owners.Context);

        Assert.Equal(PlaybackDecoderFfmpegDiscontinuityResetResult.NativeFailure, result);
        Assert.Equal(1, native.CallCount);
        Assert.True(owners.Context.IsDisposed);
        Assert.Equal(1, owners.ContextNative.FreeCount);
    }

    [Fact]
    public void Reset_ResultExposesNoContextPointerOrNativeErrorText()
    {
        using var owners = new TestOwners(PlaybackDecoderFfmpegCodecId.H264);
        owners.MarkOpened();
        var native = new FakeResetNativeApi { ThrowOnFlush = true };

        var result = CreateBoundary(native).Reset(owners.Context);
        var diagnostic = result.ToString();

        Assert.Equal(PlaybackDecoderFfmpegDiscontinuityResetResult.NativeFailure, result);
        Assert.DoesNotContain("900", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive", diagnostic, StringComparison.Ordinal);
        Assert.Empty(result.GetType().GetProperties());
    }

    private static PlaybackDecoderFfmpegDiscontinuityResetBoundary CreateBoundary(
        FakeResetNativeApi native) =>
        new(native, isSupportedPlatform: true);

    private sealed class FakeResetNativeApi :
        IPlaybackDecoderFfmpegDiscontinuityResetNativeApi
    {
        public bool ThrowOnFlush { get; init; }

        public int CallCount { get; private set; }

        public nint Function { get; private set; }

        public nint Context { get; private set; }

        public void Flush(nint function, nint context)
        {
            CallCount++;
            Function = function;
            Context = context;
            if (ThrowOnFlush)
            {
                throw new InvalidOperationException("sensitive native reset failure");
            }
        }
    }

    private sealed class TestOwners : IDisposable
    {
        private readonly string _directory;
        private readonly VerifiedPlaybackDecoderRuntimeLease _runtime;

        public TestOwners(
            PlaybackDecoderFfmpegCodecId codecId,
            bool includeFlushSymbol = true)
        {
            _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, "runtime.bin");
            File.WriteAllBytes(path, [1, 2, 3, 4]);
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            _runtime = new VerifiedPlaybackDecoderRuntimeLease(
                new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase)
                {
                    ["runtime.bin"] = stream,
                },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["runtime.bin"] = path,
                },
                []);

            var symbols = new Dictionary<string, nint>(StringComparer.Ordinal);
            if (includeFlushSymbol)
            {
                symbols.Add("avcodec_flush_buffers", (nint)6);
            }

            var gate = new PlaybackDecoderFfmpegAbiSymbolGateLease(
                _runtime,
                new FakeLoader(),
                [(nint)10],
                symbols);
            ContextNative = new FakeContextNativeApi();
            Context = new PlaybackDecoderFfmpegContextLease(
                _runtime,
                gate,
                ContextNative,
                codecId,
                freeContext: (nint)7,
                context: (nint)900);
        }

        public PlaybackDecoderFfmpegContextLease Context { get; }

        public FakeContextNativeApi ContextNative { get; }

        public void MarkOpened()
        {
            Context.MarkParametersTransferred();
            Context.MarkOpened();
        }

        public void Dispose()
        {
            Context.Dispose();
            _runtime.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FakeLoader : IPlaybackDecoderNativeLibraryLoader
    {
        public nint Load(string verifiedPath, PlaybackDecoderNativeSearchScope searchScope) =>
            throw new NotSupportedException();

        public nint GetSymbol(nint module, string symbol) =>
            throw new NotSupportedException();

        public uint InvokeVersion(nint symbol) => throw new NotSupportedException();

        public void Free(nint module)
        {
        }
    }

    private sealed class FakeContextNativeApi : IPlaybackDecoderFfmpegContextNativeApi
    {
        public int FreeCount { get; private set; }

        public nint FindDecoder(nint function, PlaybackDecoderFfmpegCodecId codecId) =>
            throw new NotSupportedException();

        public nint AllocateContext(nint function, nint decoder) =>
            throw new NotSupportedException();

        public void FreeContext(nint function, ref nint context)
        {
            FreeCount++;
            context = 0;
        }
    }
}
