using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Result of applying one payload-free CHAN control message.
/// </summary>
public sealed record PcpChannelApplyResult(
    ChannelStatusSnapshot Snapshot,
    PcpChannelPacketHeader? Packet,
    PcpStreamDiscontinuity? Discontinuity);
