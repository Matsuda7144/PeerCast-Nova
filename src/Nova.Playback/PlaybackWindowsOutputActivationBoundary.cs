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
    private readonly object _sync = new();
    private readonly IPlaybackWindowsOutputNativeActivation _native;
    private readonly Func<bool> _isSupportedPlatform;
    private readonly bool _enforceOwningThread;
    private readonly List<IDisposable> _ownedResources = [];
    private bool _isActivated;
    private bool _isDisposed;
    private int _owningThreadId;

    public PlaybackWindowsOutputActivationBoundary()
        : this(CreateSystemNativeActivation(),
            static () => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22_000),
            enforceOwningThread: true)
    {
    }

    private static IPlaybackWindowsOutputNativeActivation CreateSystemNativeActivation() =>
        OperatingSystem.IsWindows()
            ? new SystemPlaybackWindowsOutputNativeActivation()
            : new UnsupportedPlaybackWindowsOutputNativeActivation();

    internal PlaybackWindowsOutputActivationBoundary(
        IPlaybackWindowsOutputNativeActivation native,
        Func<bool> isSupportedPlatform)
        : this(native, isSupportedPlatform, enforceOwningThread: false)
    {
    }

    internal PlaybackWindowsOutputActivationBoundary(
        IPlaybackWindowsOutputNativeActivation native,
        Func<bool> isSupportedPlatform,
        bool enforceOwningThread)
    {
        ArgumentNullException.ThrowIfNull(native);
        ArgumentNullException.ThrowIfNull(isSupportedPlatform);
        _native = native;
        _isSupportedPlatform = isSupportedPlatform;
        _enforceOwningThread = enforceOwningThread;
    }

    public PlaybackWindowsOutputActivationResult Activate()
    {
        lock (_sync)
        {
            return ActivateCore();
        }
    }

    private PlaybackWindowsOutputActivationResult ActivateCore()
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
            _owningThreadId = Environment.CurrentManagedThreadId;
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
        lock (_sync)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _isActivated = false;
            _owningThreadId = 0;
            ReleaseOwnedResources();
        }
    }

    internal PlaybackWindowsOutputResourceBorrowResult BorrowActivatedResources(
        Func<PlaybackWindowsOutputBorrowedResources,
            PlaybackWindowsOutputResourceBorrowResult> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (_sync)
        {
            if (_isDisposed)
            {
                return PlaybackWindowsOutputResourceBorrowResult.Disposed;
            }

            if (!_isActivated || _ownedResources.Count != 3)
            {
                return PlaybackWindowsOutputResourceBorrowResult.ActivationRequired;
            }

            if (_enforceOwningThread &&
                _owningThreadId != Environment.CurrentManagedThreadId)
            {
                return PlaybackWindowsOutputResourceBorrowResult.InvalidEnvironment;
            }

            try
            {
                var result = callback(new(
                    _ownedResources[1],
                    _ownedResources[2]));
                return Enum.IsDefined(result)
                    ? result
                    : PlaybackWindowsOutputResourceBorrowResult.Failure;
            }
            catch
            {
                return PlaybackWindowsOutputResourceBorrowResult.Failure;
            }
        }
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

internal enum PlaybackWindowsOutputResourceBorrowResult
{
    Success,
    ActivationRequired,
    DeviceLost,
    FormatUnsupported,
    ResizeRequired,
    InvalidEnvironment,
    Disposed,
    Failure,
}

internal readonly record struct PlaybackWindowsOutputBorrowedResources(
    IDisposable Direct3D11,
    IDisposable Wasapi);

internal interface IPlaybackWindowsDirect3D11BorrowedResource
{
    nint Device { get; }
    void ReplaceOutputResources(nint swapChain, nint renderTargetView);
    void ClearOutputResources();
}

internal interface IPlaybackWindowsWasapiBorrowedResource
{
    nint AudioClient { get; }
    nint EventHandle { get; }
    bool RecreateAudioClient();
    void ResetAudioClient();
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
            device = new Direct3D11Resource(ownedDevice, ownedContext);
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
            audio = new WasapiResource(
                ownedEnumerator, ownedEndpoint, ownedClient, ownedEventHandle);
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

    private sealed class Direct3D11Resource(nint device, nint context) :
        IDisposable, IPlaybackWindowsDirect3D11BorrowedResource
    {
        private nint _device = device;
        private nint _context = context;
        private nint _swapChain;
        private nint _renderTargetView;
        public nint Device => _device;

        public void ReplaceOutputResources(nint swapChain, nint renderTargetView)
        {
            ClearOutputResources();
            _swapChain = swapChain;
            _renderTargetView = renderTargetView;
        }

        public void ClearOutputResources()
        {
            var view = Interlocked.Exchange(ref _renderTargetView, 0);
            var chain = Interlocked.Exchange(ref _swapChain, 0);
            if (view != 0) Marshal.Release(view);
            if (chain != 0) Marshal.Release(chain);
        }

        public void Dispose()
        {
            ClearOutputResources();
            var ownedContext = Interlocked.Exchange(ref _context, 0);
            var ownedDevice = Interlocked.Exchange(ref _device, 0);
            if (ownedContext != 0) Marshal.Release(ownedContext);
            if (ownedDevice != 0) Marshal.Release(ownedDevice);
        }
    }

    private sealed class WasapiResource(
        nint enumerator, nint endpoint, nint client, nint eventHandle) :
        IDisposable, IPlaybackWindowsWasapiBorrowedResource
    {
        private nint _enumerator = enumerator;
        private nint _endpoint = endpoint;
        private nint _client = client;
        private nint _eventHandle = eventHandle;
        public nint AudioClient => _client;
        public nint EventHandle => _eventHandle;

        public bool RecreateAudioClient()
        {
            if (_endpoint == 0) return false;
            var endpointObject = (IMMDevice)Marshal.GetObjectForIUnknown(_endpoint);
            try
            {
                var audioClientId = AudioClientInterface;
                var result = endpointObject.Activate(
                    ref audioClientId, ClsctxInprocServer, 0, out var replacement);
                if (result < 0 || replacement == 0) return false;
                var previous = Interlocked.Exchange(ref _client, replacement);
                if (previous != 0) Marshal.Release(previous);
                return true;
            }
            finally
            {
                Marshal.FinalReleaseComObject(endpointObject);
            }
        }

        public void ResetAudioClient()
        {
            if (_client == 0) return;
            var clientObject = (IAudioClientReset)Marshal.GetObjectForIUnknown(_client);
            try { clientObject.Reset(); }
            finally { Marshal.FinalReleaseComObject(clientObject); }
        }

        public void Dispose()
        {
            var handle = Interlocked.Exchange(ref _eventHandle, 0);
            var ownedClient = Interlocked.Exchange(ref _client, 0);
            var ownedEndpoint = Interlocked.Exchange(ref _endpoint, 0);
            var ownedEnumerator = Interlocked.Exchange(ref _enumerator, 0);
            if (handle != 0) CloseHandle(handle);
            if (ownedClient != 0) Marshal.Release(ownedClient);
            if (ownedEndpoint != 0) Marshal.Release(ownedEndpoint);
            if (ownedEnumerator != 0) Marshal.Release(ownedEnumerator);
        }
    }

    [ComImport]
    [Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClientReset
    {
        [PreserveSig] int Initialize(uint shareMode, uint streamFlags, long duration, long periodicity, nint format, nint session);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(uint shareMode, nint format, out nint closest);
        [PreserveSig] int GetMixFormat(out nint format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
    }
}
