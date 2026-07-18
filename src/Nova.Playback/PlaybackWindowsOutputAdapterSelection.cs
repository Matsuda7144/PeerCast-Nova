using System.Reflection;
using System.Runtime.InteropServices;

namespace PeerCast.Nova.Playback;

public enum PlaybackWindowsVideoAdapter
{
    Direct3D11,
}

public enum PlaybackWindowsAudioAdapter
{
    WasapiSharedEventDriven,
}

public enum PlaybackWindowsOutputPlatformResult
{
    Windows11,
    Unsupported,
}

public enum PlaybackWindowsOutputApiResult
{
    Available,
    ApiUnavailable,
    DeviceUnavailable,
    FormatUnsupported,
    PermissionDenied,
    InvalidEnvironment,
}

public enum PlaybackWindowsOutputProbeResult
{
    Available,
    PlatformUnsupported,
    ApiUnavailable,
    DeviceUnavailable,
    FormatUnsupported,
    PermissionDenied,
    InvalidEnvironment,
    Disposed,
    Failure,
}

public readonly record struct PlaybackWindowsOutputAdapterSelection(
    PlaybackWindowsVideoAdapter Video,
    PlaybackWindowsAudioAdapter Audio)
{
    public static PlaybackWindowsOutputAdapterSelection Windows11Default { get; } =
        new(
            PlaybackWindowsVideoAdapter.Direct3D11,
            PlaybackWindowsAudioAdapter.WasapiSharedEventDriven);
}

public readonly record struct PlaybackWindowsOutputProbeReport(
    PlaybackWindowsOutputProbeResult Result,
    PlaybackWindowsOutputAdapterSelection Selection);

/// <summary>
/// Resource-free probe seam. Implementations must not create a window, device,
/// swap chain, audio client, or audio stream, and must release temporary OS
/// resources before returning.
/// </summary>
public interface IPlaybackWindowsOutputAvailabilitySource
{
    PlaybackWindowsOutputPlatformResult ProbePlatform();

    PlaybackWindowsOutputApiResult ProbeVideo(
        PlaybackWindowsVideoAdapter adapter,
        PlaybackDecodedVideoPixelFormat format);

    PlaybackWindowsOutputApiResult ProbeAudio(
        PlaybackWindowsAudioAdapter adapter,
        PlaybackDecodedAudioSampleFormat format);
}

public interface IPlaybackWindowsNativeLibraryInspector
{
    bool TryLoadSystemLibrary(string libraryName, out nint handle);

    bool HasExport(nint handle, string exportName);

    void Free(nint handle);
}

public sealed class PlaybackWindowsOutputAdapterProbe : IDisposable
{
    private readonly IPlaybackWindowsOutputAvailabilitySource _source;
    private int _isDisposed;

    public PlaybackWindowsOutputAdapterProbe()
        : this(new SystemPlaybackWindowsOutputAvailabilitySource())
    {
    }

    public PlaybackWindowsOutputAdapterProbe(
        IPlaybackWindowsOutputAvailabilitySource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    public PlaybackWindowsOutputProbeReport Probe(
        PlaybackDecodedVideoPixelFormat videoFormat,
        PlaybackDecodedAudioSampleFormat audioFormat)
    {
        var selection = PlaybackWindowsOutputAdapterSelection.Windows11Default;
        if (Volatile.Read(ref _isDisposed) != 0)
        {
            return new(PlaybackWindowsOutputProbeResult.Disposed, selection);
        }

        if (videoFormat != PlaybackDecodedVideoPixelFormat.Bgra32 ||
            !Enum.IsDefined(videoFormat) ||
            audioFormat is not (
                PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved or
                PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved) ||
            !Enum.IsDefined(audioFormat))
        {
            return new(PlaybackWindowsOutputProbeResult.FormatUnsupported, selection);
        }

        try
        {
            var platform = _source.ProbePlatform();
            if (!Enum.IsDefined(platform))
            {
                return new(PlaybackWindowsOutputProbeResult.Failure, selection);
            }

            if (platform == PlaybackWindowsOutputPlatformResult.Unsupported)
            {
                return new(PlaybackWindowsOutputProbeResult.PlatformUnsupported, selection);
            }

            var video = _source.ProbeVideo(selection.Video, videoFormat);
            if (!Enum.IsDefined(video))
            {
                return new(PlaybackWindowsOutputProbeResult.Failure, selection);
            }

            if (video != PlaybackWindowsOutputApiResult.Available)
            {
                return new(Map(video), selection);
            }

            var audio = _source.ProbeAudio(selection.Audio, audioFormat);
            return !Enum.IsDefined(audio)
                ? new(PlaybackWindowsOutputProbeResult.Failure, selection)
                : new(Map(audio), selection);
        }
        catch
        {
            return new(PlaybackWindowsOutputProbeResult.Failure, selection);
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _isDisposed, 1);
        GC.SuppressFinalize(this);
    }

    private static PlaybackWindowsOutputProbeResult Map(
        PlaybackWindowsOutputApiResult value) =>
        value switch
        {
            PlaybackWindowsOutputApiResult.Available =>
                PlaybackWindowsOutputProbeResult.Available,
            PlaybackWindowsOutputApiResult.ApiUnavailable =>
                PlaybackWindowsOutputProbeResult.ApiUnavailable,
            PlaybackWindowsOutputApiResult.DeviceUnavailable =>
                PlaybackWindowsOutputProbeResult.DeviceUnavailable,
            PlaybackWindowsOutputApiResult.FormatUnsupported =>
                PlaybackWindowsOutputProbeResult.FormatUnsupported,
            PlaybackWindowsOutputApiResult.PermissionDenied =>
                PlaybackWindowsOutputProbeResult.PermissionDenied,
            PlaybackWindowsOutputApiResult.InvalidEnvironment =>
                PlaybackWindowsOutputProbeResult.InvalidEnvironment,
            _ => PlaybackWindowsOutputProbeResult.Failure,
        };
}

public sealed class SystemPlaybackWindowsOutputAvailabilitySource :
    IPlaybackWindowsOutputAvailabilitySource
{
    private static readonly LibraryRequirement[] VideoRequirements =
    [
        new("d3d11.dll", "D3D11CreateDevice"),
        new("dxgi.dll", "CreateDXGIFactory1"),
    ];

    private static readonly LibraryRequirement[] AudioRequirements =
    [
        new("ole32.dll", "CoCreateInstance"),
        new("kernel32.dll", "CreateEventW"),
    ];

    private readonly Func<bool> _isWindows11OrLater;
    private readonly IPlaybackWindowsNativeLibraryInspector _inspector;

    public SystemPlaybackWindowsOutputAvailabilitySource()
        : this(
            new PlaybackWindowsNativeLibraryInspector(),
            static () => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22_000))
    {
    }

    public SystemPlaybackWindowsOutputAvailabilitySource(
        IPlaybackWindowsNativeLibraryInspector inspector,
        Func<bool> isWindows11OrLater)
    {
        ArgumentNullException.ThrowIfNull(inspector);
        ArgumentNullException.ThrowIfNull(isWindows11OrLater);
        _inspector = inspector;
        _isWindows11OrLater = isWindows11OrLater;
    }

    public PlaybackWindowsOutputPlatformResult ProbePlatform() =>
        _isWindows11OrLater()
            ? PlaybackWindowsOutputPlatformResult.Windows11
            : PlaybackWindowsOutputPlatformResult.Unsupported;

    public PlaybackWindowsOutputApiResult ProbeVideo(
        PlaybackWindowsVideoAdapter adapter,
        PlaybackDecodedVideoPixelFormat format) =>
        adapter != PlaybackWindowsVideoAdapter.Direct3D11 ||
        format != PlaybackDecodedVideoPixelFormat.Bgra32
            ? PlaybackWindowsOutputApiResult.FormatUnsupported
            : ProbeRequirements(VideoRequirements);

    public PlaybackWindowsOutputApiResult ProbeAudio(
        PlaybackWindowsAudioAdapter adapter,
        PlaybackDecodedAudioSampleFormat format) =>
        adapter != PlaybackWindowsAudioAdapter.WasapiSharedEventDriven ||
        format is not (
            PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved or
            PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved)
            ? PlaybackWindowsOutputApiResult.FormatUnsupported
            : ProbeRequirements(AudioRequirements);

    private PlaybackWindowsOutputApiResult ProbeRequirements(
        IReadOnlyList<LibraryRequirement> requirements)
    {
        foreach (var requirement in requirements)
        {
            nint handle = 0;
            try
            {
                if (!_inspector.TryLoadSystemLibrary(requirement.LibraryName, out handle) ||
                    handle == 0 ||
                    !_inspector.HasExport(handle, requirement.ExportName))
                {
                    return PlaybackWindowsOutputApiResult.ApiUnavailable;
                }
            }
            finally
            {
                if (handle != 0)
                {
                    _inspector.Free(handle);
                }
            }
        }

        return PlaybackWindowsOutputApiResult.Available;
    }

    private readonly record struct LibraryRequirement(
        string LibraryName,
        string ExportName);
}

public sealed class PlaybackWindowsNativeLibraryInspector :
    IPlaybackWindowsNativeLibraryInspector
{
    private static readonly Assembly PlaybackAssembly =
        typeof(PlaybackWindowsNativeLibraryInspector).Assembly;

    public bool TryLoadSystemLibrary(string libraryName, out nint handle) =>
        NativeLibrary.TryLoad(
            libraryName,
            PlaybackAssembly,
            DllImportSearchPath.System32,
            out handle);

    public bool HasExport(nint handle, string exportName) =>
        NativeLibrary.TryGetExport(handle, exportName, out _);

    public void Free(nint handle) => NativeLibrary.Free(handle);
}

public static class PlaybackWindowsOutputHResultMapper
{
    private const int AccessDenied = unchecked((int)0x80070005);
    private const int InvalidArgument = unchecked((int)0x80070057);
    private const int DxgiNotFound = unchecked((int)0x887A0002);
    private const int DxgiUnsupported = unchecked((int)0x887A0004);
    private const int AudioClientDeviceInvalidated = unchecked((int)0x88890004);
    private const int AudioClientUnsupportedFormat = unchecked((int)0x88890008);

    public static PlaybackWindowsOutputProbeResult Map(int hresult) =>
        hresult switch
        {
            0 => PlaybackWindowsOutputProbeResult.Available,
            AccessDenied => PlaybackWindowsOutputProbeResult.PermissionDenied,
            InvalidArgument => PlaybackWindowsOutputProbeResult.InvalidEnvironment,
            DxgiNotFound or AudioClientDeviceInvalidated =>
                PlaybackWindowsOutputProbeResult.DeviceUnavailable,
            DxgiUnsupported => PlaybackWindowsOutputProbeResult.ApiUnavailable,
            AudioClientUnsupportedFormat =>
                PlaybackWindowsOutputProbeResult.FormatUnsupported,
            _ => PlaybackWindowsOutputProbeResult.Failure,
        };
}
