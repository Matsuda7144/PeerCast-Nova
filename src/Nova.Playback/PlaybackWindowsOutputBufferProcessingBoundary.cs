using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PeerCast.Nova.Playback;

public enum PlaybackWindowsOutputBufferState
{
    Ready,
    DeviceLost,
    Disposed,
}

public enum PlaybackWindowsOutputBufferProcessingResult
{
    Processed,
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

public enum PlaybackWindowsOutputBufferResetResult
{
    Reset,
    DeviceLost,
    Cancelled,
    Disposed,
    Failure,
}

public readonly record struct PlaybackWindowsVideoFrameBuffer(
    PlaybackDecodedVideoPixelFormat PixelFormat,
    int Width,
    int Height,
    int Stride,
    ReadOnlyMemory<byte> Buffer,
    long Generation);

public readonly record struct PlaybackWindowsAudioSampleBuffer(
    PlaybackDecodedAudioSampleFormat SampleFormat,
    int SampleRate,
    int ChannelCount,
    int FrameCount,
    ReadOnlyMemory<byte> Buffer,
    long Generation);

public readonly record struct PlaybackWindowsOutputBufferSnapshot(
    PlaybackWindowsOutputBufferState VideoState,
    PlaybackWindowsOutputBufferState AudioState,
    long VideoGeneration,
    long AudioGeneration,
    bool AudioStarted);

public sealed class PlaybackWindowsOutputBufferProcessingBoundary : IDisposable
{
    public const int MaximumBufferBytes = 256 * 1024 * 1024;
    private readonly SemaphoreSlim _videoGate = new(1, 1);
    private readonly SemaphoreSlim _audioGate = new(1, 1);
    private readonly PlaybackWindowsOutputActivationBoundary _activation;
    private readonly PlaybackWindowsOutputResourceBindingBoundary _binding;
    private readonly PlaybackWindowsOutputStreamInitializationBoundary _initialization;
    private readonly IPlaybackWindowsOutputNativeBufferProcessor _native;
    private readonly Func<bool> _isSupportedPlatform;
    private PlaybackWindowsOutputBufferState _videoState;
    private PlaybackWindowsOutputBufferState _audioState;
    private long _videoGeneration;
    private long _audioGeneration;
    private bool _audioStarted;
    private volatile bool _isDisposed;

    public PlaybackWindowsOutputBufferProcessingBoundary(
        PlaybackWindowsOutputActivationBoundary activation,
        PlaybackWindowsOutputResourceBindingBoundary binding,
        PlaybackWindowsOutputStreamInitializationBoundary initialization)
        : this(activation, binding, initialization, CreateSystemProcessor(),
            static () => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22_000))
    {
    }

    internal PlaybackWindowsOutputBufferProcessingBoundary(
        PlaybackWindowsOutputActivationBoundary activation,
        PlaybackWindowsOutputResourceBindingBoundary binding,
        PlaybackWindowsOutputStreamInitializationBoundary initialization,
        IPlaybackWindowsOutputNativeBufferProcessor native,
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
        var snapshot = initialization.Snapshot;
        _videoGeneration = snapshot.VideoGeneration;
        _audioGeneration = snapshot.AudioGeneration;
    }

    public PlaybackWindowsOutputBufferSnapshot Snapshot => new(
        _videoState, _audioState, _videoGeneration, _audioGeneration, _audioStarted);

    public async ValueTask<PlaybackWindowsOutputBufferProcessingResult> ProcessVideoAsync(
        PlaybackWindowsVideoFrameBuffer frame, CancellationToken cancellationToken)
    {
        if (_isDisposed) return PlaybackWindowsOutputBufferProcessingResult.Disposed;
        var validation = ValidateVideo(frame);
        if (validation != PlaybackWindowsOutputBufferProcessingResult.Processed) return validation;
        if (!await EnterAsync(_videoGate, cancellationToken).ConfigureAwait(false))
            return PlaybackWindowsOutputBufferProcessingResult.Cancelled;
        try
        {
            if (_isDisposed) return PlaybackWindowsOutputBufferProcessingResult.Disposed;
            var prerequisite = ValidatePrerequisites(video: true, frame.Generation);
            if (prerequisite != PlaybackWindowsOutputBufferProcessingResult.Processed) return prerequisite;
            var borrowed = _activation.BorrowActivatedResources(resources =>
                MapNative(_native.ProcessVideo(resources.Direct3D11, frame)));
            return MapBorrow(borrowed, video: true);
        }
        catch { return PlaybackWindowsOutputBufferProcessingResult.Failure; }
        finally { _videoGate.Release(); }
    }

    public async ValueTask<PlaybackWindowsOutputBufferProcessingResult> ProcessAudioAsync(
        PlaybackWindowsAudioSampleBuffer samples, CancellationToken cancellationToken)
    {
        if (_isDisposed) return PlaybackWindowsOutputBufferProcessingResult.Disposed;
        var validation = ValidateAudio(samples);
        if (validation != PlaybackWindowsOutputBufferProcessingResult.Processed) return validation;
        if (!await EnterAsync(_audioGate, cancellationToken).ConfigureAwait(false))
            return PlaybackWindowsOutputBufferProcessingResult.Cancelled;
        try
        {
            if (_isDisposed) return PlaybackWindowsOutputBufferProcessingResult.Disposed;
            var prerequisite = ValidatePrerequisites(video: false, samples.Generation);
            if (prerequisite != PlaybackWindowsOutputBufferProcessingResult.Processed) return prerequisite;
            var start = !_audioStarted;
            var borrowed = _activation.BorrowActivatedResources(resources =>
                MapNative(_native.ProcessAudio(resources.Wasapi, samples, start)));
            var result = MapBorrow(borrowed, video: false);
            if (result == PlaybackWindowsOutputBufferProcessingResult.Processed && start)
                _audioStarted = true;
            return result;
        }
        catch { return PlaybackWindowsOutputBufferProcessingResult.Failure; }
        finally { _audioGate.Release(); }
    }

    public ValueTask<PlaybackWindowsOutputBufferResetResult> ResetVideoAsync(
        long generation, CancellationToken cancellationToken) =>
        ResetAsync(generation, video: true, cancellationToken);

    public ValueTask<PlaybackWindowsOutputBufferResetResult> ResetAudioAsync(
        long generation, CancellationToken cancellationToken) =>
        ResetAsync(generation, video: false, cancellationToken);

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _audioGate.Wait();
        _videoGate.Wait();
        try
        {
            ResetBorrowed(video: false);
            ResetBorrowed(video: true);
            _audioStarted = false;
            _audioState = PlaybackWindowsOutputBufferState.Disposed;
            _videoState = PlaybackWindowsOutputBufferState.Disposed;
        }
        finally
        {
            _videoGate.Release();
            _audioGate.Release();
        }
    }

    private PlaybackWindowsOutputBufferProcessingResult ValidatePrerequisites(
        bool video, long generation)
    {
        if (!_isSupportedPlatform()) return PlaybackWindowsOutputBufferProcessingResult.InvalidEnvironment;
        var stream = _initialization.Snapshot;
        var streamState = video ? stream.VideoState : stream.AudioState;
        var streamGeneration = video ? stream.VideoGeneration : stream.AudioGeneration;
        if (streamState != PlaybackWindowsOutputStreamState.Initialized)
            return PlaybackWindowsOutputBufferProcessingResult.InitializationRequired;
        if ((video ? _videoGeneration : _audioGeneration) != generation)
            return PlaybackWindowsOutputBufferProcessingResult.InvalidConfiguration;
        var binding = _binding.Snapshot;
        var bindingState = video ? binding.VideoState : binding.AudioState;
        var bindingGeneration = video ? binding.VideoGeneration : binding.AudioGeneration;
        if (bindingState != PlaybackWindowsOutputBindingState.Bound || bindingGeneration != generation)
            return PlaybackWindowsOutputBufferProcessingResult.BindingRequired;
        if (streamGeneration != generation)
            return PlaybackWindowsOutputBufferProcessingResult.InitializationRequired;
        return PlaybackWindowsOutputBufferProcessingResult.Processed;
    }

    private async ValueTask<PlaybackWindowsOutputBufferResetResult> ResetAsync(
        long generation, bool video, CancellationToken cancellationToken)
    {
        if (_isDisposed) return PlaybackWindowsOutputBufferResetResult.Disposed;
        if (generation < 0) return PlaybackWindowsOutputBufferResetResult.Failure;
        var gate = video ? _videoGate : _audioGate;
        if (!await EnterAsync(gate, cancellationToken).ConfigureAwait(false))
            return PlaybackWindowsOutputBufferResetResult.Cancelled;
        try
        {
            if (_isDisposed) return PlaybackWindowsOutputBufferResetResult.Disposed;
            var lost = (video ? _videoState : _audioState) == PlaybackWindowsOutputBufferState.DeviceLost;
            ResetBorrowed(video);
            if (video)
            {
                _videoState = PlaybackWindowsOutputBufferState.Ready;
                _videoGeneration = generation;
            }
            else
            {
                _audioState = PlaybackWindowsOutputBufferState.Ready;
                _audioGeneration = generation;
                _audioStarted = false;
            }
            return lost ? PlaybackWindowsOutputBufferResetResult.DeviceLost :
                PlaybackWindowsOutputBufferResetResult.Reset;
        }
        catch { return PlaybackWindowsOutputBufferResetResult.Failure; }
        finally { gate.Release(); }
    }

    private void ResetBorrowed(bool video) => _activation.BorrowActivatedResources(resources =>
    {
        try
        {
            if (video) _native.ResetVideo(resources.Direct3D11);
            else _native.ResetAudio(resources.Wasapi);
        }
        catch { }
        return PlaybackWindowsOutputResourceBorrowResult.Success;
    });

    private PlaybackWindowsOutputBufferProcessingResult MapBorrow(
        PlaybackWindowsOutputResourceBorrowResult result, bool video)
    {
        var mapped = result switch
        {
            PlaybackWindowsOutputResourceBorrowResult.Success => PlaybackWindowsOutputBufferProcessingResult.Processed,
            PlaybackWindowsOutputResourceBorrowResult.WouldBlock => PlaybackWindowsOutputBufferProcessingResult.WouldBlock,
            PlaybackWindowsOutputResourceBorrowResult.ActivationRequired => PlaybackWindowsOutputBufferProcessingResult.InitializationRequired,
            PlaybackWindowsOutputResourceBorrowResult.DeviceLost => PlaybackWindowsOutputBufferProcessingResult.DeviceLost,
            PlaybackWindowsOutputResourceBorrowResult.FormatUnsupported => PlaybackWindowsOutputBufferProcessingResult.FormatUnsupported,
            PlaybackWindowsOutputResourceBorrowResult.ResizeRequired => PlaybackWindowsOutputBufferProcessingResult.ResizeRequired,
            PlaybackWindowsOutputResourceBorrowResult.InvalidEnvironment => PlaybackWindowsOutputBufferProcessingResult.InvalidEnvironment,
            PlaybackWindowsOutputResourceBorrowResult.Disposed => PlaybackWindowsOutputBufferProcessingResult.Disposed,
            _ => PlaybackWindowsOutputBufferProcessingResult.Failure,
        };
        if (mapped == PlaybackWindowsOutputBufferProcessingResult.DeviceLost)
        {
            if (video) _videoState = PlaybackWindowsOutputBufferState.DeviceLost;
            else _audioState = PlaybackWindowsOutputBufferState.DeviceLost;
        }
        return mapped;
    }

    private static PlaybackWindowsOutputResourceBorrowResult MapNative(
        PlaybackWindowsOutputNativeBufferResult result) => result switch
        {
            PlaybackWindowsOutputNativeBufferResult.Processed => PlaybackWindowsOutputResourceBorrowResult.Success,
            PlaybackWindowsOutputNativeBufferResult.WouldBlock => PlaybackWindowsOutputResourceBorrowResult.WouldBlock,
            PlaybackWindowsOutputNativeBufferResult.FormatUnsupported => PlaybackWindowsOutputResourceBorrowResult.FormatUnsupported,
            PlaybackWindowsOutputNativeBufferResult.ResizeRequired => PlaybackWindowsOutputResourceBorrowResult.ResizeRequired,
            PlaybackWindowsOutputNativeBufferResult.DeviceLost => PlaybackWindowsOutputResourceBorrowResult.DeviceLost,
            _ => PlaybackWindowsOutputResourceBorrowResult.Failure,
        };

    private static PlaybackWindowsOutputBufferProcessingResult ValidateVideo(
        PlaybackWindowsVideoFrameBuffer value)
    {
        if (!Enum.IsDefined(value.PixelFormat)) return PlaybackWindowsOutputBufferProcessingResult.Failure;
        if (value.PixelFormat != PlaybackDecodedVideoPixelFormat.Bgra32)
            return PlaybackWindowsOutputBufferProcessingResult.FormatUnsupported;
        if (value.Width <= 0 || value.Height <= 0 || value.Generation < 0 ||
            value.Stride != (long)value.Width * 4 || value.Buffer.Length <= 0 ||
            value.Buffer.Length > MaximumBufferBytes ||
            (long)value.Stride * value.Height != value.Buffer.Length)
            return PlaybackWindowsOutputBufferProcessingResult.InvalidConfiguration;
        return PlaybackWindowsOutputBufferProcessingResult.Processed;
    }

    private static PlaybackWindowsOutputBufferProcessingResult ValidateAudio(
        PlaybackWindowsAudioSampleBuffer value)
    {
        if (!Enum.IsDefined(value.SampleFormat)) return PlaybackWindowsOutputBufferProcessingResult.Failure;
        if (value.SampleFormat is not (PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved or
            PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved))
            return PlaybackWindowsOutputBufferProcessingResult.FormatUnsupported;
        var bytes = value.SampleFormat == PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved ? 2 : 4;
        if (value.SampleRate <= 0 || value.ChannelCount <= 0 || value.FrameCount <= 0 ||
            value.Generation < 0 || value.Buffer.Length <= 0 || value.Buffer.Length > MaximumBufferBytes ||
            (long)value.FrameCount * value.ChannelCount * bytes != value.Buffer.Length)
            return PlaybackWindowsOutputBufferProcessingResult.InvalidConfiguration;
        return PlaybackWindowsOutputBufferProcessingResult.Processed;
    }

    private static async ValueTask<bool> EnterAsync(
        SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        try { await gate.WaitAsync(cancellationToken).ConfigureAwait(false); return true; }
        catch (OperationCanceledException) { return false; }
    }

    private static IPlaybackWindowsOutputNativeBufferProcessor CreateSystemProcessor() =>
        OperatingSystem.IsWindows() ? new SystemPlaybackWindowsOutputNativeBufferProcessor() :
            new UnsupportedPlaybackWindowsOutputNativeBufferProcessor();
}

internal enum PlaybackWindowsOutputNativeBufferResult
{
    Processed,
    WouldBlock,
    FormatUnsupported,
    ResizeRequired,
    DeviceLost,
    Failure,
}

internal interface IPlaybackWindowsOutputNativeBufferProcessor
{
    PlaybackWindowsOutputNativeBufferResult ProcessVideo(
        IDisposable borrowedResource, PlaybackWindowsVideoFrameBuffer frame);
    PlaybackWindowsOutputNativeBufferResult ProcessAudio(
        IDisposable borrowedResource, PlaybackWindowsAudioSampleBuffer samples, bool startStream);
    void ResetVideo(IDisposable borrowedResource);
    void ResetAudio(IDisposable borrowedResource);
}

internal sealed class UnsupportedPlaybackWindowsOutputNativeBufferProcessor :
    IPlaybackWindowsOutputNativeBufferProcessor
{
    public PlaybackWindowsOutputNativeBufferResult ProcessVideo(IDisposable borrowedResource, PlaybackWindowsVideoFrameBuffer frame) => PlaybackWindowsOutputNativeBufferResult.Failure;
    public PlaybackWindowsOutputNativeBufferResult ProcessAudio(IDisposable borrowedResource, PlaybackWindowsAudioSampleBuffer samples, bool startStream) => PlaybackWindowsOutputNativeBufferResult.Failure;
    public void ResetVideo(IDisposable borrowedResource) { }
    public void ResetAudio(IDisposable borrowedResource) { }
}

[SupportedOSPlatform("windows")]
internal sealed class SystemPlaybackWindowsOutputNativeBufferProcessor :
    IPlaybackWindowsOutputNativeBufferProcessor
{
    private const int DxgiStatusOccluded = 0x087A0001;
    private readonly IPlaybackWindowsOutputNativeBufferCalls _calls;

    public SystemPlaybackWindowsOutputNativeBufferProcessor()
        : this(new SystemPlaybackWindowsOutputNativeBufferCalls()) { }

    internal SystemPlaybackWindowsOutputNativeBufferProcessor(
        IPlaybackWindowsOutputNativeBufferCalls calls) => _calls = calls;

    public PlaybackWindowsOutputNativeBufferResult ProcessVideo(
        IDisposable borrowedResource, PlaybackWindowsVideoFrameBuffer frame)
    {
        if (borrowedResource is not IPlaybackWindowsDirect3D11BorrowedResource resource ||
            resource.ImmediateContext == 0 || resource.SwapChain == 0 ||
            resource.RenderTargetView == 0) return PlaybackWindowsOutputNativeBufferResult.Failure;
        try { return Normalize(_calls.UploadAndPresent(resource, frame)); }
        catch { return PlaybackWindowsOutputNativeBufferResult.Failure; }
    }

    public PlaybackWindowsOutputNativeBufferResult ProcessAudio(
        IDisposable borrowedResource, PlaybackWindowsAudioSampleBuffer samples, bool startStream)
    {
        if (borrowedResource is not IPlaybackWindowsWasapiBorrowedResource resource ||
            resource.AudioClient == 0 || !resource.EnsureAudioRenderClient() ||
            resource.AudioRenderClient == 0) return PlaybackWindowsOutputNativeBufferResult.Failure;
        try { return Normalize(_calls.WriteAudio(resource, samples, startStream)); }
        catch { return PlaybackWindowsOutputNativeBufferResult.Failure; }
    }

    public void ResetVideo(IDisposable borrowedResource) { }
    public void ResetAudio(IDisposable borrowedResource)
    {
        if (borrowedResource is IPlaybackWindowsWasapiBorrowedResource resource)
            _calls.StopAudio(resource);
    }

    internal static PlaybackWindowsOutputNativeBufferResult Normalize(int hresult)
    {
        if (hresult >= 0) return hresult == DxgiStatusOccluded ?
            PlaybackWindowsOutputNativeBufferResult.WouldBlock :
            PlaybackWindowsOutputNativeBufferResult.Processed;
        return hresult switch
        {
            unchecked((int)0x887A0001) => PlaybackWindowsOutputNativeBufferResult.ResizeRequired,
            unchecked((int)0x887A0005) or unchecked((int)0x887A0006) or
            unchecked((int)0x887A0007) or unchecked((int)0x88890004) =>
                PlaybackWindowsOutputNativeBufferResult.DeviceLost,
            unchecked((int)0x88890008) => PlaybackWindowsOutputNativeBufferResult.FormatUnsupported,
            unchecked((int)0x88890006) => PlaybackWindowsOutputNativeBufferResult.WouldBlock,
            _ => PlaybackWindowsOutputNativeBufferResult.Failure,
        };
    }
}

internal interface IPlaybackWindowsOutputNativeBufferCalls
{
    int UploadAndPresent(IPlaybackWindowsDirect3D11BorrowedResource resource,
        PlaybackWindowsVideoFrameBuffer frame);
    int WriteAudio(IPlaybackWindowsWasapiBorrowedResource resource,
        PlaybackWindowsAudioSampleBuffer samples, bool startStream);
    void StopAudio(IPlaybackWindowsWasapiBorrowedResource resource);
}

[SupportedOSPlatform("windows")]
internal sealed class SystemPlaybackWindowsOutputNativeBufferCalls :
    IPlaybackWindowsOutputNativeBufferCalls
{
    public int UploadAndPresent(IPlaybackWindowsDirect3D11BorrowedResource resource,
        PlaybackWindowsVideoFrameBuffer frame)
    {
        nint backBuffer = 0;
        try
        {
            var swapChain = (IDXGISwapChain)Marshal.GetObjectForIUnknown(resource.SwapChain);
            try
            {
                var textureId = D3D11Texture2DInterface;
                var result = swapChain.GetBuffer(0, ref textureId, out backBuffer);
                if (result < 0) return result;
                var context = (ID3D11DeviceContext)Marshal.GetObjectForIUnknown(resource.ImmediateContext);
                try
                {
                    var managed = frame.Buffer.ToArray();
                    var pinned = GCHandle.Alloc(managed, GCHandleType.Pinned);
                    try
                    {
                        context.UpdateSubresource(backBuffer, 0, 0,
                            pinned.AddrOfPinnedObject(), (uint)frame.Stride, 0);
                    }
                    finally { pinned.Free(); }
                    var target = resource.RenderTargetView;
                    context.OMSetRenderTargets(1, ref target, 0);
                }
                finally { Marshal.FinalReleaseComObject(context); }
                return swapChain.Present(1, 0);
            }
            finally { Marshal.FinalReleaseComObject(swapChain); }
        }
        finally { if (backBuffer != 0) Marshal.Release(backBuffer); }
    }

    public int WriteAudio(IPlaybackWindowsWasapiBorrowedResource resource,
        PlaybackWindowsAudioSampleBuffer samples, bool startStream)
    {
        var client = (IAudioClient)Marshal.GetObjectForIUnknown(resource.AudioClient);
        var render = (IAudioRenderClient)Marshal.GetObjectForIUnknown(resource.AudioRenderClient);
        try
        {
            var result = client.GetBufferSize(out var capacity);
            if (result < 0) return result;
            result = client.GetCurrentPadding(out var padding);
            if (result < 0) return result;
            if (padding > capacity || samples.FrameCount > capacity - padding)
                return unchecked((int)0x88890006);
            result = render.GetBuffer((uint)samples.FrameCount, out var buffer);
            if (result < 0) return result;
            var releaseFrames = 0u;
            try
            {
                Marshal.Copy(samples.Buffer.ToArray(), 0, buffer, samples.Buffer.Length);
                releaseFrames = (uint)samples.FrameCount;
            }
            finally
            {
                var release = render.ReleaseBuffer(releaseFrames, 0);
                if (result >= 0) result = release;
            }
            if (result < 0 || !startStream) return result;
            return client.Start();
        }
        finally
        {
            Marshal.FinalReleaseComObject(render);
            Marshal.FinalReleaseComObject(client);
        }
    }

    public void StopAudio(IPlaybackWindowsWasapiBorrowedResource resource)
    {
        if (resource.AudioClient == 0) return;
        var client = (IAudioClient)Marshal.GetObjectForIUnknown(resource.AudioClient);
        try { client.Stop(); }
        finally { Marshal.FinalReleaseComObject(client); }
    }

    private static readonly Guid D3D11Texture2DInterface =
        new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

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

    [ComImport, Guid("C0BFA96C-E089-44FB-8EAF-26F8796190DA"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ID3D11DeviceContext
    {
        void GetDevice(out nint device); void GetPrivateData(); void SetPrivateData(); void SetPrivateDataInterface();
        void VSSetConstantBuffers(); void PSSetShaderResources(); void PSSetShader(); void PSSetSamplers(); void VSSetShader();
        void DrawIndexed(); void Draw(); void Map(); void Unmap(); void PSSetConstantBuffers(); void IASetInputLayout();
        void IASetVertexBuffers(); void IASetIndexBuffer(); void DrawIndexedInstanced(); void DrawInstanced();
        void GSSetConstantBuffers(); void GSSetShader(); void IASetPrimitiveTopology(); void VSSetShaderResources();
        void VSSetSamplers(); void Begin(); void End(); void GetData(); void SetPredication(); void GSSetShaderResources();
        void GSSetSamplers();
        void OMSetRenderTargets(uint count, ref nint renderTargetViews, nint depthStencilView);
        void OMSetRenderTargetsAndUnorderedAccessViews(); void SOSetTargets(); void DrawAuto();
        void DrawIndexedInstancedIndirect(); void DrawInstancedIndirect(); void Dispatch(); void DispatchIndirect();
        void RSSetState(); void RSSetViewports(); void RSSetScissorRects(); void CopySubresourceRegion(); void CopyResource();
        void UpdateSubresource(nint destination, uint subresource, nint box,
            nint source, uint rowPitch, uint depthPitch);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(uint shareMode, uint flags, long duration, long period, nint format, nint session);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(uint shareMode, nint format, out nint closest);
        [PreserveSig] int GetMixFormat(out nint format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
    }

    [ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioRenderClient
    {
        [PreserveSig] int GetBuffer(uint frames, out nint data);
        [PreserveSig] int ReleaseBuffer(uint frames, uint flags);
    }
}
