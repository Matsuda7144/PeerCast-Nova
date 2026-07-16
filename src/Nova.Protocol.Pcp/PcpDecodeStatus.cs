namespace PeerCast.Nova.Protocol.Pcp;

public enum PcpDecodeStatus
{
    NeedMoreData,
    TopLevelAtomCompleted,
    Completed,
    ProtocolError
}
