using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Callback-scoped data-plane view of one PCP channel packet. Payload memory
/// must not be retained after the consumer callback completes.
/// </summary>
public sealed class PcpChannelPacket
{
    internal PcpChannelPacket(
        ChannelId channelId,
        ChannelSessionId sessionId,
        long sequence,
        uint type,
        uint wirePosition,
        long? streamPosition,
        ReadOnlyMemory<byte> payload,
        PcpStreamDiscontinuity? discontinuity,
        byte? continuation = null)
    {
        ChannelId = channelId;
        SessionId = sessionId;
        Sequence = sequence;
        Type = type;
        WirePosition = wirePosition;
        StreamPosition = streamPosition;
        Payload = payload;
        Discontinuity = discontinuity;
        Continuation = continuation;
    }

    public ChannelId ChannelId { get; }

    public ChannelSessionId SessionId { get; }

    public long Sequence { get; }

    public uint Type { get; }

    public uint WirePosition { get; }

    /// <summary>
    /// Monotonic position when this packet was accepted. It is null when the
    /// wire position produced an explicit discontinuity.
    /// </summary>
    public long? StreamPosition { get; }

    public ReadOnlyMemory<byte> Payload { get; }

    public PcpStreamDiscontinuity? Discontinuity { get; }

    public byte? Continuation { get; }

    public bool HasContinuation => Continuation is not null;
}
