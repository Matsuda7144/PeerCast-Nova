using System.Runtime.InteropServices;

namespace PeerCast.Nova.Playback;

public enum PlaybackDecoderFfmpegAbiSymbolGateResult
{
    Verified,
    Unsupported,
    LeaseUnavailable,
    RuntimeMismatch,
    DependencyRejected,
    ModuleLoadFailed,
    SymbolMissing,
    VersionMismatch,
    ReleaseFailed,
}

internal enum PlaybackDecoderNativeSearchScope
{
    VerifiedDirectoryAndSystem32,
}

internal interface IPlaybackDecoderNativeLibraryLoader
{
    nint Load(string verifiedPath, PlaybackDecoderNativeSearchScope searchScope);

    nint GetSymbol(nint module, string symbol);

    uint InvokeVersion(nint symbol);

    void Free(nint module);
}

internal interface IPlaybackDecoderNativeDependencyInspector
{
    bool TryGetDependencies(Stream image, out IReadOnlyList<string> dependencies);
}

public sealed class PlaybackDecoderFfmpegAbiSymbolGate
{
    private static readonly ModuleRequirement[] Requirements =
    [
        new("avutil-60.dll", "avutil_version", 60,
            ["avutil_version", "av_version_info"]),
        new("swresample-6.dll", "swresample_version", 6,
            ["swresample_version", "swr_alloc", "swr_init", "swr_free"]),
        new("swscale-9.dll", "swscale_version", 9,
            ["swscale_version", "sws_getContext", "sws_scale", "sws_freeContext"]),
        new("avcodec-62.dll", "avcodec_version", 62,
            [
                "avcodec_version", "avcodec_find_decoder", "avcodec_alloc_context3",
                "avcodec_free_context", "avcodec_send_packet", "avcodec_receive_frame",
            ]),
        new("avformat-62.dll", "avformat_version", 62,
            [
                "avformat_version", "avformat_open_input", "avformat_find_stream_info",
                "av_read_frame", "avformat_close_input",
            ]),
    ];

    private static readonly HashSet<string> VerifiedRuntimeNames = Requirements
        .Select(static requirement => requirement.FileName)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> AllowedSystemDependencies = new(
        [
            "advapi32.dll", "bcrypt.dll", "cfgmgr32.dll", "combase.dll",
            "crypt32.dll", "dwmapi.dll", "gdi32.dll", "imm32.dll", "kernel32.dll",
            "mf.dll", "mfplat.dll", "mfuuid.dll", "ncrypt.dll", "ntdll.dll",
            "ole32.dll", "oleaut32.dll", "powrprof.dll", "propsys.dll",
            "rpcrt4.dll", "secur32.dll", "setupapi.dll", "shell32.dll",
            "shlwapi.dll", "user32.dll", "userenv.dll", "ucrtbase.dll",
            "version.dll", "winmm.dll", "ws2_32.dll",
        ],
        StringComparer.OrdinalIgnoreCase);

    private readonly IPlaybackDecoderNativeLibraryLoader _loader;
    private readonly IPlaybackDecoderNativeDependencyInspector _dependencyInspector;
    private readonly bool _isSupportedPlatform;

    public PlaybackDecoderFfmpegAbiSymbolGate()
        : this(
            new WindowsPlaybackDecoderNativeLibraryLoader(),
            new PortableExecutableDependencyInspector(),
            OperatingSystem.IsWindows())
    {
    }

    internal PlaybackDecoderFfmpegAbiSymbolGate(
        IPlaybackDecoderNativeLibraryLoader loader,
        IPlaybackDecoderNativeDependencyInspector dependencyInspector,
        bool isSupportedPlatform)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(dependencyInspector);
        _loader = loader;
        _dependencyInspector = dependencyInspector;
        _isSupportedPlatform = isSupportedPlatform;
    }

    public PlaybackDecoderFfmpegAbiSymbolGateResult Verify(
        VerifiedPlaybackDecoderRuntimeLease lease)
    {
        var acquisition = Acquire(lease);
        if (acquisition.Lease is null)
        {
            return acquisition.Result;
        }

        return acquisition.Lease.Release()
            ? PlaybackDecoderFfmpegAbiSymbolGateResult.Verified
            : PlaybackDecoderFfmpegAbiSymbolGateResult.ReleaseFailed;
    }

    public PlaybackDecoderFfmpegAbiSymbolGateLeaseResult Acquire(
        VerifiedPlaybackDecoderRuntimeLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!_isSupportedPlatform)
        {
            return new(PlaybackDecoderFfmpegAbiSymbolGateResult.Unsupported);
        }

        try
        {
            return lease.UseVerifiedRuntime(runtime => AcquireGuardedRuntime(lease, runtime));
        }
        catch (ObjectDisposedException)
        {
            return new(PlaybackDecoderFfmpegAbiSymbolGateResult.LeaseUnavailable);
        }
    }

    private PlaybackDecoderFfmpegAbiSymbolGateLeaseResult AcquireGuardedRuntime(
        VerifiedPlaybackDecoderRuntimeLease owner,
        IReadOnlyDictionary<string, VerifiedPlaybackDecoderRuntimeFile> runtime)
    {
        if (runtime.Count != Requirements.Length ||
            Requirements.Any(requirement => !runtime.ContainsKey(requirement.FileName)))
        {
            return new(PlaybackDecoderFfmpegAbiSymbolGateResult.RuntimeMismatch);
        }

        foreach (var requirement in Requirements)
        {
            var image = runtime[requirement.FileName].Stream;
            try
            {
                image.Position = 0;
                if (!_dependencyInspector.TryGetDependencies(image, out var dependencies) ||
                    dependencies.Any(static dependency => !IsAllowedDependency(dependency)))
                {
                    return new(PlaybackDecoderFfmpegAbiSymbolGateResult.DependencyRejected);
                }
            }
            catch (Exception exception) when (exception is IOException or NotSupportedException)
            {
                return new(PlaybackDecoderFfmpegAbiSymbolGateResult.DependencyRejected);
            }
        }

        var loaded = new List<nint>(Requirements.Length);
        var symbols = new Dictionary<string, nint>(StringComparer.Ordinal);
        var result = PlaybackDecoderFfmpegAbiSymbolGateResult.Verified;
        foreach (var requirement in Requirements)
        {
            nint module;
            try
            {
                module = _loader.Load(
                    runtime[requirement.FileName].Path,
                    PlaybackDecoderNativeSearchScope.VerifiedDirectoryAndSystem32);
            }
            catch (Exception exception) when (exception is
                InvalidOperationException or
                NotSupportedException or
                DllNotFoundException)
            {
                result = PlaybackDecoderFfmpegAbiSymbolGateResult.ModuleLoadFailed;
                break;
            }

            if (module == 0)
            {
                result = PlaybackDecoderFfmpegAbiSymbolGateResult.ModuleLoadFailed;
                break;
            }

            loaded.Add(module);
            nint versionSymbol = 0;
            foreach (var symbol in requirement.RequiredSymbols)
            {
                nint address;
                try
                {
                    address = _loader.GetSymbol(module, symbol);
                }
                catch (Exception exception) when (exception is InvalidOperationException)
                {
                    address = 0;
                }

                if (address == 0)
                {
                    result = PlaybackDecoderFfmpegAbiSymbolGateResult.SymbolMissing;
                    break;
                }

                symbols.TryAdd(symbol, address);
                if (string.Equals(
                    symbol,
                    requirement.VersionSymbol,
                    StringComparison.Ordinal))
                {
                    versionSymbol = address;
                }
            }

            if (result != PlaybackDecoderFfmpegAbiSymbolGateResult.Verified)
            {
                break;
            }

            uint version;
            try
            {
                version = _loader.InvokeVersion(versionSymbol);
            }
            catch (Exception exception) when (exception is InvalidOperationException)
            {
                result = PlaybackDecoderFfmpegAbiSymbolGateResult.VersionMismatch;
                break;
            }

            if (((version >> 16) & 0xff) != requirement.ExpectedMajor)
            {
                result = PlaybackDecoderFfmpegAbiSymbolGateResult.VersionMismatch;
                break;
            }
        }

        if (result == PlaybackDecoderFfmpegAbiSymbolGateResult.Verified)
        {
            return new(
                result,
                new PlaybackDecoderFfmpegAbiSymbolGateLease(
                    owner,
                    _loader,
                    loaded,
                    symbols));
        }

        var releaseFailed = false;
        for (var index = loaded.Count - 1; index >= 0; index--)
        {
            try
            {
                _loader.Free(loaded[index]);
            }
            catch (Exception exception) when (exception is InvalidOperationException)
            {
                releaseFailed = true;
            }
        }

        return new(releaseFailed
            ? PlaybackDecoderFfmpegAbiSymbolGateResult.ReleaseFailed
            : result);
    }

    private static bool IsAllowedDependency(string dependency)
    {
        if (string.IsNullOrWhiteSpace(dependency) ||
            dependency.Length > 128 ||
            dependency.Any(static character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')))
        {
            return false;
        }

        return VerifiedRuntimeNames.Contains(dependency) ||
            AllowedSystemDependencies.Contains(dependency) ||
            dependency.StartsWith("api-ms-win-", StringComparison.OrdinalIgnoreCase) ||
            dependency.StartsWith("ext-ms-win-", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ModuleRequirement(
        string FileName,
        string VersionSymbol,
        uint ExpectedMajor,
        string[] RequiredSymbols);
}

public sealed class PlaybackDecoderFfmpegAbiSymbolGateLeaseResult
{
    internal PlaybackDecoderFfmpegAbiSymbolGateLeaseResult(
        PlaybackDecoderFfmpegAbiSymbolGateResult result,
        PlaybackDecoderFfmpegAbiSymbolGateLease? lease = null)
    {
        if (!Enum.IsDefined(result))
        {
            throw new ArgumentException(
                "The native gate result category is undefined.",
                nameof(result));
        }

        if ((result == PlaybackDecoderFfmpegAbiSymbolGateResult.Verified) !=
            (lease is not null))
        {
            throw new ArgumentException(
                "The native gate result is inconsistent.",
                nameof(lease));
        }

        Result = result;
        Lease = lease;
    }

    public PlaybackDecoderFfmpegAbiSymbolGateResult Result { get; }

    public PlaybackDecoderFfmpegAbiSymbolGateLease? Lease { get; }
}

public sealed class PlaybackDecoderFfmpegAbiSymbolGateLease : IDisposable
{
    private readonly object _sync = new();
    private readonly VerifiedPlaybackDecoderRuntimeLease _runtimeLease;
    private IPlaybackDecoderNativeLibraryLoader? _loader;
    private List<nint>? _modules;
    private Dictionary<string, nint>? _symbols;

    internal PlaybackDecoderFfmpegAbiSymbolGateLease(
        VerifiedPlaybackDecoderRuntimeLease runtimeLease,
        IPlaybackDecoderNativeLibraryLoader loader,
        List<nint> modules,
        Dictionary<string, nint> symbols)
    {
        _runtimeLease = runtimeLease;
        _loader = loader;
        _modules = modules;
        _symbols = symbols;
    }

    public bool IsDisposed
    {
        get
        {
            lock (_sync)
            {
                return _modules is null;
            }
        }
    }

    internal VerifiedPlaybackDecoderRuntimeLease RuntimeLease => _runtimeLease;

    internal TResult UseSymbols<TResult>(Func<IReadOnlyDictionary<string, nint>, TResult> use)
    {
        ArgumentNullException.ThrowIfNull(use);
        lock (_sync)
        {
            if (_modules is null || _symbols is null || _runtimeLease.IsDisposed)
            {
                throw new ObjectDisposedException(
                    nameof(PlaybackDecoderFfmpegAbiSymbolGateLease),
                    "The verified native gate lease is no longer available.");
            }

            return use(_symbols);
        }
    }

    internal bool Release()
    {
        IPlaybackDecoderNativeLibraryLoader? loader;
        List<nint>? modules;
        lock (_sync)
        {
            loader = _loader;
            modules = _modules;
            _loader = null;
            _modules = null;
            _symbols = null;
        }

        if (loader is null || modules is null)
        {
            return true;
        }

        var succeeded = true;
        for (var index = modules.Count - 1; index >= 0; index--)
        {
            try
            {
                loader.Free(modules[index]);
            }
            catch (Exception exception) when (exception is InvalidOperationException)
            {
                succeeded = false;
            }
        }

        return succeeded;
    }

    public void Dispose() => Release();
}

internal sealed class WindowsPlaybackDecoderNativeLibraryLoader :
    IPlaybackDecoderNativeLibraryLoader
{
    private const uint LoadLibrarySearchDllLoadDir = 0x00000100;
    private const uint LoadLibrarySearchSystem32 = 0x00000800;

    public nint Load(string verifiedPath, PlaybackDecoderNativeSearchScope searchScope)
    {
        if (!OperatingSystem.IsWindows() ||
            searchScope != PlaybackDecoderNativeSearchScope.VerifiedDirectoryAndSystem32)
        {
            throw new NotSupportedException("The native loader boundary is unavailable.");
        }

        return LoadLibraryExW(
            verifiedPath,
            0,
            LoadLibrarySearchDllLoadDir | LoadLibrarySearchSystem32);
    }

    public nint GetSymbol(nint module, string symbol) => GetProcAddress(module, symbol);

    public uint InvokeVersion(nint symbol) =>
        Marshal.GetDelegateForFunctionPointer<VersionFunction>(symbol)();

    public void Free(nint module)
    {
        if (!FreeLibrary(module))
        {
            throw new InvalidOperationException("A native module could not be released.");
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint VersionFunction();

    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", SetLastError = true,
        CharSet = CharSet.Unicode)]
    private static extern nint LoadLibraryExW(string fileName, nint file, uint flags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi,
        ExactSpelling = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern nint GetProcAddress(
        nint module,
        [MarshalAs(UnmanagedType.LPStr)] string symbol);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(nint module);
}
