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
        ArgumentNullException.ThrowIfNull(lease);
        if (!_isSupportedPlatform)
        {
            return PlaybackDecoderFfmpegAbiSymbolGateResult.Unsupported;
        }

        try
        {
            return lease.UseVerifiedRuntime(VerifyGuardedRuntime);
        }
        catch (ObjectDisposedException)
        {
            return PlaybackDecoderFfmpegAbiSymbolGateResult.LeaseUnavailable;
        }
    }

    private PlaybackDecoderFfmpegAbiSymbolGateResult VerifyGuardedRuntime(
        IReadOnlyDictionary<string, VerifiedPlaybackDecoderRuntimeFile> runtime)
    {
        if (runtime.Count != Requirements.Length ||
            Requirements.Any(requirement => !runtime.ContainsKey(requirement.FileName)))
        {
            return PlaybackDecoderFfmpegAbiSymbolGateResult.RuntimeMismatch;
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
                    return PlaybackDecoderFfmpegAbiSymbolGateResult.DependencyRejected;
                }
            }
            catch (Exception exception) when (exception is IOException or NotSupportedException)
            {
                return PlaybackDecoderFfmpegAbiSymbolGateResult.DependencyRejected;
            }
        }

        var loaded = new List<nint>(Requirements.Length);
        var result = PlaybackDecoderFfmpegAbiSymbolGateResult.Verified;
        var releaseFailed = false;
        try
        {
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
        }
        finally
        {
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
        }

        return releaseFailed
            ? PlaybackDecoderFfmpegAbiSymbolGateResult.ReleaseFailed
            : result;
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
