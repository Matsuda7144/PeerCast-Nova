using System.Runtime.InteropServices;

namespace PeerCast.Nova.Playback;

public enum PlaybackDecoderFfmpegDiscontinuityResetResult
{
    Reset,
    Unsupported,
    ContextUnavailable,
    ContextNotOpened,
    GateRejected,
    NativeFailure,
}

internal interface IPlaybackDecoderFfmpegDiscontinuityResetNativeApi
{
    void Flush(nint function, nint context);
}

public sealed class PlaybackDecoderFfmpegDiscontinuityResetBoundary
{
    private const string FlushBuffersSymbol = "avcodec_flush_buffers";

    private readonly IPlaybackDecoderFfmpegDiscontinuityResetNativeApi _nativeApi;
    private readonly bool _isSupportedPlatform;

    public PlaybackDecoderFfmpegDiscontinuityResetBoundary()
        : this(
            new WindowsPlaybackDecoderFfmpegDiscontinuityResetNativeApi(),
            OperatingSystem.IsWindows() && Environment.Is64BitProcess)
    {
    }

    internal PlaybackDecoderFfmpegDiscontinuityResetBoundary(
        IPlaybackDecoderFfmpegDiscontinuityResetNativeApi nativeApi,
        bool isSupportedPlatform)
    {
        ArgumentNullException.ThrowIfNull(nativeApi);
        _nativeApi = nativeApi;
        _isSupportedPlatform = isSupportedPlatform;
    }

    public PlaybackDecoderFfmpegDiscontinuityResetResult Reset(
        PlaybackDecoderFfmpegContextLease contextLease)
    {
        ArgumentNullException.ThrowIfNull(contextLease);

        if (!_isSupportedPlatform)
        {
            return PlaybackDecoderFfmpegDiscontinuityResetResult.Unsupported;
        }

        if (contextLease.IsDisposed)
        {
            return PlaybackDecoderFfmpegDiscontinuityResetResult.ContextUnavailable;
        }

        try
        {
            return contextLease.UseContextAndSymbols((context, symbols) =>
            {
                if (!contextLease.IsOpened)
                {
                    return PlaybackDecoderFfmpegDiscontinuityResetResult.ContextNotOpened;
                }

                if (!symbols.TryGetValue(FlushBuffersSymbol, out var flushBuffers))
                {
                    return PlaybackDecoderFfmpegDiscontinuityResetResult.GateRejected;
                }

                _nativeApi.Flush(flushBuffers, context);
                return PlaybackDecoderFfmpegDiscontinuityResetResult.Reset;
            });
        }
        catch (ObjectDisposedException)
        {
            return PlaybackDecoderFfmpegDiscontinuityResetResult.ContextUnavailable;
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or
            NotSupportedException or
            ArgumentException)
        {
            contextLease.Dispose();
            return PlaybackDecoderFfmpegDiscontinuityResetResult.NativeFailure;
        }
    }
}

internal sealed class WindowsPlaybackDecoderFfmpegDiscontinuityResetNativeApi :
    IPlaybackDecoderFfmpegDiscontinuityResetNativeApi
{
    public void Flush(nint function, nint context)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
        {
            throw new NotSupportedException(
                "The native discontinuity-reset boundary is unavailable.");
        }

        Marshal.GetDelegateForFunctionPointer<FlushBuffersFunction>(function)(context);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FlushBuffersFunction(nint context);
}
