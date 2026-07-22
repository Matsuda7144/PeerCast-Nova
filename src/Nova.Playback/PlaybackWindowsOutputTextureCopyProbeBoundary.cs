using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PeerCast.Nova.Playback;

public enum PlaybackWindowsOutputTextureCopyProbeResult
{
    Presented,
    WouldBlock,
    InitializationRequired,
    BindingRequired,
    FormatUnsupported,
    ResizeRequired,
    DeviceLost,
    InvalidConfiguration,
    InvalidEnvironment,
    Cancelled,
    Disposed,
    Failure,
}

/// <summary>
/// Diagnostic-only one-frame D3D11 path that creates an exact BGRA texture from
/// caller-owned bytes, copies that texture to the existing swap-chain back buffer,
/// and presents once. It does not replace the production buffer processor.
/// </summary>
public sealed class PlaybackWindowsOutputTextureCopyProbeBoundary : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly PlaybackWindowsOutputActivationBoundary _activation;
    private readonly PlaybackWindowsOutputResourceBindingBoundary _binding;
    private readonly PlaybackWindowsOutputStreamInitializationBoundary _initialization;
    private readonly IPlaybackWindowsOutputTextureCopyProbeNative _native;
    private readonly Func<bool> _isSupportedPlatform;
    private bool _isDisposed;

    public PlaybackWindowsOutputTextureCopyProbeBoundary(
        PlaybackWindowsOutputActivationBoundary activation,
        PlaybackWindowsOutputResourceBindingBoundary binding,
        PlaybackWindowsOutputStreamInitializationBoundary initialization)
        : this(
            activation,
            binding,
            initialization,
            CreateSystemNative(),
            static () => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22_000))
    {
    }

    internal PlaybackWindowsOutputTextureCopyProbeBoundary(
        PlaybackWindowsOutputActivationBoundary activation,
        PlaybackWindowsOutputResourceBindingBoundary binding,
        PlaybackWindowsOutputStreamInitializationBoundary initialization,
        IPlaybackWindowsOutputTextureCopyProbeNative native,
        Func<bool> isSupportedPlatform)
    {
        ArgumentNullException.ThrowIfNull(activation);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(initialization);
        ArgumentNullException.ThrowIfNull(native);
        ArgumentNullException.ThrowIfNull(isSupportedPlatform);
        _activation = activation;
        _binding = binding;
        _initialization = initialization;
        _native = native;
        _isSupportedPlatform = isSupportedPlatform;
    }

    public async ValueTask<PlaybackWindowsOutputTextureCopyProbeResult> PresentAsync(
        PlaybackWindowsVideoFrameBuffer frame,
        CancellationToken cancellationToken = default)
    {
        if (_isDisposed)
        {
            return PlaybackWindowsOutputTextureCopyProbeResult.Disposed;
        }

        var validation = Validate(frame);
        if (validation != PlaybackWindowsOutputTextureCopyProbeResult.Presented)
        {
            return validation;
        }

        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return PlaybackWindowsOutputTextureCopyProbeResult.Cancelled;
        }

        try
        {
            if (_isDisposed)
            {
                return PlaybackWindowsOutputTextureCopyProbeResult.Disposed;
            }

            if (!_isSupportedPlatform())
            {
                return PlaybackWindowsOutputTextureCopyProbeResult.InvalidEnvironment;
            }

            var streams = _initialization.Snapshot;
            if (streams.VideoState != PlaybackWindowsOutputStreamState.Initialized ||
                streams.VideoGeneration != frame.Generation)
            {
                return PlaybackWindowsOutputTextureCopyProbeResult.InitializationRequired;
            }

            var binding = _binding.Snapshot;
            if (binding.VideoState != PlaybackWindowsOutputBindingState.Bound ||
                binding.VideoGeneration != frame.Generation)
            {
                return PlaybackWindowsOutputTextureCopyProbeResult.BindingRequired;
            }

            var borrowed = _activation.BorrowActivatedResources(resources =>
                MapNative(_native.Present(resources.Direct3D11, frame)));
            return MapBorrow(borrowed);
        }
        catch
        {
            return PlaybackWindowsOutputTextureCopyProbeResult.Failure;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _gate.Wait();
        _gate.Release();
    }

    private static PlaybackWindowsOutputTextureCopyProbeResult Validate(
        PlaybackWindowsVideoFrameBuffer frame)
    {
        if (!Enum.IsDefined(frame.PixelFormat))
        {
            return PlaybackWindowsOutputTextureCopyProbeResult.Failure;
        }

        if (frame.PixelFormat != PlaybackDecodedVideoPixelFormat.Bgra32)
        {
            return PlaybackWindowsOutputTextureCopyProbeResult.FormatUnsupported;
        }

        if (frame.Width <= 0 || frame.Height <= 0 || frame.Generation < 0 ||
            frame.Stride != (long)frame.Width * 4 || frame.Buffer.Length <= 0 ||
            frame.Buffer.Length > PlaybackWindowsOutputBufferProcessingBoundary.MaximumBufferBytes ||
            (long)frame.Stride * frame.Height != frame.Buffer.Length)
        {
            return PlaybackWindowsOutputTextureCopyProbeResult.InvalidConfiguration;
        }

        return PlaybackWindowsOutputTextureCopyProbeResult.Presented;
    }

    private static PlaybackWindowsOutputResourceBorrowResult MapNative(
        PlaybackWindowsOutputTextureCopyProbeNativeResult result) => result switch
        {
            PlaybackWindowsOutputTextureCopyProbeNativeResult.Presented =>
                PlaybackWindowsOutputResourceBorrowResult.Success,
            PlaybackWindowsOutputTextureCopyProbeNativeResult.WouldBlock =>
                PlaybackWindowsOutputResourceBorrowResult.WouldBlock,
            PlaybackWindowsOutputTextureCopyProbeNativeResult.FormatUnsupported =>
                PlaybackWindowsOutputResourceBorrowResult.FormatUnsupported,
            PlaybackWindowsOutputTextureCopyProbeNativeResult.ResizeRequired =>
                PlaybackWindowsOutputResourceBorrowResult.ResizeRequired,
            PlaybackWindowsOutputTextureCopyProbeNativeResult.DeviceLost =>
                PlaybackWindowsOutputResourceBorrowResult.DeviceLost,
            _ => PlaybackWindowsOutputResourceBorrowResult.Failure,
        };

    private static PlaybackWindowsOutputTextureCopyProbeResult MapBorrow(
        PlaybackWindowsOutputResourceBorrowResult result) => result switch
        {
            PlaybackWindowsOutputResourceBorrowResult.Success =>
                PlaybackWindowsOutputTextureCopyProbeResult.Presented,
            PlaybackWindowsOutputResourceBorrowResult.WouldBlock =>
                PlaybackWindowsOutputTextureCopyProbeResult.WouldBlock,
            PlaybackWindowsOutputResourceBorrowResult.ActivationRequired =>
                PlaybackWindowsOutputTextureCopyProbeResult.InitializationRequired,
            PlaybackWindowsOutputResourceBorrowResult.FormatUnsupported =>
                PlaybackWindowsOutputTextureCopyProbeResult.FormatUnsupported,
            PlaybackWindowsOutputResourceBorrowResult.ResizeRequired =>
                PlaybackWindowsOutputTextureCopyProbeResult.ResizeRequired,
            PlaybackWindowsOutputResourceBorrowResult.DeviceLost =>
                PlaybackWindowsOutputTextureCopyProbeResult.DeviceLost,
            PlaybackWindowsOutputResourceBorrowResult.InvalidEnvironment =>
                PlaybackWindowsOutputTextureCopyProbeResult.InvalidEnvironment,
            PlaybackWindowsOutputResourceBorrowResult.Disposed =>
                PlaybackWindowsOutputTextureCopyProbeResult.Disposed,
            _ => PlaybackWindowsOutputTextureCopyProbeResult.Failure,
        };

    private static IPlaybackWindowsOutputTextureCopyProbeNative CreateSystemNative() =>
        OperatingSystem.IsWindows()
            ? new SystemPlaybackWindowsOutputTextureCopyProbeNative()
            : new UnsupportedPlaybackWindowsOutputTextureCopyProbeNative();
}

internal enum PlaybackWindowsOutputTextureCopyProbeNativeResult
{
    Presented,
    WouldBlock,
    FormatUnsupported,
    ResizeRequired,
    DeviceLost,
    Failure,
}

internal interface IPlaybackWindowsOutputTextureCopyProbeNative
{
    PlaybackWindowsOutputTextureCopyProbeNativeResult Present(
        IDisposable borrowedResource,
        PlaybackWindowsVideoFrameBuffer frame);
}

internal sealed class UnsupportedPlaybackWindowsOutputTextureCopyProbeNative :
    IPlaybackWindowsOutputTextureCopyProbeNative
{
    public PlaybackWindowsOutputTextureCopyProbeNativeResult Present(
        IDisposable borrowedResource,
        PlaybackWindowsVideoFrameBuffer frame) =>
        PlaybackWindowsOutputTextureCopyProbeNativeResult.Failure;
}

internal readonly record struct PlaybackWindowsD3D11TextureCopyPlan(
    uint Width,
    uint Height,
    uint Format,
    uint RowPitch,
    uint SlicePitch)
{
    internal const uint Bgra32UnormFormat = 87;

    internal static bool TryCreate(
        PlaybackWindowsVideoFrameBuffer frame,
        out PlaybackWindowsD3D11TextureCopyPlan plan)
    {
        plan = default;
        if (frame.PixelFormat != PlaybackDecodedVideoPixelFormat.Bgra32 ||
            frame.Width <= 0 || frame.Height <= 0 || frame.Stride <= 0 ||
            frame.Buffer.Length <= 0 || frame.Stride != (long)frame.Width * 4 ||
            (long)frame.Stride * frame.Height != frame.Buffer.Length)
        {
            return false;
        }

        plan = new(
            checked((uint)frame.Width),
            checked((uint)frame.Height),
            Bgra32UnormFormat,
            checked((uint)frame.Stride),
            checked((uint)frame.Buffer.Length));
        return true;
    }
}

internal sealed class SystemPlaybackWindowsOutputTextureCopyProbeNative :
    IPlaybackWindowsOutputTextureCopyProbeNative
{
    private const int DxgiStatusOccluded = 0x087A0001;
    private readonly IPlaybackWindowsOutputTextureCopyProbeCalls _calls;

    [SupportedOSPlatform("windows")]
    internal SystemPlaybackWindowsOutputTextureCopyProbeNative()
        : this(new SystemPlaybackWindowsOutputTextureCopyProbeCalls())
    {
    }

    internal SystemPlaybackWindowsOutputTextureCopyProbeNative(
        IPlaybackWindowsOutputTextureCopyProbeCalls calls)
    {
        ArgumentNullException.ThrowIfNull(calls);
        _calls = calls;
    }

    public PlaybackWindowsOutputTextureCopyProbeNativeResult Present(
        IDisposable borrowedResource,
        PlaybackWindowsVideoFrameBuffer frame)
    {
        if (borrowedResource is not IPlaybackWindowsDirect3D11BorrowedResource resource ||
            resource.Device == 0 || resource.ImmediateContext == 0 || resource.SwapChain == 0 ||
            !PlaybackWindowsD3D11TextureCopyPlan.TryCreate(frame, out var plan))
        {
            return PlaybackWindowsOutputTextureCopyProbeNativeResult.Failure;
        }

        try
        {
            return Normalize(_calls.CreateTextureCopyAndPresent(resource, frame, plan));
        }
        catch
        {
            return PlaybackWindowsOutputTextureCopyProbeNativeResult.Failure;
        }
    }

    internal static PlaybackWindowsOutputTextureCopyProbeNativeResult Normalize(int hresult)
    {
        if (hresult >= 0)
        {
            return hresult == DxgiStatusOccluded
                ? PlaybackWindowsOutputTextureCopyProbeNativeResult.WouldBlock
                : PlaybackWindowsOutputTextureCopyProbeNativeResult.Presented;
        }

        return hresult switch
        {
            unchecked((int)0x887A0001) =>
                PlaybackWindowsOutputTextureCopyProbeNativeResult.ResizeRequired,
            unchecked((int)0x887A0005) or unchecked((int)0x887A0006) or
            unchecked((int)0x887A0007) =>
                PlaybackWindowsOutputTextureCopyProbeNativeResult.DeviceLost,
            unchecked((int)0x887A0004) =>
                PlaybackWindowsOutputTextureCopyProbeNativeResult.FormatUnsupported,
            _ => PlaybackWindowsOutputTextureCopyProbeNativeResult.Failure,
        };
    }
}

internal interface IPlaybackWindowsOutputTextureCopyProbeCalls
{
    int CreateTextureCopyAndPresent(
        IPlaybackWindowsDirect3D11BorrowedResource resource,
        PlaybackWindowsVideoFrameBuffer frame,
        PlaybackWindowsD3D11TextureCopyPlan plan);
}

[SupportedOSPlatform("windows")]
internal sealed class SystemPlaybackWindowsOutputTextureCopyProbeCalls :
    IPlaybackWindowsOutputTextureCopyProbeCalls
{
    private const uint D3D11UsageDefault = 0;
    private static readonly Guid D3D11Texture2DInterface =
        new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

    public int CreateTextureCopyAndPresent(
        IPlaybackWindowsDirect3D11BorrowedResource resource,
        PlaybackWindowsVideoFrameBuffer frame,
        PlaybackWindowsD3D11TextureCopyPlan plan)
    {
        nint backBuffer = 0;
        nint uploadTexture = 0;
        object? deviceObject = null;
        object? contextObject = null;
        object? swapChainObject = null;
        var pinned = default(GCHandle);
        try
        {
            swapChainObject = Marshal.GetObjectForIUnknown(resource.SwapChain);
            var swapChain = (IDXGISwapChain)swapChainObject;
            var textureId = D3D11Texture2DInterface;
            var result = swapChain.GetBuffer(0, ref textureId, out backBuffer);
            if (result < 0)
            {
                return result;
            }

            var managed = frame.Buffer.ToArray();
            pinned = GCHandle.Alloc(managed, GCHandleType.Pinned);
            var description = new Texture2DDescription
            {
                Width = plan.Width,
                Height = plan.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = plan.Format,
                SampleDescription = new() { Count = 1 },
                Usage = D3D11UsageDefault,
            };
            var initialData = new SubresourceData
            {
                SystemMemory = pinned.AddrOfPinnedObject(),
                SystemMemoryPitch = plan.RowPitch,
                SystemMemorySlicePitch = plan.SlicePitch,
            };

            deviceObject = Marshal.GetObjectForIUnknown(resource.Device);
            var device = (ID3D11Device)deviceObject;
            result = device.CreateTexture2D(ref description, ref initialData, out uploadTexture);
            if (result < 0)
            {
                return result;
            }

            contextObject = Marshal.GetObjectForIUnknown(resource.ImmediateContext);
            var context = (ID3D11DeviceContext)contextObject;
            context.CopyResource(backBuffer, uploadTexture);
            return swapChain.Present(1, 0);
        }
        finally
        {
            if (pinned.IsAllocated)
            {
                pinned.Free();
            }

            if (contextObject is not null)
            {
                Marshal.FinalReleaseComObject(contextObject);
            }

            if (deviceObject is not null)
            {
                Marshal.FinalReleaseComObject(deviceObject);
            }

            if (swapChainObject is not null)
            {
                Marshal.FinalReleaseComObject(swapChainObject);
            }

            if (uploadTexture != 0)
            {
                Marshal.Release(uploadTexture);
            }

            if (backBuffer != 0)
            {
                Marshal.Release(backBuffer);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SampleDescription
    {
        public uint Count;
        public uint Quality;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Texture2DDescription
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public uint Format;
        public SampleDescription SampleDescription;
        public uint Usage;
        public uint BindFlags;
        public uint CpuAccessFlags;
        public uint MiscellaneousFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SubresourceData
    {
        public nint SystemMemory;
        public uint SystemMemoryPitch;
        public uint SystemMemorySlicePitch;
    }

    [ComImport, Guid("310D36A0-D2E7-4C0A-AA04-6A9D23B8886A"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGISwapChain
    {
        [PreserveSig] int SetPrivateData(ref Guid name, uint size, nint data);
        [PreserveSig] int SetPrivateDataInterface(ref Guid name, nint unknown);
        [PreserveSig] int GetPrivateData(ref Guid name, ref uint size, nint data);
        [PreserveSig] int GetParent(ref Guid interfaceId, out nint parent);
        [PreserveSig] int GetDevice(ref Guid interfaceId, out nint device);
        [PreserveSig] int Present(uint syncInterval, uint flags);
        [PreserveSig] int GetBuffer(uint buffer, ref Guid interfaceId, out nint surface);
    }

    [ComImport, Guid("DB6F6DDB-AC77-4E88-8253-819DF9BBF140"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ID3D11Device
    {
        [PreserveSig] int CreateBuffer(nint description, nint initialData, out nint buffer);
        [PreserveSig] int CreateTexture1D(nint description, nint initialData, out nint texture);
        [PreserveSig]
        int CreateTexture2D(
            ref Texture2DDescription description,
            ref SubresourceData initialData,
            out nint texture);
    }

    [ComImport, Guid("C0BFA96C-E089-44FB-8EAF-26F8796190DA"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ID3D11DeviceContext
    {
        void GetDevice(out nint device); void GetPrivateData(); void SetPrivateData();
        void SetPrivateDataInterface(); void VSSetConstantBuffers(); void PSSetShaderResources();
        void PSSetShader(); void PSSetSamplers(); void VSSetShader(); void DrawIndexed();
        void Draw(); void Map(); void Unmap(); void PSSetConstantBuffers();
        void IASetInputLayout(); void IASetVertexBuffers(); void IASetIndexBuffer();
        void DrawIndexedInstanced(); void DrawInstanced(); void GSSetConstantBuffers();
        void GSSetShader(); void IASetPrimitiveTopology(); void VSSetShaderResources();
        void VSSetSamplers(); void Begin(); void End(); void GetData(); void SetPredication();
        void GSSetShaderResources(); void GSSetSamplers(); void OMSetRenderTargets();
        void OMSetRenderTargetsAndUnorderedAccessViews(); void OMSetBlendState();
        void OMSetDepthStencilState(); void SOSetTargets(); void DrawAuto();
        void DrawIndexedInstancedIndirect(); void DrawInstancedIndirect(); void Dispatch();
        void DispatchIndirect(); void RSSetState(); void RSSetViewports(); void RSSetScissorRects();
        void CopySubresourceRegion();
        [PreserveSig]
        void CopyResource(nint destination, nint source);
    }
}
