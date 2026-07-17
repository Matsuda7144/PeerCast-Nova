namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDecoderFfmpegContextLeaseTests
{
    private static readonly string[] RuntimeFiles =
    [
        "avutil-60.dll",
        "swresample-6.dll",
        "swscale-9.dll",
        "avcodec-62.dll",
        "avformat-62.dll",
    ];

    [Fact]
    public void Create_SupportedCodecRetainsParentsUntilOrderedDisposal()
    {
        using var runtime = new FakeRuntime();
        var events = new List<string>();
        var loader = new FakeLoader(events);
        var native = new FakeNativeApi(events);
        var gate = CreateGate(loader);
        var gateLease = Assert.IsType<PlaybackDecoderFfmpegAbiSymbolGateLease>(
            gate.Acquire(runtime.Lease).Lease);
        var factory = new PlaybackDecoderFfmpegContextLeaseFactory(
            gate,
            native,
            isSupportedPlatform: true);

        var result = factory.Create(gateLease, PlaybackDecoderFfmpegCodecId.H264);

        Assert.Equal(PlaybackDecoderFfmpegContextLeaseResult.Created, result.Result);
        Assert.Equal(5, loader.LoadedModules.Count);
        var lease = Assert.IsType<PlaybackDecoderFfmpegContextLease>(result.Lease);
        Assert.Equal((nint)900, lease.UseContext(static context => context));
        Assert.False(runtime.Lease.IsDisposed);
        Assert.Empty(loader.FreedModules);

        lease.Dispose();

        Assert.True(runtime.Lease.IsDisposed);
        Assert.Equal(1, native.FreeCount);
        Assert.Equal(loader.LoadedModules.AsEnumerable().Reverse(), loader.FreedModules);
        Assert.True(
            events.IndexOf("context:free") <
            events.FindIndex(static entry => entry.StartsWith("module:free:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Create_UnknownCodecIdFailsBeforeGateOrNativeCall()
    {
        using var runtime = new FakeRuntime();
        var loader = new FakeLoader([]);
        var native = new FakeNativeApi([]);

        var result = CreateFactory(loader, native).Create(
            runtime.Lease,
            (PlaybackDecoderFfmpegCodecId)int.MaxValue);

        Assert.Equal(PlaybackDecoderFfmpegContextLeaseResult.UnknownCodecId, result.Result);
        Assert.Null(result.Lease);
        Assert.Empty(loader.LoadedModules);
        Assert.Equal(0, native.FindCount);
    }

    [Fact]
    public void Create_MissingDecoderFailsClosedAndReleasesGate()
    {
        using var runtime = new FakeRuntime();
        var loader = new FakeLoader([]);
        var native = new FakeNativeApi([]) { Decoder = 0 };

        var result = CreateFactory(loader, native).Create(
            runtime.Lease,
            PlaybackDecoderFfmpegCodecId.Aac);

        Assert.Equal(PlaybackDecoderFfmpegContextLeaseResult.DecoderUnavailable, result.Result);
        Assert.Equal(5, loader.FreedModules.Count);
        Assert.False(runtime.Lease.IsDisposed);
        Assert.Equal(0, native.AllocateCount);
    }

    [Fact]
    public void Create_ContextAllocationFailureReleasesGateWithoutContextFree()
    {
        using var runtime = new FakeRuntime();
        var loader = new FakeLoader([]);
        var native = new FakeNativeApi([]) { Context = 0 };

        var result = CreateFactory(loader, native).Create(
            runtime.Lease,
            PlaybackDecoderFfmpegCodecId.H264);

        Assert.Equal(
            PlaybackDecoderFfmpegContextLeaseResult.ContextAllocationFailed,
            result.Result);
        Assert.Equal(5, loader.FreedModules.Count);
        Assert.Equal(0, native.FreeCount);
    }

    [Fact]
    public void Create_NativeExceptionFailsClosedAndCleansPartialGate()
    {
        using var runtime = new FakeRuntime();
        var loader = new FakeLoader([]);
        var native = new FakeNativeApi([]) { ThrowOnFind = true };

        var result = CreateFactory(loader, native).Create(
            runtime.Lease,
            PlaybackDecoderFfmpegCodecId.H264);

        Assert.Equal(PlaybackDecoderFfmpegContextLeaseResult.NativeFailure, result.Result);
        Assert.Equal(5, loader.FreedModules.Count);
        Assert.False(runtime.Lease.IsDisposed);
    }

    [Fact]
    public void Create_AllocationExceptionAfterDecoderLookupCleansPartialGate()
    {
        using var runtime = new FakeRuntime();
        var loader = new FakeLoader([]);
        var native = new FakeNativeApi([]) { ThrowOnAllocate = true };

        var result = CreateFactory(loader, native).Create(
            runtime.Lease,
            PlaybackDecoderFfmpegCodecId.H264);

        Assert.Equal(PlaybackDecoderFfmpegContextLeaseResult.NativeFailure, result.Result);
        Assert.Equal(1, native.FindCount);
        Assert.Equal(1, native.AllocateCount);
        Assert.Equal(5, loader.FreedModules.Count);
        Assert.Equal(loader.LoadedModules.AsEnumerable().Reverse(), loader.FreedModules);
    }

    [Fact]
    public void Dispose_IsIdempotentAndFreesEveryOwnerOnce()
    {
        using var runtime = new FakeRuntime();
        var loader = new FakeLoader([]);
        var native = new FakeNativeApi([]);
        var lease = Assert.IsType<PlaybackDecoderFfmpegContextLease>(
            CreateFactory(loader, native)
                .Create(runtime.Lease, PlaybackDecoderFfmpegCodecId.H264)
                .Lease);

        lease.Dispose();
        lease.Dispose();

        Assert.Equal(1, native.FreeCount);
        Assert.Equal(5, loader.FreedModules.Distinct().Count());
        Assert.Equal(5, loader.FreedModules.Count);
    }

    [Fact]
    public void UseContext_RejectsDisposedChildLease()
    {
        using var runtime = new FakeRuntime();
        var lease = Assert.IsType<PlaybackDecoderFfmpegContextLease>(
            CreateFactory(new FakeLoader([]), new FakeNativeApi([]))
                .Create(runtime.Lease, PlaybackDecoderFfmpegCodecId.H264)
                .Lease);
        lease.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            lease.UseContext(static context => context));
    }

    [Fact]
    public void UseContext_RejectsParentDisposedBeforeChild()
    {
        using var runtime = new FakeRuntime();
        var lease = Assert.IsType<PlaybackDecoderFfmpegContextLease>(
            CreateFactory(new FakeLoader([]), new FakeNativeApi([]))
                .Create(runtime.Lease, PlaybackDecoderFfmpegCodecId.H264)
                .Lease);
        runtime.Lease.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            lease.UseContext(static context => context));

        lease.Dispose();
    }

    [Fact]
    public void UseContext_RejectsGateDisposedBeforeChild()
    {
        using var runtime = new FakeRuntime();
        var loader = new FakeLoader([]);
        var gate = CreateGate(loader);
        var gateLease = Assert.IsType<PlaybackDecoderFfmpegAbiSymbolGateLease>(
            gate.Acquire(runtime.Lease).Lease);
        var native = new FakeNativeApi([]);
        var contextLease = new PlaybackDecoderFfmpegContextLease(
            runtime.Lease,
            gateLease,
            native,
            PlaybackDecoderFfmpegCodecId.H264,
            (nint)3,
            (nint)900);
        gateLease.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            contextLease.UseContext(static context => context));

        contextLease.Dispose();
    }

    [Fact]
    public void Dispose_NativeCleanupFailureStillReleasesGateAndParent()
    {
        using var runtime = new FakeRuntime();
        var loader = new FakeLoader([]);
        var native = new FakeNativeApi([]) { ThrowOnFree = true };
        var lease = Assert.IsType<PlaybackDecoderFfmpegContextLease>(
            CreateFactory(loader, native)
                .Create(runtime.Lease, PlaybackDecoderFfmpegCodecId.H264)
                .Lease);

        lease.Dispose();

        Assert.Equal(5, loader.FreedModules.Count);
        Assert.True(runtime.Lease.IsDisposed);
    }

    [Fact]
    public void Create_ResultAndFailureContainNoNativeInputInformation()
    {
        using var runtime = new FakeRuntime();
        var result = CreateFactory(
                new FakeLoader([]),
                new FakeNativeApi([]) { Decoder = 0 })
            .Create(runtime.Lease, PlaybackDecoderFfmpegCodecId.H264);
        var diagnostic = result.Result.ToString();

        Assert.DoesNotContain(runtime.DirectoryPath, diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SHA", diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("avcodec", diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            [nameof(result.Lease), nameof(result.Result)],
            result.GetType().GetProperties().Select(static property => property.Name).Order());
    }

    [NonWindowsFact]
    public void Create_NonWindowsReturnsFixedUnsupportedWithoutTouchingParent()
    {
        using var runtime = new FakeRuntime();

        var result = new PlaybackDecoderFfmpegContextLeaseFactory().Create(
            runtime.Lease,
            PlaybackDecoderFfmpegCodecId.H264);

        Assert.Equal(PlaybackDecoderFfmpegContextLeaseResult.Unsupported, result.Result);
        Assert.Null(result.Lease);
        Assert.False(runtime.Lease.IsDisposed);
    }

    private static PlaybackDecoderFfmpegContextLeaseFactory CreateFactory(
        FakeLoader loader,
        FakeNativeApi native) =>
        new(CreateGate(loader), native, isSupportedPlatform: true);

    private static PlaybackDecoderFfmpegAbiSymbolGate CreateGate(FakeLoader loader) =>
        new(loader, new FakeInspector(), isSupportedPlatform: true);

    private sealed class FakeInspector : IPlaybackDecoderNativeDependencyInspector
    {
        public bool TryGetDependencies(
            Stream image,
            out IReadOnlyList<string> dependencies)
        {
            dependencies = ["kernel32.dll", "avutil-60.dll"];
            return true;
        }
    }

    private sealed class FakeLoader : IPlaybackDecoderNativeLibraryLoader
    {
        private readonly List<string> _events;
        private readonly Dictionary<nint, string> _moduleNames = [];
        private long _nextHandle = 100;

        public FakeLoader(List<string> events)
        {
            _events = events;
        }

        public List<nint> LoadedModules { get; } = [];

        public List<nint> FreedModules { get; } = [];

        public nint Load(string verifiedPath, PlaybackDecoderNativeSearchScope searchScope)
        {
            Assert.Equal(
                PlaybackDecoderNativeSearchScope.VerifiedDirectoryAndSystem32,
                searchScope);
            var module = (nint)_nextHandle++;
            _moduleNames.Add(module, Path.GetFileName(verifiedPath));
            LoadedModules.Add(module);
            _events.Add($"module:load:{module}");
            return module;
        }

        public nint GetSymbol(nint module, string symbol)
        {
            var address = (nint)_nextHandle++;
            _moduleNames.Add(address, _moduleNames[module]);
            return address;
        }

        public uint InvokeVersion(nint symbol) =>
            ParseMajor(_moduleNames[symbol]) << 16;

        public void Free(nint module)
        {
            Assert.DoesNotContain(module, FreedModules);
            FreedModules.Add(module);
            _events.Add($"module:free:{module}");
        }

        private static uint ParseMajor(string moduleName)
        {
            var dash = moduleName.LastIndexOf('-');
            var dot = moduleName.LastIndexOf('.');
            return uint.Parse(
                moduleName.AsSpan(dash + 1, dot - dash - 1),
                System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private sealed class FakeNativeApi : IPlaybackDecoderFfmpegContextNativeApi
    {
        private readonly List<string> _events;

        public FakeNativeApi(List<string> events)
        {
            _events = events;
        }

        public nint Decoder { get; init; } = (nint)800;

        public nint Context { get; init; } = (nint)900;

        public bool ThrowOnFind { get; init; }

        public bool ThrowOnAllocate { get; init; }

        public bool ThrowOnFree { get; init; }

        public int FindCount { get; private set; }

        public int AllocateCount { get; private set; }

        public int FreeCount { get; private set; }

        public nint FindDecoder(nint function, PlaybackDecoderFfmpegCodecId codecId)
        {
            FindCount++;
            if (ThrowOnFind)
            {
                throw new InvalidOperationException("sensitive native failure");
            }

            return Decoder;
        }

        public nint AllocateContext(nint function, nint decoder)
        {
            AllocateCount++;
            if (ThrowOnAllocate)
            {
                throw new InvalidOperationException("sensitive native allocation failure");
            }

            return Context;
        }

        public void FreeContext(nint function, ref nint context)
        {
            FreeCount++;
            _events.Add("context:free");
            context = 0;
            if (ThrowOnFree)
            {
                throw new InvalidOperationException("sensitive native cleanup failure");
            }
        }
    }

    private sealed class FakeRuntime : IDisposable
    {
        public FakeRuntime()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            var streams = new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase);
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fileName in RuntimeFiles)
            {
                var path = Path.Combine(DirectoryPath, fileName);
                File.WriteAllBytes(path, [1, 2, 3, 4]);
                streams.Add(
                    fileName,
                    new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
                paths.Add(fileName, path);
            }

            Lease = new VerifiedPlaybackDecoderRuntimeLease(streams, paths, []);
        }

        public string DirectoryPath { get; }

        public VerifiedPlaybackDecoderRuntimeLease Lease { get; }

        public void Dispose()
        {
            Lease.Dispose();
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
