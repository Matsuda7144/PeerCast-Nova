using System.Runtime.InteropServices;

namespace PeerCast.Nova.Playback;

public enum PlaybackDecoderFfmpegPacketSendResult
{
    Sent,
    NeedFrameDrain,
    EndOfStream,
    Unsupported,
    ContextUnavailable,
    ContextNotOpened,
    EmptyPacket,
    PacketTooLarge,
    GateRejected,
    NativeAllocationFailed,
    NativeRejected,
    NativeFailure,
}

public enum PlaybackDecoderFfmpegFrameReceiveResult
{
    FrameAvailable,
    NeedMoreInput,
    EndOfStream,
    Unsupported,
    ContextUnavailable,
    ContextNotOpened,
    GateRejected,
    NativeAllocationFailed,
    NativeRejected,
    NativeFailure,
}

internal enum PlaybackDecoderFfmpegNativeIoResult
{
    Success,
    Again,
    EndOfStream,
    AllocationFailed,
    Rejected,
}

internal interface IPlaybackDecoderFfmpegPacketFrameNativeApi
{
    PlaybackDecoderFfmpegNativeIoResult SendPacket(
        nint packetAllocFunction,
        nint packetFreeFunction,
        nint memoryAllocFunction,
        nint memoryFreeFunction,
        nint packetFromDataFunction,
        nint sendPacketFunction,
        nint context,
        ReadOnlyMemory<byte> packet);

    PlaybackDecoderFfmpegNativeIoResult ReceiveFrame(
        nint frameAllocFunction,
        nint frameFreeFunction,
        nint receiveFrameFunction,
        nint context);
}

public sealed class PlaybackDecoderFfmpegPacketFrameBoundary
{
    public const int MaximumPacketBytes = 4 * 1024 * 1024;

    private const string PacketAllocSymbol = "av_packet_alloc";
    private const string PacketFreeSymbol = "av_packet_free";
    private const string MemoryAllocSymbol = "av_malloc";
    private const string MemoryFreeSymbol = "av_free";
    private const string PacketFromDataSymbol = "av_packet_from_data";
    private const string SendPacketSymbol = "avcodec_send_packet";
    private const string FrameAllocSymbol = "av_frame_alloc";
    private const string FrameFreeSymbol = "av_frame_free";
    private const string ReceiveFrameSymbol = "avcodec_receive_frame";

    private readonly IPlaybackDecoderFfmpegPacketFrameNativeApi _nativeApi;
    private readonly bool _isSupportedPlatform;

    public PlaybackDecoderFfmpegPacketFrameBoundary()
        : this(
            new WindowsPlaybackDecoderFfmpegPacketFrameNativeApi(),
            OperatingSystem.IsWindows() && Environment.Is64BitProcess)
    {
    }

    internal PlaybackDecoderFfmpegPacketFrameBoundary(
        IPlaybackDecoderFfmpegPacketFrameNativeApi nativeApi,
        bool isSupportedPlatform)
    {
        ArgumentNullException.ThrowIfNull(nativeApi);
        _nativeApi = nativeApi;
        _isSupportedPlatform = isSupportedPlatform;
    }

    public PlaybackDecoderFfmpegPacketSendResult SendPacket(
        PlaybackDecoderFfmpegContextLease contextLease,
        ReadOnlyMemory<byte> packet)
    {
        ArgumentNullException.ThrowIfNull(contextLease);
        if (packet.IsEmpty)
        {
            return PlaybackDecoderFfmpegPacketSendResult.EmptyPacket;
        }

        if (packet.Length > MaximumPacketBytes)
        {
            return PlaybackDecoderFfmpegPacketSendResult.PacketTooLarge;
        }

        if (!_isSupportedPlatform)
        {
            return PlaybackDecoderFfmpegPacketSendResult.Unsupported;
        }

        if (contextLease.IsDisposed)
        {
            return PlaybackDecoderFfmpegPacketSendResult.ContextUnavailable;
        }

        try
        {
            return contextLease.UseContextAndSymbols((context, symbols) =>
            {
                if (!contextLease.IsOpened)
                {
                    return PlaybackDecoderFfmpegPacketSendResult.ContextNotOpened;
                }

                if (!TryGetPacketSymbols(symbols, out var nativeSymbols))
                {
                    return PlaybackDecoderFfmpegPacketSendResult.GateRejected;
                }

                return _nativeApi.SendPacket(
                    nativeSymbols.PacketAlloc,
                    nativeSymbols.PacketFree,
                    nativeSymbols.MemoryAlloc,
                    nativeSymbols.MemoryFree,
                    nativeSymbols.PacketFromData,
                    nativeSymbols.SendPacket,
                    context,
                    packet) switch
                {
                    PlaybackDecoderFfmpegNativeIoResult.Success =>
                        PlaybackDecoderFfmpegPacketSendResult.Sent,
                    PlaybackDecoderFfmpegNativeIoResult.Again =>
                        PlaybackDecoderFfmpegPacketSendResult.NeedFrameDrain,
                    PlaybackDecoderFfmpegNativeIoResult.EndOfStream =>
                        PlaybackDecoderFfmpegPacketSendResult.EndOfStream,
                    PlaybackDecoderFfmpegNativeIoResult.AllocationFailed =>
                        PlaybackDecoderFfmpegPacketSendResult.NativeAllocationFailed,
                    _ => PlaybackDecoderFfmpegPacketSendResult.NativeRejected,
                };
            });
        }
        catch (ObjectDisposedException)
        {
            return PlaybackDecoderFfmpegPacketSendResult.ContextUnavailable;
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or NotSupportedException or OverflowException)
        {
            contextLease.Dispose();
            return PlaybackDecoderFfmpegPacketSendResult.NativeFailure;
        }
    }

    public PlaybackDecoderFfmpegFrameReceiveResult ReceiveFrame(
        PlaybackDecoderFfmpegContextLease contextLease)
    {
        ArgumentNullException.ThrowIfNull(contextLease);
        if (!_isSupportedPlatform)
        {
            return PlaybackDecoderFfmpegFrameReceiveResult.Unsupported;
        }

        if (contextLease.IsDisposed)
        {
            return PlaybackDecoderFfmpegFrameReceiveResult.ContextUnavailable;
        }

        try
        {
            return contextLease.UseContextAndSymbols((context, symbols) =>
            {
                if (!contextLease.IsOpened)
                {
                    return PlaybackDecoderFfmpegFrameReceiveResult.ContextNotOpened;
                }

                if (!symbols.TryGetValue(FrameAllocSymbol, out var frameAlloc) ||
                    !symbols.TryGetValue(FrameFreeSymbol, out var frameFree) ||
                    !symbols.TryGetValue(ReceiveFrameSymbol, out var receiveFrame))
                {
                    return PlaybackDecoderFfmpegFrameReceiveResult.GateRejected;
                }

                return _nativeApi.ReceiveFrame(
                    frameAlloc,
                    frameFree,
                    receiveFrame,
                    context) switch
                {
                    PlaybackDecoderFfmpegNativeIoResult.Success =>
                        PlaybackDecoderFfmpegFrameReceiveResult.FrameAvailable,
                    PlaybackDecoderFfmpegNativeIoResult.Again =>
                        PlaybackDecoderFfmpegFrameReceiveResult.NeedMoreInput,
                    PlaybackDecoderFfmpegNativeIoResult.EndOfStream =>
                        PlaybackDecoderFfmpegFrameReceiveResult.EndOfStream,
                    PlaybackDecoderFfmpegNativeIoResult.AllocationFailed =>
                        PlaybackDecoderFfmpegFrameReceiveResult.NativeAllocationFailed,
                    _ => PlaybackDecoderFfmpegFrameReceiveResult.NativeRejected,
                };
            });
        }
        catch (ObjectDisposedException)
        {
            return PlaybackDecoderFfmpegFrameReceiveResult.ContextUnavailable;
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or NotSupportedException)
        {
            contextLease.Dispose();
            return PlaybackDecoderFfmpegFrameReceiveResult.NativeFailure;
        }
    }

    private static bool TryGetPacketSymbols(
        IReadOnlyDictionary<string, nint> symbols,
        out PacketSymbols result)
    {
        result = default;
        if (!symbols.TryGetValue(PacketAllocSymbol, out var packetAlloc) ||
            !symbols.TryGetValue(PacketFreeSymbol, out var packetFree) ||
            !symbols.TryGetValue(MemoryAllocSymbol, out var memoryAlloc) ||
            !symbols.TryGetValue(MemoryFreeSymbol, out var memoryFree) ||
            !symbols.TryGetValue(PacketFromDataSymbol, out var packetFromData) ||
            !symbols.TryGetValue(SendPacketSymbol, out var sendPacket))
        {
            return false;
        }

        result = new(packetAlloc, packetFree, memoryAlloc, memoryFree, packetFromData, sendPacket);
        return true;
    }

    private readonly record struct PacketSymbols(
        nint PacketAlloc,
        nint PacketFree,
        nint MemoryAlloc,
        nint MemoryFree,
        nint PacketFromData,
        nint SendPacket);
}

internal sealed class WindowsPlaybackDecoderFfmpegPacketFrameNativeApi :
    IPlaybackDecoderFfmpegPacketFrameNativeApi
{
    private const int Again = -11;
    private const int EndOfFile = -541478725;
    private const int InputPadding = 64;

    public PlaybackDecoderFfmpegNativeIoResult SendPacket(
        nint packetAllocFunction,
        nint packetFreeFunction,
        nint memoryAllocFunction,
        nint memoryFreeFunction,
        nint packetFromDataFunction,
        nint sendPacketFunction,
        nint context,
        ReadOnlyMemory<byte> packet)
    {
        EnsureSupported();
        var packetAlloc = Marshal.GetDelegateForFunctionPointer<PointerFunction>(packetAllocFunction);
        var packetFree = Marshal.GetDelegateForFunctionPointer<FreePointerFunction>(packetFreeFunction);
        var memoryAlloc = Marshal.GetDelegateForFunctionPointer<AllocateFunction>(memoryAllocFunction);
        var memoryFree = Marshal.GetDelegateForFunctionPointer<FreeMemoryFunction>(memoryFreeFunction);
        var packetFromData = Marshal.GetDelegateForFunctionPointer<PacketFromDataFunction>(packetFromDataFunction);
        var sendPacket = Marshal.GetDelegateForFunctionPointer<CodecIoFunction>(sendPacketFunction);

        var nativePacket = packetAlloc();
        if (nativePacket == 0)
        {
            return PlaybackDecoderFfmpegNativeIoResult.AllocationFailed;
        }

        nint nativeData = 0;
        try
        {
            nativeData = memoryAlloc(checked((nuint)(packet.Length + InputPadding)));
            if (nativeData == 0)
            {
                return PlaybackDecoderFfmpegNativeIoResult.AllocationFailed;
            }

            var copy = packet.ToArray();
            try
            {
                Marshal.Copy(copy, 0, nativeData, copy.Length);
            }
            finally
            {
                Array.Clear(copy, 0, copy.Length);
            }

            for (var index = 0; index < InputPadding; index++)
            {
                Marshal.WriteByte(nativeData, packet.Length + index, 0);
            }

            if (packetFromData(nativePacket, nativeData, packet.Length) < 0)
            {
                return PlaybackDecoderFfmpegNativeIoResult.Rejected;
            }

            nativeData = 0;
            return Map(sendPacket(context, nativePacket));
        }
        finally
        {
            if (nativeData != 0)
            {
                memoryFree(nativeData);
            }

            packetFree(ref nativePacket);
        }
    }

    public PlaybackDecoderFfmpegNativeIoResult ReceiveFrame(
        nint frameAllocFunction,
        nint frameFreeFunction,
        nint receiveFrameFunction,
        nint context)
    {
        EnsureSupported();
        var frameAlloc = Marshal.GetDelegateForFunctionPointer<PointerFunction>(frameAllocFunction);
        var frameFree = Marshal.GetDelegateForFunctionPointer<FreePointerFunction>(frameFreeFunction);
        var receiveFrame = Marshal.GetDelegateForFunctionPointer<CodecIoFunction>(receiveFrameFunction);
        var frame = frameAlloc();
        if (frame == 0)
        {
            return PlaybackDecoderFfmpegNativeIoResult.AllocationFailed;
        }

        try
        {
            return Map(receiveFrame(context, frame));
        }
        finally
        {
            frameFree(ref frame);
        }
    }

    private static PlaybackDecoderFfmpegNativeIoResult Map(int result) => result switch
    {
        0 => PlaybackDecoderFfmpegNativeIoResult.Success,
        Again => PlaybackDecoderFfmpegNativeIoResult.Again,
        EndOfFile => PlaybackDecoderFfmpegNativeIoResult.EndOfStream,
        _ => PlaybackDecoderFfmpegNativeIoResult.Rejected,
    };

    private static void EnsureSupported()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
        {
            throw new NotSupportedException("The native packet/frame boundary is unavailable.");
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint PointerFunction();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FreePointerFunction(ref nint pointer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint AllocateFunction(nuint size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FreeMemoryFunction(nint pointer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PacketFromDataFunction(nint packet, nint data, int size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CodecIoFunction(nint context, nint value);
}
