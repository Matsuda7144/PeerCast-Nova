using System.Runtime.InteropServices;

namespace PeerCast.Nova.Playback;

public enum PlaybackDecoderFfmpegNativeFrameResult
{
    Processed,
    NeedMoreInput,
    EndOfStream,
    Unsupported,
    ContextUnavailable,
    ContextNotOpened,
    GateRejected,
    NativeAllocationFailed,
    NativeRejected,
    ConsumerRejected,
    NativeFailure,
}

internal enum PlaybackDecoderFfmpegNativeFrameReceiveResult
{
    Success,
    Again,
    EndOfStream,
    AllocationFailed,
    Rejected,
}

internal interface IPlaybackDecoderFfmpegNativeFrameConsumer
{
    bool Consume(nint frame);
}

internal interface IPlaybackDecoderFfmpegNativeFrameApi
{
    PlaybackDecoderFfmpegNativeFrameReceiveResult ReceiveAndConsume(
        nint frameAllocFunction,
        nint frameFreeFunction,
        nint receiveFrameFunction,
        nint context,
        IPlaybackDecoderFfmpegNativeFrameConsumer consumer);
}

public sealed class PlaybackDecoderFfmpegNativeFrameBoundary
{
    private const string FrameAllocSymbol = "av_frame_alloc";
    private const string FrameFreeSymbol = "av_frame_free";
    private const string ReceiveFrameSymbol = "avcodec_receive_frame";

    private readonly IPlaybackDecoderFfmpegNativeFrameApi _nativeApi;
    private readonly IPlaybackDecoderFfmpegNativeFrameConsumer _consumer;
    private readonly bool _isSupportedPlatform;

    public PlaybackDecoderFfmpegNativeFrameBoundary()
        : this(
            new WindowsPlaybackDecoderFfmpegNativeFrameApi(),
            new RejectingPlaybackDecoderFfmpegNativeFrameConsumer(),
            OperatingSystem.IsWindows() && Environment.Is64BitProcess)
    {
    }

    internal PlaybackDecoderFfmpegNativeFrameBoundary(
        IPlaybackDecoderFfmpegNativeFrameApi nativeApi,
        IPlaybackDecoderFfmpegNativeFrameConsumer consumer,
        bool isSupportedPlatform)
    {
        ArgumentNullException.ThrowIfNull(nativeApi);
        ArgumentNullException.ThrowIfNull(consumer);
        _nativeApi = nativeApi;
        _consumer = consumer;
        _isSupportedPlatform = isSupportedPlatform;
    }

    public PlaybackDecoderFfmpegNativeFrameResult ReceiveAndProcess(
        PlaybackDecoderFfmpegContextLease contextLease)
    {
        ArgumentNullException.ThrowIfNull(contextLease);
        if (!_isSupportedPlatform)
        {
            return PlaybackDecoderFfmpegNativeFrameResult.Unsupported;
        }

        if (contextLease.IsDisposed)
        {
            return PlaybackDecoderFfmpegNativeFrameResult.ContextUnavailable;
        }

        try
        {
            return contextLease.UseContextAndSymbols((context, symbols) =>
            {
                if (!contextLease.IsOpened)
                {
                    return PlaybackDecoderFfmpegNativeFrameResult.ContextNotOpened;
                }

                if (!symbols.TryGetValue(FrameAllocSymbol, out var frameAlloc) ||
                    !symbols.TryGetValue(FrameFreeSymbol, out var frameFree) ||
                    !symbols.TryGetValue(ReceiveFrameSymbol, out var receiveFrame))
                {
                    return PlaybackDecoderFfmpegNativeFrameResult.GateRejected;
                }

                return _nativeApi.ReceiveAndConsume(
                    frameAlloc,
                    frameFree,
                    receiveFrame,
                    context,
                    _consumer) switch
                {
                    PlaybackDecoderFfmpegNativeFrameReceiveResult.Success =>
                        PlaybackDecoderFfmpegNativeFrameResult.Processed,
                    PlaybackDecoderFfmpegNativeFrameReceiveResult.Again =>
                        PlaybackDecoderFfmpegNativeFrameResult.NeedMoreInput,
                    PlaybackDecoderFfmpegNativeFrameReceiveResult.EndOfStream =>
                        PlaybackDecoderFfmpegNativeFrameResult.EndOfStream,
                    PlaybackDecoderFfmpegNativeFrameReceiveResult.AllocationFailed =>
                        PlaybackDecoderFfmpegNativeFrameResult.NativeAllocationFailed,
                    _ => PlaybackDecoderFfmpegNativeFrameResult.NativeRejected,
                };
            });
        }
        catch (PlaybackDecoderFfmpegFrameConsumerRejectedException)
        {
            return PlaybackDecoderFfmpegNativeFrameResult.ConsumerRejected;
        }
        catch (ObjectDisposedException)
        {
            return PlaybackDecoderFfmpegNativeFrameResult.ContextUnavailable;
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or NotSupportedException)
        {
            contextLease.Dispose();
            return PlaybackDecoderFfmpegNativeFrameResult.NativeFailure;
        }
    }
}

internal sealed class PlaybackDecoderFfmpegFrameConsumerRejectedException : Exception
{
}

internal sealed class RejectingPlaybackDecoderFfmpegNativeFrameConsumer :
    IPlaybackDecoderFfmpegNativeFrameConsumer
{
    public bool Consume(nint frame) => false;
}

internal sealed class WindowsPlaybackDecoderFfmpegNativeFrameApi :
    IPlaybackDecoderFfmpegNativeFrameApi
{
    private const int Again = -11;
    private const int EndOfFile = -541478725;

    public PlaybackDecoderFfmpegNativeFrameReceiveResult ReceiveAndConsume(
        nint frameAllocFunction,
        nint frameFreeFunction,
        nint receiveFrameFunction,
        nint context,
        IPlaybackDecoderFfmpegNativeFrameConsumer consumer)
    {
        EnsureSupported();
        var frameAlloc = Marshal.GetDelegateForFunctionPointer<PointerFunction>(frameAllocFunction);
        var frameFree = Marshal.GetDelegateForFunctionPointer<FreePointerFunction>(frameFreeFunction);
        var receiveFrame = Marshal.GetDelegateForFunctionPointer<CodecIoFunction>(receiveFrameFunction);
        var frame = frameAlloc();
        if (frame == 0)
        {
            return PlaybackDecoderFfmpegNativeFrameReceiveResult.AllocationFailed;
        }

        try
        {
            var result = receiveFrame(context, frame);
            if (result == 0)
            {
                if (!consumer.Consume(frame))
                {
                    throw new PlaybackDecoderFfmpegFrameConsumerRejectedException();
                }

                return PlaybackDecoderFfmpegNativeFrameReceiveResult.Success;
            }

            return result switch
            {
                Again => PlaybackDecoderFfmpegNativeFrameReceiveResult.Again,
                EndOfFile => PlaybackDecoderFfmpegNativeFrameReceiveResult.EndOfStream,
                _ => PlaybackDecoderFfmpegNativeFrameReceiveResult.Rejected,
            };
        }
        finally
        {
            frameFree(ref frame);
        }
    }

    private static void EnsureSupported()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
        {
            throw new NotSupportedException("The native frame boundary is unavailable.");
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint PointerFunction();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FreePointerFunction(ref nint pointer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CodecIoFunction(nint context, nint frame);
}
