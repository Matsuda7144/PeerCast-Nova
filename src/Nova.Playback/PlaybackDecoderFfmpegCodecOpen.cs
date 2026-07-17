using System.Runtime.InteropServices;

namespace PeerCast.Nova.Playback;

public enum PlaybackDecoderFfmpegCodecOpenResult
{
    Opened,
    Unsupported,
    ContextUnavailable,
    ParametersNotTransferred,
    AlreadyOpened,
    GateRejected,
    NativeOpenRejected,
    NativeFailure,
}

internal interface IPlaybackDecoderFfmpegCodecOpenNativeApi
{
    int Open(nint function, nint context);
}

public sealed class PlaybackDecoderFfmpegCodecOpen
{
    private const string OpenCodecSymbol = "avcodec_open2";

    private readonly IPlaybackDecoderFfmpegCodecOpenNativeApi _nativeApi;
    private readonly bool _isSupportedPlatform;

    public PlaybackDecoderFfmpegCodecOpen()
        : this(
            new WindowsPlaybackDecoderFfmpegCodecOpenNativeApi(),
            OperatingSystem.IsWindows() && Environment.Is64BitProcess)
    {
    }

    internal PlaybackDecoderFfmpegCodecOpen(
        IPlaybackDecoderFfmpegCodecOpenNativeApi nativeApi,
        bool isSupportedPlatform)
    {
        ArgumentNullException.ThrowIfNull(nativeApi);
        _nativeApi = nativeApi;
        _isSupportedPlatform = isSupportedPlatform;
    }

    public PlaybackDecoderFfmpegCodecOpenResult Open(
        PlaybackDecoderFfmpegContextLease contextLease)
    {
        ArgumentNullException.ThrowIfNull(contextLease);

        if (!_isSupportedPlatform)
        {
            return PlaybackDecoderFfmpegCodecOpenResult.Unsupported;
        }

        if (contextLease.IsDisposed)
        {
            return PlaybackDecoderFfmpegCodecOpenResult.ContextUnavailable;
        }

        PlaybackDecoderFfmpegCodecOpenResult result;
        try
        {
            result = contextLease.UseContextAndSymbols((context, symbols) =>
            {
                if (contextLease.IsOpened)
                {
                    return PlaybackDecoderFfmpegCodecOpenResult.AlreadyOpened;
                }

                if (!contextLease.HasTransferredParameters)
                {
                    return PlaybackDecoderFfmpegCodecOpenResult.ParametersNotTransferred;
                }

                if (!symbols.TryGetValue(OpenCodecSymbol, out var openCodec))
                {
                    return PlaybackDecoderFfmpegCodecOpenResult.GateRejected;
                }

                if (_nativeApi.Open(openCodec, context) != 0)
                {
                    return PlaybackDecoderFfmpegCodecOpenResult.NativeOpenRejected;
                }

                contextLease.MarkOpened();
                return PlaybackDecoderFfmpegCodecOpenResult.Opened;
            });
        }
        catch (ObjectDisposedException)
        {
            return PlaybackDecoderFfmpegCodecOpenResult.ContextUnavailable;
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or
            NotSupportedException or
            ArgumentException)
        {
            contextLease.Dispose();
            return PlaybackDecoderFfmpegCodecOpenResult.NativeFailure;
        }

        if (result == PlaybackDecoderFfmpegCodecOpenResult.NativeOpenRejected)
        {
            contextLease.Dispose();
        }

        return result;
    }
}

internal sealed class WindowsPlaybackDecoderFfmpegCodecOpenNativeApi :
    IPlaybackDecoderFfmpegCodecOpenNativeApi
{
    public int Open(nint function, nint context)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
        {
            throw new NotSupportedException("The native codec-open boundary is unavailable.");
        }

        return Marshal.GetDelegateForFunctionPointer<OpenCodecFunction>(function)(
            context,
            codec: 0,
            options: 0);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int OpenCodecFunction(nint context, nint codec, nint options);
}
