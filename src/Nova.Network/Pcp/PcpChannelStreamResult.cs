namespace PeerCast.Nova.Network.Pcp;

public enum PcpChannelStreamEndReason
{
    ConnectionEnded,
    QuitReceived,
}

public sealed record PcpChannelStreamResult(
    PcpChannelStreamEndReason EndReason,
    long ChannelMessageCount,
    long PacketCount,
    long PayloadByteCount);
