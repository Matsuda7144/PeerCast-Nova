using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PeerCast.Nova.Playback;

public enum PlaybackWindowsOutputActivationResult
{
    Activated,
    PlatformUnsupported,
    ApiUnavailable,
    DeviceUnavailable,
    PermissionDenied,
    InvalidEnvironment,
    Failure,
    Disposed,
}

public sealed class PlaybackWindowsOutputActivationBoundary : IDisposable
{
    private readonly IPlaybackWindowsOutputNativeActivation _native;
    private readonly Func<bool> _isSupportedPlatform;
    private readonly List<IDisposable> _ownedResources = [];
    private bool _isActivated;
    private bool _isDisposed;

    public PlaybackWindowsOutputActivationBoundary()
        : this(CreateSystemNativeActivation(),
            static () => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22_000))
    {
    }

    private static IPlaybackWindowsOutputNativeActivation CreateSystemNativeActivation() =>
        OperatingSystem.IsWindows()
            ? new SystemPlaybackWindowsOutputNativeActivation()
            : new UnsupportedPlaybackWindowsOutputNativeActivation();

    internal PlaybackWindowsOutputActivationBoundary(
        IPlaybackWindowsOutputNativeActivation native,
        Func<bool> isSupportedPlatform)
    {
        ArgumentNullException.ThrowIfNull(native);
        ArgumentNullException.ThrowIfNull(isSupportedPlatform);
        _native = native;
        _isSupportedPlatform = isSupportedPlatform;
    }

    public PlaybackWindowsOutputActivationResult Activate()
    {
        if (_isDisposed)
        {
            return PlaybackWindowsOutputActivationResult.Disposed;
        }

        if (_isActivated)
        {
            return PlaybackWindowsOutputActivationResult.Activated;
        }

        try
        {
            if (!_isSupportedPlatform())
            {
                return PlaybackWindowsOutputActivationResult.PlatformUnsupported;
            }

            var result = Acquire(_native.InitializeComApartment);
            if (result != PlaybackWindowsOutputActivationResult.Activated)
            {
                ReleaseOwnedResources();
                return result;
            }

            result = Acquire(_native.ActivateDirect3D11);
            if (result != PlaybackWindowsOutputActivationResult.Activated)
            {
                ReleaseOwnedResources();
                return result;
            }

            result = Acquire(_native.ActivateWasapiSharedEventDriven);
            if (result != PlaybackWindowsOutputActivationResult.Activated)
            {
                ReleaseOwnedResources();
                return result;
            }

            _isActivated = true;
            return PlaybackWindowsOutputActivationResult.Activated;
        }
        catch
        {
            ReleaseOwnedResources();
            return PlaybackWindowsOutputActivationResult.Failure;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _isActivated = false;
        ReleaseOwnedResources();
    }

    private PlaybackWindowsOutputActivationResult Acquire(
        NativeActivationStep activation)
    {
        var result = activation(out var resource);
        if (!Enum.IsDefined(result))
        {
            resource?.Dispose();
            return PlaybackWindowsOutputActivationResult.Failure;
        }

        if (result == PlaybackWindowsOutputActivationResult.Activated)
        {
            if (resource is null)
            {
                return PlaybackWindowsOutputActivationResult.Failure;
            }

            _ownedResources.Add(resource);
            return result;
        }

        resource?.Dispose();
        return result is PlaybackWindowsOutputActivationResult.PlatformUnsupported or
            PlaybackWindowsOutputActivationResult.Disposed
            ? PlaybackWindowsOutputActivationResult.Failure
            : result;
    }

    private void ReleaseOwnedResources()
    {
        for (var index = _ownedResources.Count - 1; index >= 0; index--)
        {
            try
            {
                _ownedResources[index].Dispose();
            }
            catch
            {
                // Cleanup is best-effort and never changes the fixed public result.
            }
        }

        _ownedResources.Clear();
    }

    internal delegate PlaybackWindowsOutputActivationResult NativeActivationStep(
        out IDisposable? resource);
}

internal interface IPlaybackWindowsOutputNativeActivation
{
    PlaybackWindowsOutputActivationResult InitializeComApartment(out IDisposable? apartment);
    PlaybackWindowsOutputActivationResult ActivateDirect3D11(out IDisposable? device);
    PlaybackWindowsOutputActivationResult ActivateWasapiSharedEventDriven(out IDisposable? audio);
}

internal sealed class UnsupportedPlaybackWindowsOutputNativeActivation :
    IPlaybackWindowsOutputNativeActivation
{
    public PlaybackWindowsOutputActivationResult InitializeComApartment(
        out IDisposable? apartment) => Unsupported(out apartment);

    public PlaybackWindowsOutputActivationResult ActivateDirect3D11(
        out IDisposable? device) => Unsupported(out device);

    public PlaybackWindowsOutputActivationResult ActivateWasapiSharedEventDriven(
        out IDisposable? audio) => Unsupported(out audio);

    private static PlaybackWindowsOutputActivationResult Unsupported(
        out IDisposable? resource)
    {
        resource = null;
        return PlaybackWindowsOutputActivationResult.PlatformUnsupported;
    }
}

[SupportedOSPlatform("windows")]
internal sealed class SystemPlaybackWindowsOutputNativeActivation :
    IPlaybackWindowsOutputNativeActivation
{
    private const uint CoinitMultithreaded = 0;
    private const uint D3DDriverTypeHardware = 1;
    private const uint D3D11SdkVersion = 7;
    private const uint ClsctxInprocServer = 1;
    private const uint ERender = 0;
    private const uint EConsole = 0;
    private static readonly Guid MmDeviceEnumeratorClass =
        new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid MmDeviceEnumeratorInterface =
        new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid AudioClientInterface =
        new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");

    public PlaybackWindowsOutputActivationResult InitializeComApartment(
        out IDisposable? apartment)
    {
        apartment = null;
        var hresult = CoInitializeEx(0, CoinitMultithreaded);
        if (hresult < 0)
        {
            return Map(hresult);
        }

        apartment = new ActionResource(CoUninitialize);
        return PlaybackWindowsOutputActivationResult.Activated;
    }

    public PlaybackWindowsOutputActivationResult ActivateDirect3D11(
        out IDisposable? device)
    {
        device = null;
        nint devicePointer = 0;
        nint contextPointer = 0;
        try
        {
            var hresult = D3D11CreateDevice(0, D3DDriverTypeHardware, 0, 0, 0, 0,
                D3D11SdkVersion, out devicePointer, out _, out contextPointer);
            if (hresult < 0)
            {
                return Map(hresult);
            }

            if (devicePointer == 0 || contextPointer == 0)
            {
                return PlaybackWindowsOutputActivationResult.Failure;
            }

            var ownedDevice = devicePointer;
            var ownedContext = contextPointer;
            device = new ActionResource(() =>
            {
                Marshal.Release(ownedContext);
                Marshal.Release(ownedDevice);
            });
            devicePointer = 0;
            contextPointer = 0;
            return PlaybackWindowsOutputActivationResult.Activated;
        }
        finally
        {
            if (contextPointer != 0) Marshal.Release(contextPointer);
            if (devicePointer != 0) Marshal.Release(devicePointer);
        }
    }

    public PlaybackWindowsOutputActivationResult ActivateWasapiSharedEventDriven(
        out IDisposable? audio)
    {
        audio = null;
        nint enumerator = 0;
        nint endpoint = 0;
        nint client = 0;
        nint eventHandle = 0;
        try
        {
            var classId = MmDeviceEnumeratorClass;
            var enumeratorId = MmDeviceEnumeratorInterface;
            var hresult = CoCreateInstance(ref classId, 0, ClsctxInprocServer,
                ref enumeratorId, out enumerator);
            if (hresult < 0) return Map(hresult);

            var enumeratorObject = (IMMDeviceEnumerator)Marshal.GetObjectForIUnknown(enumerator);
            hresult = enumeratorObject.GetDefaultAudioEndpoint(ERender, EConsole, out endpoint);
            Marshal.FinalReleaseComObject(enumeratorObject);
            if (hresult < 0) return Map(hresult);

            var endpointObject = (IMMDevice)Marshal.GetObjectForIUnknown(endpoint);
            var audioClientId = AudioClientInterface;
            hresult = endpointObject.Activate(ref audioClientId, ClsctxInprocServer, 0,
                out client);
            Marshal.FinalReleaseComObject(endpointObject);
            if (hresult < 0) return Map(hresult);

            eventHandle = CreateEventW(0, false, false, null);
            if (eventHandle == 0)
            {
                return Map(Marshal.GetHRForLastWin32Error());
            }

            var ownedEnumerator = enumerator;
            var ownedEndpoint = endpoint;
            var ownedClient = client;
            var ownedEventHandle = eventHandle;
            audio = new ActionResource(() =>
            {
                CloseHandle(ownedEventHandle);
                Marshal.Release(ownedClient);
                Marshal.Release(ownedEndpoint);
                Marshal.Release(ownedEnumerator);
            });
            enumerator = endpoint = client = eventHandle = 0;
            return PlaybackWindowsOutputActivationResult.Activated;
        }
        finally
        {
            if (eventHandle != 0) CloseHandle(eventHandle);
            if (client != 0) Marshal.Release(client);
            if (endpoint != 0) Marshal.Release(endpoint);
            if (enumerator != 0) Marshal.Release(enumerator);
        }
    }

    private static PlaybackWindowsOutputActivationResult Map(int hresult) =>
        PlaybackWindowsOutputHResultMapper.Map(hresult) switch
        {
            PlaybackWindowsOutputProbeResult.Available =>
                PlaybackWindowsOutputActivationResult.Activated,
            PlaybackWindowsOutputProbeResult.ApiUnavailable =>
                PlaybackWindowsOutputActivationResult.ApiUnavailable,
            PlaybackWindowsOutputProbeResult.DeviceUnavailable =>
                PlaybackWindowsOutputActivationResult.DeviceUnavailable,
            PlaybackWindowsOutputProbeResult.PermissionDenied =>
                PlaybackWindowsOutputActivationResult.PermissionDenied,
            PlaybackWindowsOutputProbeResult.InvalidEnvironment =>
                PlaybackWindowsOutputActivationResult.InvalidEnvironment,
            _ => PlaybackWindowsOutputActivationResult.Failure,
        };

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint reserved, uint concurrencyModel);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(ref Guid classId, nint outer,
        uint context, ref Guid interfaceId, out nint instance);

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(nint adapter, uint driverType,
        nint software, uint flags, nint featureLevels, uint featureLevelCount,
        uint sdkVersion, out nint device, out uint featureLevel,
        out nint immediateContext);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateEventW(nint attributes,
        [MarshalAs(UnmanagedType.Bool)] bool manualReset,
        [MarshalAs(UnmanagedType.Bool)] bool initialState, string? name);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(uint dataFlow, uint stateMask, out nint devices);
        [PreserveSig] int GetDefaultAudioEndpoint(uint dataFlow, uint role, out nint endpoint);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid interfaceId, uint context,
            nint activationParameters, out nint instance);
    }

    private sealed class ActionResource(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
