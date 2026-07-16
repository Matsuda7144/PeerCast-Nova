namespace PeerCast.Nova.Playback.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NativeEnvironmentDefinition
{
    public const string Name = "Native environment";
}

[Collection(NativeEnvironmentDefinition.Name)]
public sealed class PlaybackDecoderFfmpegAbiSymbolGateTests
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
    public void Verify_FiveDllsVersionsAndSymbolsPassWithFixedSearchScope()
    {
        using var runtime = new FakeRuntime();
        var loader = new FakeLoader();
        var gate = CreateGate(loader, new FakeInspector());

        var result = gate.Verify(runtime.Lease);

        Assert.Equal(PlaybackDecoderFfmpegAbiSymbolGateResult.Verified, result);
        Assert.Equal(RuntimeFiles, loader.LoadedNames);
        Assert.All(loader.SearchScopes, static scope => Assert.Equal(
            PlaybackDecoderNativeSearchScope.VerifiedDirectoryAndSystem32,
            scope));
        Assert.Equal(loader.LoadedModules.AsEnumerable().Reverse(), loader.FreedModules);
    }

    [Fact]
    public void Verify_MissingDllFailsBeforeDependencyInspectionOrLoad()
    {
        using var runtime = new FakeRuntime(RuntimeFiles[..4]);
        var loader = new FakeLoader();
        var inspector = new FakeInspector();

        var result = CreateGate(loader, inspector).Verify(runtime.Lease);

        Assert.Equal(PlaybackDecoderFfmpegAbiSymbolGateResult.RuntimeMismatch, result);
        Assert.Equal(0, inspector.CallCount);
        Assert.Empty(loader.LoadedModules);
    }

    [Fact]
    public void Verify_UnverifiedDependencyFailsClosedBeforeLoad()
    {
        using var runtime = new FakeRuntime();
        var loader = new FakeLoader();
        var inspector = new FakeInspector
        {
            Dependencies = ["kernel32.dll", "unverified-codec.dll"],
        };

        var result = CreateGate(loader, inspector).Verify(runtime.Lease);

        Assert.Equal(PlaybackDecoderFfmpegAbiSymbolGateResult.DependencyRejected, result);
        Assert.Empty(loader.LoadedModules);
    }

    [Fact]
    public void Verify_RequiredSymbolMissingReleasesLoadedModulesInReverseOrder()
    {
        using var runtime = new FakeRuntime();
        var loader = new FakeLoader { MissingSymbol = "avcodec_send_packet" };

        var result = CreateGate(loader).Verify(runtime.Lease);

        Assert.Equal(PlaybackDecoderFfmpegAbiSymbolGateResult.SymbolMissing, result);
        Assert.Equal(loader.LoadedModules.AsEnumerable().Reverse(), loader.FreedModules);
        Assert.Equal(4, loader.FreedModules.Count);
    }

    [Fact]
    public void Verify_VersionMismatchReleasesCurrentAndPriorModulesInReverseOrder()
    {
        using var runtime = new FakeRuntime();
        var loader = new FakeLoader
        {
            VersionMajorOverrides = { ["swscale-9.dll"] = 8 },
        };

        var result = CreateGate(loader).Verify(runtime.Lease);

        Assert.Equal(PlaybackDecoderFfmpegAbiSymbolGateResult.VersionMismatch, result);
        Assert.Equal(loader.LoadedModules.AsEnumerable().Reverse(), loader.FreedModules);
        Assert.Equal(3, loader.FreedModules.Count);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void Verify_PartialLoadFailureReleasesEveryPriorModuleInReverseOrder(
        int failingLoad)
    {
        using var runtime = new FakeRuntime();
        var loader = new FakeLoader { FailingLoad = failingLoad };

        var result = CreateGate(loader).Verify(runtime.Lease);

        Assert.Equal(PlaybackDecoderFfmpegAbiSymbolGateResult.ModuleLoadFailed, result);
        Assert.Equal(loader.LoadedModules.AsEnumerable().Reverse(), loader.FreedModules);
        Assert.Equal(failingLoad - 1, loader.FreedModules.Count);
    }

    [Fact]
    public void Verify_EachModuleIsReleasedExactlyOnce()
    {
        using var runtime = new FakeRuntime();
        var loader = new FakeLoader();
        var gate = CreateGate(loader);

        Assert.Equal(PlaybackDecoderFfmpegAbiSymbolGateResult.Verified, gate.Verify(runtime.Lease));

        Assert.Equal(loader.FreedModules.Count, loader.FreedModules.Distinct().Count());
        Assert.Equal(5, loader.FreedModules.Count);
    }

    [Fact]
    public void Verify_DisposedLeaseIsRejectedBeforeLoaderUse()
    {
        using var runtime = new FakeRuntime();
        var loader = new FakeLoader();
        runtime.Lease.Dispose();

        var result = CreateGate(loader).Verify(runtime.Lease);

        Assert.Equal(PlaybackDecoderFfmpegAbiSymbolGateResult.LeaseUnavailable, result);
        Assert.Empty(loader.LoadedModules);
    }

    [Fact]
    public void Verify_ResultContainsNoPathHashOrDllContent()
    {
        using var runtime = new FakeRuntime();
        var loader = new FakeLoader { MissingSymbol = "av_read_frame" };

        var result = CreateGate(loader).Verify(runtime.Lease);
        var diagnostic = result.ToString();

        Assert.Equal(PlaybackDecoderFfmpegAbiSymbolGateResult.SymbolMissing, result);
        Assert.DoesNotContain(runtime.DirectoryPath, diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("av_read_frame", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("SHA", diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.GetType().GetProperties());
    }

    [Fact]
    public void Verify_DoesNotUseCurrentDirectoryOrPathSearchScope()
    {
        using var runtime = new FakeRuntime();
        var loader = new FakeLoader();
        var originalDirectory = Environment.CurrentDirectory;
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.CurrentDirectory = runtime.TrapDirectoryPath;
            Environment.SetEnvironmentVariable("PATH", runtime.TrapDirectoryPath);

            var result = CreateGate(loader).Verify(runtime.Lease);

            Assert.Equal(PlaybackDecoderFfmpegAbiSymbolGateResult.Verified, result);
            Assert.All(loader.SearchScopes, static scope => Assert.Equal(
                PlaybackDecoderNativeSearchScope.VerifiedDirectoryAndSystem32,
                scope));
            Assert.DoesNotContain(
                loader.LoadedPaths,
                path => path.StartsWith(
                    Environment.CurrentDirectory,
                    StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    [NonWindowsFact]
    public void Verify_NonWindowsFailsClosedWithoutTouchingLeaseOrLoader()
    {
        using var runtime = new FakeRuntime();
        var gate = new PlaybackDecoderFfmpegAbiSymbolGate();

        var result = gate.Verify(runtime.Lease);

        Assert.Equal(PlaybackDecoderFfmpegAbiSymbolGateResult.Unsupported, result);
        Assert.False(runtime.Lease.IsDisposed);
    }

    private static PlaybackDecoderFfmpegAbiSymbolGate CreateGate(
        FakeLoader loader,
        FakeInspector? inspector = null) =>
        new(loader, inspector ?? new FakeInspector(), isSupportedPlatform: true);

    private sealed class FakeInspector : IPlaybackDecoderNativeDependencyInspector
    {
        public IReadOnlyList<string> Dependencies { get; init; } =
            ["kernel32.dll", "avutil-60.dll"];

        public int CallCount { get; private set; }

        public bool TryGetDependencies(
            Stream image,
            out IReadOnlyList<string> dependencies)
        {
            CallCount++;
            dependencies = Dependencies;
            return true;
        }
    }

    private sealed class FakeLoader : IPlaybackDecoderNativeLibraryLoader
    {
        private readonly Dictionary<nint, string> _moduleNames = [];
        private readonly Dictionary<nint, string> _symbolNames = [];
        private int _loadCount;
        private long _nextHandle = 100;

        public int? FailingLoad { get; init; }

        public string? MissingSymbol { get; init; }

        public Dictionary<string, uint> VersionMajorOverrides { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public List<string> LoadedNames { get; } = [];

        public List<string> LoadedPaths { get; } = [];

        public List<PlaybackDecoderNativeSearchScope> SearchScopes { get; } = [];

        public List<nint> LoadedModules { get; } = [];

        public List<nint> FreedModules { get; } = [];

        public nint Load(string verifiedPath, PlaybackDecoderNativeSearchScope searchScope)
        {
            _loadCount++;
            if (_loadCount == FailingLoad)
            {
                return 0;
            }

            var module = (nint)_nextHandle++;
            var name = Path.GetFileName(verifiedPath);
            _moduleNames.Add(module, name);
            LoadedNames.Add(name);
            LoadedPaths.Add(verifiedPath);
            SearchScopes.Add(searchScope);
            LoadedModules.Add(module);
            return module;
        }

        public nint GetSymbol(nint module, string symbol)
        {
            if (string.Equals(symbol, MissingSymbol, StringComparison.Ordinal))
            {
                return 0;
            }

            var address = (nint)_nextHandle++;
            _symbolNames.Add(address, symbol);
            _moduleNames.Add(address, _moduleNames[module]);
            return address;
        }

        public uint InvokeVersion(nint symbol)
        {
            var moduleName = _moduleNames[symbol];
            var major = VersionMajorOverrides.TryGetValue(moduleName, out var value)
                ? value
                : ParseMajor(moduleName);
            return major << 16;
        }

        public void Free(nint module)
        {
            Assert.DoesNotContain(module, FreedModules);
            FreedModules.Add(module);
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

    private sealed class FakeRuntime : IDisposable
    {
        private readonly Dictionary<string, FileStream> _streams =
            new(StringComparer.OrdinalIgnoreCase);

        public FakeRuntime(params string[]? fileNames)
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            TrapDirectoryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            Directory.CreateDirectory(TrapDirectoryPath);
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fileName in fileNames is { Length: > 0 }
                ? fileNames
                : RuntimeFiles)
            {
                var path = Path.Combine(DirectoryPath, fileName);
                File.WriteAllBytes(path, [1, 2, 3, 4]);
                var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                _streams.Add(fileName, stream);
                paths.Add(fileName, path);
            }

            Lease = new VerifiedPlaybackDecoderRuntimeLease(_streams, paths, []);
        }

        public string DirectoryPath { get; }

        public string TrapDirectoryPath { get; }

        public VerifiedPlaybackDecoderRuntimeLease Lease { get; }

        public void Dispose()
        {
            Lease.Dispose();
            Directory.Delete(DirectoryPath, recursive: true);
            Directory.Delete(TrapDirectoryPath, recursive: true);
        }
    }
}
