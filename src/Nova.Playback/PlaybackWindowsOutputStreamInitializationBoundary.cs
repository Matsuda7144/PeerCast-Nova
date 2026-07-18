using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PeerCast.Nova.Playback;

public enum PlaybackWindowsOutputStreamState
{
    Uninitialized,
    Initialized,
    DeviceLost,
    Disposed,
}

public enum PlaybackWindowsOutputStreamInitializationResult
{
    Initialized,
    ActivationRequired,
    BindingRequired,
    PlatformUnsupported,
    InvalidConfiguration,
    FormatUnsupported,
    AlreadyInitialized,
    ResizeRequired,
    DeviceLost,
    InvalidEnvironment,
    Cancelled,
    Disposed,
    Failure,
}

public enum PlaybackWindowsOutputStreamResetResult
{
    Reset,
    NotInitialized,
    DeviceLost,
    Cancelled,
    Disposed,
    Failure,
}

public readonly record struct PlaybackWindowsVideoStreamConfiguration(
    long WindowToken,
    int Width,
    int Height,
    PlaybackDecodedVideoPixelFormat PixelFormat,
    long Generation);

public readonly record struct PlaybackWindowsAudioStreamConfiguration(
    PlaybackDecodedAudioSampleFormat SampleFormat,
    int SampleRate,
    int ChannelCount,
    TimeSpan BufferDuration,
    long Generation);

public readonly record struct PlaybackWindowsOutputStreamSnapshot(
    PlaybackWindowsOutputStreamState VideoState,
    PlaybackWindowsOutputStreamState AudioState,
    long VideoGeneration,
    long AudioGeneration);

public sealed class PlaybackWindowsOutputStreamInitializationBoundary : IDisposable
{
    public static readonly TimeSpan MaximumAudioBufferDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MinimumAudioBufferDuration = TimeSpan.FromMilliseconds(1);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly PlaybackWindowsOutputActivationBoundary _activation;
    private readonly PlaybackWindowsOutputResourceBindingBoundary _binding;
    private readonly IPlaybackWindowsOutputNativeStreamInitializer _native;
    private readonly Func<bool> _isSupportedPlatform;
    private PlaybackWindowsOutputStreamState _videoState;
    private PlaybackWindowsOutputStreamState _audioState;
    private long _videoGeneration;
    private long _audioGeneration;
    private bool _videoInitialized;
    private bool _audioInitialized;
    private volatile bool _isDisposed;

    public PlaybackWindowsOutputStreamInitializationBoundary(
        PlaybackWindowsOutputActivationBoundary activation,
        PlaybackWindowsOutputResourceBindingBoundary binding)
        : this(activation, binding, CreateSystemInitializer(),
            static () => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22_000))
    {
    }

    private static IPlaybackWindowsOutputNativeStreamInitializer CreateSystemInitializer() =>
        OperatingSystem.IsWindows()
            ? new SystemPlaybackWindowsOutputNativeStreamInitializer()
            : new UnsupportedPlaybackWindowsOutputNativeStreamInitializer();

    internal PlaybackWindowsOutputStreamInitializationBoundary(
        PlaybackWindowsOutputActivationBoundary activation,
        PlaybackWindowsOutputResourceBindingBoundary binding,
        IPlaybackWindowsOutputNativeStreamInitializer native,
        Func<bool> isSupportedPlatform)
    {
        ArgumentNullException.ThrowIfNull(activation);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(native);
        ArgumentNullException.ThrowIfNull(isSupportedPlatform);
        _activation = activation;
        _binding = binding;
        _native = native;
        _isSupportedPlatform = isSupportedPlatform;
    }

    public PlaybackWindowsOutputStreamSnapshot Snapshot => new(
        _videoState, _audioState, _videoGeneration, _audioGeneration);

    public async ValueTask<PlaybackWindowsOutputStreamInitializationResult> InitializeAsync(
        PlaybackWindowsVideoStreamConfiguration video,
        PlaybackWindowsAudioStreamConfiguration audio,
        CancellationToken cancellationToken)
    {
        if (_isDisposed) return PlaybackWindowsOutputStreamInitializationResult.Disposed;
        var validation = Validate(video, audio);
        if (validation != PlaybackWindowsOutputStreamInitializationResult.Initialized)
        {
            return validation;
        }

        if (!await EnterAsync(cancellationToken).ConfigureAwait(false))
        {
            return PlaybackWindowsOutputStreamInitializationResult.Cancelled;
        }

        try
        {
            if (_isDisposed) return PlaybackWindowsOutputStreamInitializationResult.Disposed;
            if (!_isSupportedPlatform())
                return PlaybackWindowsOutputStreamInitializationResult.PlatformUnsupported;
            if (_videoState == PlaybackWindowsOutputStreamState.Initialized ||
                _audioState == PlaybackWindowsOutputStreamState.Initialized)
                return PlaybackWindowsOutputStreamInitializationResult.AlreadyInitialized;

            var activation = _activation.BorrowActivatedResources(
                static _ => PlaybackWindowsOutputResourceBorrowResult.Success);
            if (activation != PlaybackWindowsOutputResourceBorrowResult.Success)
                return MapBorrow(activation);

            var binding = _binding.Snapshot;
            if (binding.VideoState != PlaybackWindowsOutputBindingState.Bound ||
                binding.AudioState != PlaybackWindowsOutputBindingState.Bound ||
                binding.VideoGeneration != video.Generation ||
                binding.AudioGeneration != audio.Generation)
                return PlaybackWindowsOutputStreamInitializationResult.BindingRequired;

            var borrowed = _activation.BorrowActivatedResources(resources =>
                InitializeBorrowed(resources, video, audio));
            return MapBorrow(borrowed);
        }
        catch
        {
            RollbackOwnedResources();
            return PlaybackWindowsOutputStreamInitializationResult.Failure;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<PlaybackWindowsOutputStreamInitializationResult> ResizeVideoAsync(
        PlaybackWindowsVideoStreamConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (_isDisposed) return PlaybackWindowsOutputStreamInitializationResult.Disposed;
        if (!IsValidVideo(configuration))
            return PlaybackWindowsOutputStreamInitializationResult.InvalidConfiguration;
        if (!await EnterAsync(cancellationToken).ConfigureAwait(false))
            return PlaybackWindowsOutputStreamInitializationResult.Cancelled;

        try
        {
            if (_isDisposed) return PlaybackWindowsOutputStreamInitializationResult.Disposed;
            if (_videoState != PlaybackWindowsOutputStreamState.Initialized)
                return PlaybackWindowsOutputStreamInitializationResult.BindingRequired;
            if (configuration.Generation != _videoGeneration)
                return PlaybackWindowsOutputStreamInitializationResult.ResizeRequired;

            var borrowed = _activation.BorrowActivatedResources(resources =>
                MapNative(_native.ResizeVideo(resources.Direct3D11, configuration)));
            var result = MapBorrow(borrowed);
            if (result == PlaybackWindowsOutputStreamInitializationResult.Initialized)
                _videoGeneration = configuration.Generation;
            return result;
        }
        catch
        {
            return PlaybackWindowsOutputStreamInitializationResult.Failure;
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask<PlaybackWindowsOutputStreamResetResult> ResetVideoAsync(
        long generation, CancellationToken cancellationToken) =>
        ResetAsync(generation, video: true, cancellationToken);

    public ValueTask<PlaybackWindowsOutputStreamResetResult> ResetAudioAsync(
        long generation, CancellationToken cancellationToken) =>
        ResetAsync(generation, video: false, cancellationToken);

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _gate.Wait();
        try
        {
            RollbackOwnedResources();
            _videoState = PlaybackWindowsOutputStreamState.Disposed;
            _audioState = PlaybackWindowsOutputStreamState.Disposed;
        }
        finally
        {
            _gate.Release();
        }
    }

    private PlaybackWindowsOutputResourceBorrowResult InitializeBorrowed(
        PlaybackWindowsOutputBorrowedResources resources,
        PlaybackWindowsVideoStreamConfiguration video,
        PlaybackWindowsAudioStreamConfiguration audio)
    {
        PlaybackWindowsOutputNativeStreamResult videoResult;
        try { videoResult = _native.InitializeVideo(resources.Direct3D11, video); }
        catch
        {
            TryCleanup(() => _native.ResetVideo(resources.Direct3D11));
            return PlaybackWindowsOutputResourceBorrowResult.Failure;
        }
        if (!Enum.IsDefined(videoResult) ||
            videoResult != PlaybackWindowsOutputNativeStreamResult.Initialized)
        {
            TryCleanup(() => _native.ResetVideo(resources.Direct3D11));
            return Enum.IsDefined(videoResult)
                ? MapNative(videoResult)
                : PlaybackWindowsOutputResourceBorrowResult.Failure;
        }

        _videoInitialized = true;
        _videoState = PlaybackWindowsOutputStreamState.Initialized;
        _videoGeneration = video.Generation;
        PlaybackWindowsOutputNativeStreamResult audioResult;
        try { audioResult = _native.InitializeAudio(resources.Wasapi, audio); }
        catch
        {
            TryCleanup(() => _native.ResetAudio(resources.Wasapi));
            RollbackBorrowed(resources);
            return PlaybackWindowsOutputResourceBorrowResult.Failure;
        }
        if (!Enum.IsDefined(audioResult) ||
            audioResult != PlaybackWindowsOutputNativeStreamResult.Initialized)
        {
            TryCleanup(() => _native.ResetAudio(resources.Wasapi));
            RollbackBorrowed(resources);
            return Enum.IsDefined(audioResult)
                ? MapNative(audioResult)
                : PlaybackWindowsOutputResourceBorrowResult.Failure;
        }

        _audioInitialized = true;
        _audioState = PlaybackWindowsOutputStreamState.Initialized;
        _audioGeneration = audio.Generation;
        return PlaybackWindowsOutputResourceBorrowResult.Success;
    }

    private async ValueTask<PlaybackWindowsOutputStreamResetResult> ResetAsync(
        long generation, bool video, CancellationToken cancellationToken)
    {
        if (_isDisposed) return PlaybackWindowsOutputStreamResetResult.Disposed;
        if (generation < 0) return PlaybackWindowsOutputStreamResetResult.Failure;
        if (!await EnterAsync(cancellationToken).ConfigureAwait(false))
            return PlaybackWindowsOutputStreamResetResult.Cancelled;
        try
        {
            if (_isDisposed) return PlaybackWindowsOutputStreamResetResult.Disposed;
            var state = video ? _videoState : _audioState;
            if (state == PlaybackWindowsOutputStreamState.Uninitialized)
                return PlaybackWindowsOutputStreamResetResult.NotInitialized;
            var lost = state == PlaybackWindowsOutputStreamState.DeviceLost;
            if (video)
            {
                if (_videoInitialized) ResetBorrowed(video: true);
                _videoInitialized = false;
                _videoState = PlaybackWindowsOutputStreamState.Uninitialized;
                _videoGeneration = generation;
            }
            else
            {
                if (_audioInitialized) ResetBorrowed(video: false);
                _audioInitialized = false;
                _audioState = PlaybackWindowsOutputStreamState.Uninitialized;
                _audioGeneration = generation;
            }
            return lost ? PlaybackWindowsOutputStreamResetResult.DeviceLost :
                PlaybackWindowsOutputStreamResetResult.Reset;
        }
        catch { return PlaybackWindowsOutputStreamResetResult.Failure; }
        finally { _gate.Release(); }
    }

    private void RollbackOwnedResources()
    {
        _activation.BorrowActivatedResources(resources =>
        {
            RollbackBorrowed(resources);
            return PlaybackWindowsOutputResourceBorrowResult.Success;
        });
        _audioState = PlaybackWindowsOutputStreamState.Uninitialized;
        _videoState = PlaybackWindowsOutputStreamState.Uninitialized;
    }

    private void RollbackBorrowed(PlaybackWindowsOutputBorrowedResources resources)
    {
        if (_audioInitialized)
        {
            TryCleanup(() => _native.ResetAudio(resources.Wasapi));
            _audioInitialized = false;
        }
        if (_videoInitialized)
        {
            TryCleanup(() => _native.ResetVideo(resources.Direct3D11));
            _videoInitialized = false;
        }
    }

    private void ResetBorrowed(bool video)
    {
        _activation.BorrowActivatedResources(resources =>
        {
            if (video) TryCleanup(() => _native.ResetVideo(resources.Direct3D11));
            else TryCleanup(() => _native.ResetAudio(resources.Wasapi));
            return PlaybackWindowsOutputResourceBorrowResult.Success;
        });
    }

    private async ValueTask<bool> EnterAsync(CancellationToken cancellationToken)
    {
        try { await _gate.WaitAsync(cancellationToken).ConfigureAwait(false); return true; }
        catch (OperationCanceledException) { return false; }
    }

    private static PlaybackWindowsOutputStreamInitializationResult Validate(
        PlaybackWindowsVideoStreamConfiguration video,
        PlaybackWindowsAudioStreamConfiguration audio)
    {
        if (!Enum.IsDefined(video.PixelFormat) || !Enum.IsDefined(audio.SampleFormat))
            return PlaybackWindowsOutputStreamInitializationResult.Failure;
        if (!IsValidVideo(video) || !IsValidAudio(audio))
            return PlaybackWindowsOutputStreamInitializationResult.InvalidConfiguration;
        if (video.PixelFormat != PlaybackDecodedVideoPixelFormat.Bgra32 ||
            audio.SampleFormat is not (PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved or
                PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved))
            return PlaybackWindowsOutputStreamInitializationResult.FormatUnsupported;
        return PlaybackWindowsOutputStreamInitializationResult.Initialized;
    }

    private static bool IsValidVideo(PlaybackWindowsVideoStreamConfiguration value) =>
        value.WindowToken > 0 && value.Width > 0 && value.Height > 0 &&
        value.Generation >= 0 && Enum.IsDefined(value.PixelFormat);

    private static bool IsValidAudio(PlaybackWindowsAudioStreamConfiguration value) =>
        value.SampleRate > 0 && value.ChannelCount > 0 && value.Generation >= 0 &&
        value.BufferDuration >= MinimumAudioBufferDuration &&
        value.BufferDuration <= MaximumAudioBufferDuration &&
        Enum.IsDefined(value.SampleFormat);

    private PlaybackWindowsOutputStreamInitializationResult MapBorrow(
        PlaybackWindowsOutputResourceBorrowResult result)
    {
        if (result == PlaybackWindowsOutputResourceBorrowResult.DeviceLost)
        {
            _videoState = PlaybackWindowsOutputStreamState.DeviceLost;
            _audioState = PlaybackWindowsOutputStreamState.DeviceLost;
        }
        return result switch
        {
            PlaybackWindowsOutputResourceBorrowResult.Success => PlaybackWindowsOutputStreamInitializationResult.Initialized,
            PlaybackWindowsOutputResourceBorrowResult.ActivationRequired or PlaybackWindowsOutputResourceBorrowResult.Disposed => PlaybackWindowsOutputStreamInitializationResult.ActivationRequired,
            PlaybackWindowsOutputResourceBorrowResult.DeviceLost => PlaybackWindowsOutputStreamInitializationResult.DeviceLost,
            PlaybackWindowsOutputResourceBorrowResult.FormatUnsupported => PlaybackWindowsOutputStreamInitializationResult.FormatUnsupported,
            PlaybackWindowsOutputResourceBorrowResult.ResizeRequired => PlaybackWindowsOutputStreamInitializationResult.ResizeRequired,
            PlaybackWindowsOutputResourceBorrowResult.InvalidEnvironment => PlaybackWindowsOutputStreamInitializationResult.InvalidEnvironment,
            _ => PlaybackWindowsOutputStreamInitializationResult.Failure,
        };
    }

    private static PlaybackWindowsOutputResourceBorrowResult MapNative(
        PlaybackWindowsOutputNativeStreamResult result) => result switch
        {
            PlaybackWindowsOutputNativeStreamResult.Initialized => PlaybackWindowsOutputResourceBorrowResult.Success,
            PlaybackWindowsOutputNativeStreamResult.DeviceLost => PlaybackWindowsOutputResourceBorrowResult.DeviceLost,
            PlaybackWindowsOutputNativeStreamResult.FormatUnsupported => PlaybackWindowsOutputResourceBorrowResult.FormatUnsupported,
            PlaybackWindowsOutputNativeStreamResult.ResizeRequired => PlaybackWindowsOutputResourceBorrowResult.ResizeRequired,
            _ => PlaybackWindowsOutputResourceBorrowResult.Failure,
        };

    private static void TryCleanup(Action cleanup)
    {
        try { cleanup(); } catch { }
    }
}

internal enum PlaybackWindowsOutputNativeStreamResult
{
    Initialized,
    FormatUnsupported,
    ResizeRequired,
    DeviceLost,
    Failure,
}

internal interface IPlaybackWindowsOutputNativeStreamInitializer
{
    PlaybackWindowsOutputNativeStreamResult InitializeVideo(IDisposable borrowedResource, PlaybackWindowsVideoStreamConfiguration configuration);
    PlaybackWindowsOutputNativeStreamResult ResizeVideo(IDisposable borrowedResource, PlaybackWindowsVideoStreamConfiguration configuration);
    PlaybackWindowsOutputNativeStreamResult InitializeAudio(IDisposable borrowedResource, PlaybackWindowsAudioStreamConfiguration configuration);
    void ResetVideo(IDisposable borrowedResource);
    void ResetAudio(IDisposable borrowedResource);
}

internal sealed class UnsupportedPlaybackWindowsOutputNativeStreamInitializer :
    IPlaybackWindowsOutputNativeStreamInitializer
{
    public PlaybackWindowsOutputNativeStreamResult InitializeVideo(IDisposable borrowedResource, PlaybackWindowsVideoStreamConfiguration configuration) => PlaybackWindowsOutputNativeStreamResult.Failure;
    public PlaybackWindowsOutputNativeStreamResult ResizeVideo(IDisposable borrowedResource, PlaybackWindowsVideoStreamConfiguration configuration) => PlaybackWindowsOutputNativeStreamResult.Failure;
    public PlaybackWindowsOutputNativeStreamResult InitializeAudio(IDisposable borrowedResource, PlaybackWindowsAudioStreamConfiguration configuration) => PlaybackWindowsOutputNativeStreamResult.Failure;
    public void ResetVideo(IDisposable borrowedResource) { }
    public void ResetAudio(IDisposable borrowedResource) { }
}

[SupportedOSPlatform("windows")]
internal sealed class SystemPlaybackWindowsOutputNativeStreamInitializer : IPlaybackWindowsOutputNativeStreamInitializer
{
    private const uint DxgiFormatB8G8R8A8Unorm = 87;
    private const uint DxgiUsageRenderTargetOutput = 0x20;
    private const uint DxgiSwapEffectDiscard = 0;
    private const uint AudioClientShared = 0;
    private const uint AudioClientStreamFlagsEventCallback = 0x00040000;
    private const ushort WaveFormatPcm = 1;
    private const ushort WaveFormatIeeeFloat = 3;
    private static readonly Guid DxgiFactoryInterface = new("7B7166EC-21C7-44AE-B21A-C9AE321AE369");
    private static readonly Guid D3D11Texture2DInterface = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");
    private readonly IPlaybackWindowsWindowIdentityResolver _windows;

    public SystemPlaybackWindowsOutputNativeStreamInitializer()
        : this(new SystemPlaybackWindowsWindowIdentityResolver()) { }

    internal SystemPlaybackWindowsOutputNativeStreamInitializer(
        IPlaybackWindowsWindowIdentityResolver windows) => _windows = windows;

    public PlaybackWindowsOutputNativeStreamResult InitializeVideo(
        IDisposable borrowedResource,
        PlaybackWindowsVideoStreamConfiguration configuration)
    {
        if (borrowedResource is not IPlaybackWindowsDirect3D11BorrowedResource d3d ||
            d3d.Device == 0 || !_windows.TryResolve(configuration.WindowToken, out var window) ||
            window == 0)
            return PlaybackWindowsOutputNativeStreamResult.Failure;

        nint factoryPointer = 0;
        nint swapChainPointer = 0;
        nint backBufferPointer = 0;
        nint renderTargetPointer = 0;
        try
        {
            var factoryId = DxgiFactoryInterface;
            var result = CreateDXGIFactory1(ref factoryId, out factoryPointer);
            if (result < 0) return Map(result);
            var factory = (IDXGIFactory)Marshal.GetObjectForIUnknown(factoryPointer);
            try
            {
                var description = CreateDescription(window, configuration.Width, configuration.Height);
                result = factory.CreateSwapChain(d3d.Device, ref description, out swapChainPointer);
            }
            finally { Marshal.FinalReleaseComObject(factory); }
            if (result < 0) return Map(result);

            var chain = (IDXGISwapChain)Marshal.GetObjectForIUnknown(swapChainPointer);
            try
            {
                var textureId = D3D11Texture2DInterface;
                result = chain.GetBuffer(0, ref textureId, out backBufferPointer);
            }
            finally { Marshal.FinalReleaseComObject(chain); }
            if (result < 0) return Map(result);

            var device = (ID3D11Device)Marshal.GetObjectForIUnknown(d3d.Device);
            try { result = device.CreateRenderTargetView(backBufferPointer, 0, out renderTargetPointer); }
            finally { Marshal.FinalReleaseComObject(device); }
            if (result < 0) return Map(result);

            d3d.ReplaceOutputResources(swapChainPointer, renderTargetPointer);
            swapChainPointer = 0;
            renderTargetPointer = 0;
            return PlaybackWindowsOutputNativeStreamResult.Initialized;
        }
        catch { return PlaybackWindowsOutputNativeStreamResult.Failure; }
        finally
        {
            if (renderTargetPointer != 0) Marshal.Release(renderTargetPointer);
            if (backBufferPointer != 0) Marshal.Release(backBufferPointer);
            if (swapChainPointer != 0) Marshal.Release(swapChainPointer);
            if (factoryPointer != 0) Marshal.Release(factoryPointer);
        }
    }

    public PlaybackWindowsOutputNativeStreamResult ResizeVideo(
        IDisposable borrowedResource,
        PlaybackWindowsVideoStreamConfiguration configuration)
    {
        ResetVideo(borrowedResource);
        return InitializeVideo(borrowedResource, configuration);
    }

    public PlaybackWindowsOutputNativeStreamResult InitializeAudio(
        IDisposable borrowedResource,
        PlaybackWindowsAudioStreamConfiguration configuration)
    {
        if (borrowedResource is not IPlaybackWindowsWasapiBorrowedResource wasapi ||
            wasapi.EventHandle == 0 || !wasapi.RecreateAudioClient() ||
            wasapi.AudioClient == 0)
            return PlaybackWindowsOutputNativeStreamResult.Failure;

        nint requested = 0;
        nint mix = 0;
        nint closest = 0;
        object? clientObject = null;
        try
        {
            clientObject = Marshal.GetObjectForIUnknown(wasapi.AudioClient);
            var client = (IAudioClient)clientObject;
            var result = client.GetMixFormat(out mix);
            if (result < 0 || mix == 0) return Map(result);

            var format = CreateWaveFormat(configuration);
            requested = Marshal.AllocCoTaskMem(Marshal.SizeOf<WaveFormatEx>());
            Marshal.StructureToPtr(format, requested, false);
            result = client.IsFormatSupported(AudioClientShared, requested, out closest);
            if (result != 0) return result < 0 ? Map(result) :
                PlaybackWindowsOutputNativeStreamResult.FormatUnsupported;

            var duration = checked(configuration.BufferDuration.Ticks);
            result = client.Initialize(AudioClientShared,
                AudioClientStreamFlagsEventCallback, duration, 0, requested, 0);
            if (result < 0) return Map(result);
            result = client.SetEventHandle(wasapi.EventHandle);
            return result < 0 ? Map(result) : PlaybackWindowsOutputNativeStreamResult.Initialized;
        }
        catch { return PlaybackWindowsOutputNativeStreamResult.Failure; }
        finally
        {
            if (clientObject is not null) Marshal.FinalReleaseComObject(clientObject);
            if (closest != 0) Marshal.FreeCoTaskMem(closest);
            if (mix != 0) Marshal.FreeCoTaskMem(mix);
            if (requested != 0) Marshal.FreeCoTaskMem(requested);
        }
    }

    public void ResetVideo(IDisposable borrowedResource)
    {
        if (borrowedResource is IPlaybackWindowsDirect3D11BorrowedResource d3d)
            d3d.ClearOutputResources();
    }

    public void ResetAudio(IDisposable borrowedResource)
    {
        if (borrowedResource is IPlaybackWindowsWasapiBorrowedResource wasapi)
            wasapi.ResetAudioClient();
    }

    internal static PlaybackWindowsOutputNativeStreamResult MapHResult(int hresult) => hresult switch
    {
        unchecked((int)0x887A0005) or unchecked((int)0x887A0006) or
        unchecked((int)0x887A0007) or unchecked((int)0x88890004) =>
            PlaybackWindowsOutputNativeStreamResult.DeviceLost,
        unchecked((int)0x88890008) => PlaybackWindowsOutputNativeStreamResult.FormatUnsupported,
        unchecked((int)0x887A0001) => PlaybackWindowsOutputNativeStreamResult.ResizeRequired,
        _ => PlaybackWindowsOutputNativeStreamResult.Failure,
    };

    private static PlaybackWindowsOutputNativeStreamResult Map(int hresult) =>
        MapHResult(hresult);

    private static DxgiSwapChainDescription CreateDescription(nint window, int width, int height) => new()
    {
        BufferDescription = new() { Width = (uint)width, Height = (uint)height, Format = DxgiFormatB8G8R8A8Unorm, RefreshRate = new() },
        SampleDescription = new() { Count = 1 },
        BufferUsage = DxgiUsageRenderTargetOutput,
        BufferCount = 1,
        OutputWindow = window,
        Windowed = true,
        SwapEffect = DxgiSwapEffectDiscard,
    };

    private static WaveFormatEx CreateWaveFormat(PlaybackWindowsAudioStreamConfiguration value)
    {
        var bits = value.SampleFormat == PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved ? 16 : 32;
        var blockAlign = checked((ushort)(value.ChannelCount * bits / 8));
        return new()
        {
            FormatTag = value.SampleFormat == PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved ? WaveFormatPcm : WaveFormatIeeeFloat,
            Channels = checked((ushort)value.ChannelCount),
            SamplesPerSecond = checked((uint)value.SampleRate),
            AverageBytesPerSecond = checked((uint)(value.SampleRate * blockAlign)),
            BlockAlign = blockAlign,
            BitsPerSample = checked((ushort)bits),
        };
    }

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid interfaceId, out nint factory);

    [StructLayout(LayoutKind.Sequential)] private struct Rational { public uint Numerator; public uint Denominator; }
    [StructLayout(LayoutKind.Sequential)] private struct ModeDescription { public uint Width; public uint Height; public Rational RefreshRate; public uint Format; public uint ScanlineOrdering; public uint Scaling; }
    [StructLayout(LayoutKind.Sequential)] private struct SampleDescription { public uint Count; public uint Quality; }
    [StructLayout(LayoutKind.Sequential)] private struct DxgiSwapChainDescription { public ModeDescription BufferDescription; public SampleDescription SampleDescription; public uint BufferUsage; public uint BufferCount; public nint OutputWindow; [MarshalAs(UnmanagedType.Bool)] public bool Windowed; public uint SwapEffect; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct WaveFormatEx { public ushort FormatTag; public ushort Channels; public uint SamplesPerSecond; public uint AverageBytesPerSecond; public ushort BlockAlign; public ushort BitsPerSample; public ushort ExtraSize; }

    [ComImport, Guid("7B7166EC-21C7-44AE-B21A-C9AE321AE369"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory
    {
        [PreserveSig] int SetPrivateData(ref Guid name, uint size, nint data);
        [PreserveSig] int SetPrivateDataInterface(ref Guid name, nint unknown);
        [PreserveSig] int GetPrivateData(ref Guid name, ref uint size, nint data);
        [PreserveSig] int GetParent(ref Guid id, out nint parent);
        [PreserveSig] int EnumAdapters(uint adapter, out nint result);
        [PreserveSig] int MakeWindowAssociation(nint window, uint flags);
        [PreserveSig] int GetWindowAssociation(out nint window);
        [PreserveSig] int CreateSwapChain(nint device, ref DxgiSwapChainDescription description, out nint swapChain);
    }

    [ComImport, Guid("310D36A0-D2E7-4C0A-AA04-6A9D23B8886A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGISwapChain
    {
        [PreserveSig] int SetPrivateData(ref Guid name, uint size, nint data);
        [PreserveSig] int SetPrivateDataInterface(ref Guid name, nint unknown);
        [PreserveSig] int GetPrivateData(ref Guid name, ref uint size, nint data);
        [PreserveSig] int GetParent(ref Guid id, out nint parent);
        [PreserveSig] int GetDevice(ref Guid id, out nint device);
        [PreserveSig] int Present(uint syncInterval, uint flags);
        [PreserveSig] int GetBuffer(uint buffer, ref Guid id, out nint surface);
    }

    [ComImport, Guid("DB6F6DDB-AC77-4E88-8253-819DF9BBF140"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ID3D11Device
    {
        [PreserveSig] int CreateBuffer(nint description, nint initialData, out nint buffer);
        [PreserveSig] int CreateTexture1D(nint description, nint initialData, out nint texture);
        [PreserveSig] int CreateTexture2D(nint description, nint initialData, out nint texture);
        [PreserveSig] int CreateTexture3D(nint description, nint initialData, out nint texture);
        [PreserveSig] int CreateShaderResourceView(nint resource, nint description, out nint view);
        [PreserveSig] int CreateUnorderedAccessView(nint resource, nint description, out nint view);
        [PreserveSig] int CreateRenderTargetView(nint resource, nint description, out nint view);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(uint shareMode, uint streamFlags, long bufferDuration, long periodicity, nint format, nint sessionGuid);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(uint shareMode, nint format, out nint closestMatch);
        [PreserveSig] int GetMixFormat(out nint format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(nint eventHandle);
    }
}

internal interface IPlaybackWindowsWindowIdentityResolver
{
    bool TryResolve(long token, out nint window);
}

[SupportedOSPlatform("windows")]
internal sealed class SystemPlaybackWindowsWindowIdentityResolver : IPlaybackWindowsWindowIdentityResolver
{
    public bool TryResolve(long token, out nint window)
    {
        window = checked((nint)token);
        return window != 0 && IsWindow(window);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);
}
