using System.Numerics;
using System.Runtime.InteropServices;

namespace PeerCast.Nova.Playback;

public enum PlaybackMetadataAwareNativePacketResult
{
    Submitted,
    NeedFrameDrain,
    EndOfStream,
    InvalidConfiguration,
    SourceMismatch,
    CompletionUnavailable,
    EventRejected,
    PacketTooLarge,
    TimestampRejected,
    Unsupported,
    GateRejected,
    NativeAllocationFailed,
    NativeRejected,
    NativeFailure,
}

internal enum PlaybackMetadataAwareNativeSendResult
{
    Success,
    Again,
    EndOfStream,
    GateRejected,
    AllocationFailed,
    Rejected,
}

internal readonly record struct PlaybackMetadataAwareNativePacketInput(
    ReadOnlyMemory<byte> Payload,
    long PresentationTimestamp,
    long DecodeTimestamp,
    long Duration,
    int StreamIndex,
    int Flags,
    PlaybackBoundedDemuxTimeBase TimeBase);

internal interface IPlaybackMetadataAwareNativePacketOwner : IDisposable
{
    PlaybackMetadataAwareNativeSendResult SendOnce(
        PlaybackBoundedFlvNativeDecoderSession session,
        PlaybackBoundedDemuxMediaKind mediaKind);
}

internal readonly record struct PlaybackMetadataAwareNativePacketCreation(
    PlaybackMetadataAwareNativeSendResult Result,
    IPlaybackMetadataAwareNativePacketOwner? Owner);

internal interface IPlaybackMetadataAwareNativePacketOperations
{
    PlaybackMetadataAwareNativePacketCreation CreatePacket(
        PlaybackBoundedFlvNativeDecoderSession session,
        PlaybackBoundedDemuxMediaKind mediaKind,
        PlaybackMetadataAwareNativePacketInput input);

    PlaybackMetadataAwareNativeSendResult SendFlush(
        PlaybackBoundedFlvNativeDecoderSession session,
        PlaybackBoundedDemuxMediaKind mediaKind);
}

public sealed class PlaybackMetadataAwareNativePacketCompletion : IDisposable, IAsyncDisposable
{
    private IPlaybackMetadataAwareNativePacketOwner? _packet;
    private PlaybackBoundedDemuxReadCompletion? _sourceCompletion;

    internal PlaybackMetadataAwareNativePacketCompletion(
        PlaybackMetadataAwareNativePacketResult result,
        IPlaybackMetadataAwareNativePacketOwner? packet,
        PlaybackBoundedDemuxReadCompletion? sourceCompletion)
    {
        Result = result;
        _packet = packet;
        _sourceCompletion = sourceCompletion;
    }

    public PlaybackMetadataAwareNativePacketResult Result { get; }

    public bool RetainsPendingOwnership =>
        Volatile.Read(ref _packet) is not null ||
        Volatile.Read(ref _sourceCompletion) is not null;

    public bool IsDisposed => !RetainsPendingOwnership;

    public void Dispose()
    {
        var packet = Interlocked.Exchange(ref _packet, null);
        var sourceCompletion = Interlocked.Exchange(ref _sourceCompletion, null);
        try
        {
            packet?.Dispose();
        }
        finally
        {
            sourceCompletion?.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class PlaybackMetadataAwareNativePacketBoundary
{
    public const int MaximumPacketBytes = PlaybackDecoderFfmpegPacketFrameBoundary.MaximumPacketBytes;

    private const long AvNoPtsValue = long.MinValue;
    private const int AvPacketFlagKey = 0x0001;

    private readonly IPlaybackMetadataAwareNativePacketOperations _operations;
    private readonly bool _isSupportedPlatform;

    public PlaybackMetadataAwareNativePacketBoundary()
        : this(
            new PlaybackMetadataAwareNativePacketOperations(),
            OperatingSystem.IsWindows() && Environment.Is64BitProcess)
    {
    }

    internal PlaybackMetadataAwareNativePacketBoundary(
        IPlaybackMetadataAwareNativePacketOperations operations,
        bool isSupportedPlatform = true)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations;
        _isSupportedPlatform = isSupportedPlatform;
    }

    public PlaybackMetadataAwareNativePacketCompletion SubmitOne(
        PlaybackBoundedFlvNativeDecoderSession? nativeSession,
        PlaybackBoundedDemuxSession? sourceSession,
        PlaybackBoundedDemuxReadCompletion? sourceCompletion)
    {
        if (nativeSession is null || sourceSession is null || sourceCompletion is null)
        {
            sourceCompletion?.Dispose();
            return Complete(PlaybackMetadataAwareNativePacketResult.InvalidConfiguration);
        }

        if (!_isSupportedPlatform)
        {
            sourceCompletion.Dispose();
            return Complete(PlaybackMetadataAwareNativePacketResult.Unsupported);
        }

        if (nativeSession.IsDisposed || sourceSession.IsDisposed)
        {
            sourceCompletion.Dispose();
            return Complete(PlaybackMetadataAwareNativePacketResult.InvalidConfiguration);
        }

        if (!nativeSession.ReferencesSourceSession(sourceSession))
        {
            sourceCompletion.Dispose();
            return Complete(PlaybackMetadataAwareNativePacketResult.SourceMismatch);
        }

        if (sourceCompletion.IsDisposed)
        {
            return Complete(PlaybackMetadataAwareNativePacketResult.CompletionUnavailable);
        }

        if (sourceCompletion.Events.Count != 1)
        {
            sourceCompletion.Dispose();
            return Complete(PlaybackMetadataAwareNativePacketResult.EventRejected);
        }

        var demuxEvent = sourceCompletion.Events[0];
        if (!IsDefinedEvent(sourceCompletion, demuxEvent))
        {
            sourceCompletion.Dispose();
            return Complete(PlaybackMetadataAwareNativePacketResult.EventRejected);
        }

        var mapping = TryMapEvent(nativeSession, sourceSession, demuxEvent);
        if (mapping.Result != PlaybackMetadataAwareNativePacketResult.Submitted)
        {
            sourceCompletion.Dispose();
            return Complete(mapping.Result);
        }

        IPlaybackMetadataAwareNativePacketOwner? packet = null;
        try
        {
            if (demuxEvent.Kind == PlaybackBoundedDemuxEventKind.EndOfStream)
            {
                var flushResult = _operations.SendFlush(nativeSession, demuxEvent.MediaKind);
                if (flushResult == PlaybackMetadataAwareNativeSendResult.Again)
                {
                    return new(
                        PlaybackMetadataAwareNativePacketResult.NeedFrameDrain,
                        null,
                        sourceCompletion);
                }

                sourceCompletion.Dispose();
                return Complete(MapSendResult(flushResult));
            }

            if (demuxEvent.Kind != PlaybackBoundedDemuxEventKind.Packet ||
                demuxEvent.Payload.IsEmpty)
            {
                sourceCompletion.Dispose();
                return Complete(PlaybackMetadataAwareNativePacketResult.EventRejected);
            }

            if (demuxEvent.Payload.Length > MaximumPacketBytes)
            {
                sourceCompletion.Dispose();
                return Complete(PlaybackMetadataAwareNativePacketResult.PacketTooLarge);
            }

            if (!TryMapTime(demuxEvent.PresentationTimestamp, mapping.TimeBase, out var pts) ||
                !TryMapTime(demuxEvent.DecodeTimestamp, mapping.TimeBase, out var dts) ||
                !TryMapDuration(demuxEvent.Duration, mapping.TimeBase, out var duration))
            {
                sourceCompletion.Dispose();
                return Complete(PlaybackMetadataAwareNativePacketResult.TimestampRejected);
            }

            var flags = demuxEvent.KeyframeState == PlaybackBoundedDemuxKeyframeState.Keyframe
                ? AvPacketFlagKey
                : 0;
            var input = new PlaybackMetadataAwareNativePacketInput(
                demuxEvent.Payload,
                pts,
                dts,
                duration,
                demuxEvent.StreamId,
                flags,
                mapping.TimeBase);
            var creation = _operations.CreatePacket(nativeSession, demuxEvent.MediaKind, input);
            packet = creation.Owner;
            if (creation.Result != PlaybackMetadataAwareNativeSendResult.Success || packet is null)
            {
                packet?.Dispose();
                sourceCompletion.Dispose();
                return Complete(MapCreationResult(creation.Result));
            }

            var sendResult = packet.SendOnce(nativeSession, demuxEvent.MediaKind);
            if (sendResult == PlaybackMetadataAwareNativeSendResult.Again)
            {
                var retained = packet;
                packet = null;
                return new(
                    PlaybackMetadataAwareNativePacketResult.NeedFrameDrain,
                    retained,
                    sourceCompletion);
            }

            DisposePacketThenCompletion(packet, sourceCompletion);
            packet = null;
            return Complete(MapSendResult(sendResult));
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or NotSupportedException or OverflowException or
            ObjectDisposedException)
        {
            DisposePacketThenCompletion(packet, sourceCompletion);
            return Complete(PlaybackMetadataAwareNativePacketResult.NativeFailure);
        }
    }

    private static bool IsDefinedEvent(
        PlaybackBoundedDemuxReadCompletion sourceCompletion,
        PlaybackBoundedDemuxEvent demuxEvent)
    {
        if (!Enum.IsDefined(sourceCompletion.Result) ||
            !Enum.IsDefined(demuxEvent.Kind) ||
            !Enum.IsDefined(demuxEvent.MediaKind) ||
            !Enum.IsDefined(demuxEvent.KeyframeState) ||
            !Enum.IsDefined(demuxEvent.Discontinuity))
        {
            return false;
        }

        return demuxEvent.Kind switch
        {
            PlaybackBoundedDemuxEventKind.Packet =>
                sourceCompletion.Result == PlaybackBoundedDemuxResult.PacketsAvailable,
            PlaybackBoundedDemuxEventKind.EndOfStream =>
                sourceCompletion.Result is PlaybackBoundedDemuxResult.PacketsAvailable or
                    PlaybackBoundedDemuxResult.EndOfStream,
            _ => false,
        };
    }

    private static EventMapping TryMapEvent(
        PlaybackBoundedFlvNativeDecoderSession nativeSession,
        PlaybackBoundedDemuxSession sourceSession,
        PlaybackBoundedDemuxEvent demuxEvent)
    {
        if (demuxEvent.Kind == PlaybackBoundedDemuxEventKind.Packet &&
            demuxEvent.Discontinuity != PlaybackDecodedFrameDiscontinuityKind.None)
        {
            return new(PlaybackMetadataAwareNativePacketResult.EventRejected);
        }

        return demuxEvent.MediaKind switch
        {
            PlaybackBoundedDemuxMediaKind.Video
                when demuxEvent.StreamId == nativeSession.VideoStreamId &&
                     sourceSession.Video.StreamId == demuxEvent.StreamId &&
                     sourceSession.Video.MediaKind == PlaybackBoundedDemuxMediaKind.Video &&
                     sourceSession.Video.Codec == PlaybackBoundedDemuxCodec.H264 &&
                     sourceSession.Video.TimeBase == nativeSession.VideoTimeBase &&
                     IsValidTimeBase(nativeSession.VideoTimeBase) &&
                     IsValidKeyframeState(demuxEvent, isVideo: true) =>
                new(
                    PlaybackMetadataAwareNativePacketResult.Submitted,
                    nativeSession.VideoTimeBase),
            PlaybackBoundedDemuxMediaKind.Audio
                when demuxEvent.StreamId == nativeSession.AudioStreamId &&
                     sourceSession.Audio.StreamId == demuxEvent.StreamId &&
                     sourceSession.Audio.MediaKind == PlaybackBoundedDemuxMediaKind.Audio &&
                     sourceSession.Audio.Codec == PlaybackBoundedDemuxCodec.Aac &&
                     sourceSession.Audio.TimeBase == nativeSession.AudioTimeBase &&
                     IsValidTimeBase(nativeSession.AudioTimeBase) &&
                     IsValidKeyframeState(demuxEvent, isVideo: false) =>
                new(
                    PlaybackMetadataAwareNativePacketResult.Submitted,
                    nativeSession.AudioTimeBase),
            _ => new(PlaybackMetadataAwareNativePacketResult.EventRejected),
        };
    }

    private static bool IsValidKeyframeState(
        PlaybackBoundedDemuxEvent demuxEvent,
        bool isVideo)
    {
        if (demuxEvent.Kind == PlaybackBoundedDemuxEventKind.EndOfStream)
        {
            return demuxEvent.KeyframeState == PlaybackBoundedDemuxKeyframeState.NotApplicable;
        }

        return isVideo
            ? demuxEvent.KeyframeState is PlaybackBoundedDemuxKeyframeState.Unknown or
                PlaybackBoundedDemuxKeyframeState.NotKeyframe or
                PlaybackBoundedDemuxKeyframeState.Keyframe
            : demuxEvent.KeyframeState == PlaybackBoundedDemuxKeyframeState.NotApplicable;
    }

    private static bool TryMapTime(
        PlaybackBoundedDemuxTime value,
        PlaybackBoundedDemuxTimeBase timeBase,
        out long result)
    {
        if (!value.IsKnown)
        {
            result = AvNoPtsValue;
            return true;
        }

        return TryConvertTicks(value.Value.Ticks, timeBase, out result);
    }

    private static bool TryMapDuration(
        PlaybackBoundedDemuxTime value,
        PlaybackBoundedDemuxTimeBase timeBase,
        out long result)
    {
        result = 0;
        if (!value.IsKnown)
        {
            return true;
        }

        return value.Value >= TimeSpan.Zero &&
            TryConvertTicks(value.Value.Ticks, timeBase, out result);
    }

    private static bool TryConvertTicks(
        long ticks,
        PlaybackBoundedDemuxTimeBase timeBase,
        out long result)
    {
        result = 0;
        if (!IsValidTimeBase(timeBase))
        {
            return false;
        }

        var numerator = (BigInteger)ticks * timeBase.Denominator;
        var denominator = (BigInteger)timeBase.Numerator * TimeSpan.TicksPerSecond;
        var quotient = BigInteger.DivRem(numerator, denominator, out var remainder);
        if (remainder != BigInteger.Zero || quotient < long.MinValue || quotient > long.MaxValue)
        {
            return false;
        }

        result = (long)quotient;
        return true;
    }

    private static bool IsValidTimeBase(PlaybackBoundedDemuxTimeBase value) =>
        value.Numerator > 0 && value.Denominator > 0 &&
        value.Numerator <= PlaybackBoundedDemuxOptions.MaximumTimeBaseComponent &&
        value.Denominator <= PlaybackBoundedDemuxOptions.MaximumTimeBaseComponent;

    private static PlaybackMetadataAwareNativePacketResult MapCreationResult(
        PlaybackMetadataAwareNativeSendResult result) => result switch
        {
            PlaybackMetadataAwareNativeSendResult.GateRejected =>
                PlaybackMetadataAwareNativePacketResult.GateRejected,
            PlaybackMetadataAwareNativeSendResult.AllocationFailed =>
                PlaybackMetadataAwareNativePacketResult.NativeAllocationFailed,
            _ => PlaybackMetadataAwareNativePacketResult.NativeRejected,
        };

    private static PlaybackMetadataAwareNativePacketResult MapSendResult(
        PlaybackMetadataAwareNativeSendResult result) => result switch
        {
            PlaybackMetadataAwareNativeSendResult.Success =>
                PlaybackMetadataAwareNativePacketResult.Submitted,
            PlaybackMetadataAwareNativeSendResult.Again =>
                PlaybackMetadataAwareNativePacketResult.NeedFrameDrain,
            PlaybackMetadataAwareNativeSendResult.EndOfStream =>
                PlaybackMetadataAwareNativePacketResult.EndOfStream,
            PlaybackMetadataAwareNativeSendResult.GateRejected =>
                PlaybackMetadataAwareNativePacketResult.GateRejected,
            PlaybackMetadataAwareNativeSendResult.AllocationFailed =>
                PlaybackMetadataAwareNativePacketResult.NativeAllocationFailed,
            _ => PlaybackMetadataAwareNativePacketResult.NativeRejected,
        };

    private static void DisposePacketThenCompletion(
        IPlaybackMetadataAwareNativePacketOwner? packet,
        PlaybackBoundedDemuxReadCompletion sourceCompletion)
    {
        try
        {
            packet?.Dispose();
        }
        finally
        {
            sourceCompletion.Dispose();
        }
    }

    private static PlaybackMetadataAwareNativePacketCompletion Complete(
        PlaybackMetadataAwareNativePacketResult result) => new(result, null, null);

    private readonly record struct EventMapping(
        PlaybackMetadataAwareNativePacketResult Result,
        PlaybackBoundedDemuxTimeBase TimeBase = default);
}

internal sealed class PlaybackMetadataAwareNativePacketOperations :
    IPlaybackMetadataAwareNativePacketOperations
{
    private const string PacketAllocSymbol = "av_packet_alloc";
    private const string PacketFreeSymbol = "av_packet_free";
    private const string MemoryAllocSymbol = "av_malloc";
    private const string MemoryFreeSymbol = "av_free";
    private const string PacketFromDataSymbol = "av_packet_from_data";
    private const string SendPacketSymbol = "avcodec_send_packet";

    public PlaybackMetadataAwareNativePacketCreation CreatePacket(
        PlaybackBoundedFlvNativeDecoderSession session,
        PlaybackBoundedDemuxMediaKind mediaKind,
        PlaybackMetadataAwareNativePacketInput input) =>
        CreatePacket(GetContext(session, mediaKind), input);

    internal static PlaybackMetadataAwareNativePacketCreation CreatePacket(
        PlaybackDecoderFfmpegContextLease context,
        PlaybackMetadataAwareNativePacketInput input)
    {
        try
        {
            return context.UseContextAndSymbols((_, symbols) =>
            {
                if (!context.IsOpened ||
                    !symbols.TryGetValue(PacketAllocSymbol, out var packetAlloc) ||
                    !symbols.TryGetValue(PacketFreeSymbol, out var packetFree) ||
                    !symbols.TryGetValue(MemoryAllocSymbol, out var memoryAlloc) ||
                    !symbols.TryGetValue(MemoryFreeSymbol, out var memoryFree) ||
                    !symbols.TryGetValue(PacketFromDataSymbol, out var packetFromData) ||
                    !symbols.TryGetValue(SendPacketSymbol, out var sendPacket))
                {
                    return new PlaybackMetadataAwareNativePacketCreation(
                        PlaybackMetadataAwareNativeSendResult.GateRejected,
                        null);
                }

                return WindowsPlaybackMetadataAwareNativePacketOwner.Create(
                    packetAlloc,
                    packetFree,
                    memoryAlloc,
                    memoryFree,
                    packetFromData,
                    sendPacket,
                    input);
            });
        }
        catch (ObjectDisposedException)
        {
            return new(PlaybackMetadataAwareNativeSendResult.Rejected, null);
        }
    }

    public PlaybackMetadataAwareNativeSendResult SendFlush(
        PlaybackBoundedFlvNativeDecoderSession session,
        PlaybackBoundedDemuxMediaKind mediaKind) =>
        SendFlush(GetContext(session, mediaKind));

    internal static PlaybackMetadataAwareNativeSendResult SendFlush(
        PlaybackDecoderFfmpegContextLease context)
    {
        try
        {
            return context.UseContextAndSymbols((nativeContext, symbols) =>
            {
                if (!context.IsOpened ||
                    !symbols.TryGetValue(SendPacketSymbol, out var sendPacket))
                {
                    return PlaybackMetadataAwareNativeSendResult.GateRejected;
                }

                return WindowsPlaybackMetadataAwareNativePacketOwner.Send(
                    sendPacket,
                    nativeContext,
                    0);
            });
        }
        catch (ObjectDisposedException)
        {
            return PlaybackMetadataAwareNativeSendResult.Rejected;
        }
    }

    internal static PlaybackDecoderFfmpegContextLease GetContext(
        PlaybackBoundedFlvNativeDecoderSession session,
        PlaybackBoundedDemuxMediaKind mediaKind) => mediaKind switch
        {
            PlaybackBoundedDemuxMediaKind.Video => session.VideoContext,
            PlaybackBoundedDemuxMediaKind.Audio => session.AudioContext,
            _ => throw new InvalidOperationException("The packet media kind is undefined."),
        };
}

internal sealed class WindowsPlaybackMetadataAwareNativePacketOwner :
    IPlaybackMetadataAwareNativePacketOwner
{
    internal const int InputPadding = 64;

    private const int BufferOffset = 0;
    private const int PresentationTimestampOffset = 8;
    private const int DecodeTimestampOffset = 16;
    private const int DataOffset = 24;
    private const int StreamIndexOffset = 36;
    private const int FlagsOffset = 40;
    private const int DurationOffset = 64;
    private const int TimeBaseNumeratorOffset = 96;
    private const int TimeBaseDenominatorOffset = 100;
    private const int Again = -11;
    private const int EndOfFile = -541478725;

    private readonly object _sync = new();
    private nint _packet;
    private readonly FreePointerFunction _free;
    private readonly CodecIoFunction _send;

    private WindowsPlaybackMetadataAwareNativePacketOwner(
        nint packet,
        FreePointerFunction free,
        CodecIoFunction send)
    {
        _packet = packet;
        _free = free;
        _send = send;
    }

    public static PlaybackMetadataAwareNativePacketCreation Create(
        nint packetAllocFunction,
        nint packetFreeFunction,
        nint memoryAllocFunction,
        nint memoryFreeFunction,
        nint packetFromDataFunction,
        nint sendPacketFunction,
        PlaybackMetadataAwareNativePacketInput input)
    {
        EnsureSupported();
        var allocatePacket = Marshal.GetDelegateForFunctionPointer<PointerFunction>(packetAllocFunction);
        var freePacket = Marshal.GetDelegateForFunctionPointer<FreePointerFunction>(packetFreeFunction);
        var allocateMemory = Marshal.GetDelegateForFunctionPointer<AllocateFunction>(memoryAllocFunction);
        var freeMemory = Marshal.GetDelegateForFunctionPointer<FreeMemoryFunction>(memoryFreeFunction);
        var packetFromData = Marshal.GetDelegateForFunctionPointer<PacketFromDataFunction>(packetFromDataFunction);
        var send = Marshal.GetDelegateForFunctionPointer<CodecIoFunction>(sendPacketFunction);
        var packet = allocatePacket();
        if (packet == 0)
        {
            return new(PlaybackMetadataAwareNativeSendResult.AllocationFailed, null);
        }

        nint data = 0;
        try
        {
            data = allocateMemory(checked((nuint)(input.Payload.Length + InputPadding)));
            if (data == 0)
            {
                freePacket(ref packet);
                return new(PlaybackMetadataAwareNativeSendResult.AllocationFailed, null);
            }

            WritePayloadAndPadding(data, input.Payload.Span);
            if (packetFromData(packet, data, input.Payload.Length) < 0)
            {
                freeMemory(data);
                data = 0;
                freePacket(ref packet);
                return new(PlaybackMetadataAwareNativeSendResult.Rejected, null);
            }

            data = 0;
            if (Marshal.ReadIntPtr(packet, BufferOffset) == 0 ||
                Marshal.ReadIntPtr(packet, DataOffset) == 0)
            {
                freePacket(ref packet);
                return new(PlaybackMetadataAwareNativeSendResult.Rejected, null);
            }

            WriteReviewedMetadata(packet, input);
            return new(
                PlaybackMetadataAwareNativeSendResult.Success,
                new WindowsPlaybackMetadataAwareNativePacketOwner(packet, freePacket, send));
        }
        catch
        {
            if (data != 0)
            {
                freeMemory(data);
            }

            if (packet != 0)
            {
                freePacket(ref packet);
            }

            throw;
        }
    }

    public PlaybackMetadataAwareNativeSendResult SendOnce(
        PlaybackBoundedFlvNativeDecoderSession session,
        PlaybackBoundedDemuxMediaKind mediaKind) =>
        SendOnce(PlaybackMetadataAwareNativePacketOperations.GetContext(
            session,
            mediaKind));

    internal PlaybackMetadataAwareNativeSendResult SendOnce(
        PlaybackDecoderFfmpegContextLease context)
    {
        lock (_sync)
        {
            if (_packet == 0)
            {
                return PlaybackMetadataAwareNativeSendResult.Rejected;
            }

            return context.UseContextAndSymbols(
                (nativeContext, _) => Send(_send, nativeContext, _packet));
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_packet != 0)
            {
                _free(ref _packet);
            }
        }
    }

    internal static void WritePayloadAndPadding(nint destination, ReadOnlySpan<byte> payload)
    {
        if (destination == 0)
        {
            throw new ArgumentException("The native payload destination is null.", nameof(destination));
        }

        for (var index = 0; index < payload.Length; index++)
        {
            Marshal.WriteByte(destination, index, payload[index]);
        }

        for (var index = 0; index < InputPadding; index++)
        {
            Marshal.WriteByte(destination, payload.Length + index, 0);
        }
    }

    internal static void WriteReviewedMetadata(
        nint packet,
        PlaybackMetadataAwareNativePacketInput input)
    {
        if (packet == 0)
        {
            throw new ArgumentException("The native packet pointer is null.", nameof(packet));
        }

        Marshal.WriteInt64(packet, PresentationTimestampOffset, input.PresentationTimestamp);
        Marshal.WriteInt64(packet, DecodeTimestampOffset, input.DecodeTimestamp);
        Marshal.WriteInt32(packet, StreamIndexOffset, input.StreamIndex);
        Marshal.WriteInt32(packet, FlagsOffset, input.Flags);
        Marshal.WriteInt64(packet, DurationOffset, input.Duration);
        Marshal.WriteInt32(packet, TimeBaseNumeratorOffset, input.TimeBase.Numerator);
        Marshal.WriteInt32(packet, TimeBaseDenominatorOffset, input.TimeBase.Denominator);
    }

    internal static PlaybackMetadataAwareNativeSendResult Send(
        nint sendPacketFunction,
        nint context,
        nint packet)
    {
        EnsureSupported();
        var send = Marshal.GetDelegateForFunctionPointer<CodecIoFunction>(sendPacketFunction);
        return Send(send, context, packet);
    }

    private static PlaybackMetadataAwareNativeSendResult Send(
        CodecIoFunction send,
        nint context,
        nint packet) => send(context, packet) switch
        {
            0 => PlaybackMetadataAwareNativeSendResult.Success,
            Again => PlaybackMetadataAwareNativeSendResult.Again,
            EndOfFile => PlaybackMetadataAwareNativeSendResult.EndOfStream,
            _ => PlaybackMetadataAwareNativeSendResult.Rejected,
        };

    private static void EnsureSupported()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
        {
            throw new NotSupportedException("The metadata-aware packet boundary is unavailable.");
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
    private delegate int CodecIoFunction(nint context, nint packet);
}
