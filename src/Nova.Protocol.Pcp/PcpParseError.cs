namespace PeerCast.Nova.Protocol.Pcp;

public enum PcpParseError
{
    None,
    NeedMoreData,
    ContentLengthLimitExceeded,
    ChildCountLimitExceeded,
    NestingDepthLimitExceeded,
    UnexpectedEndOfStream,
    InvalidPreamble,
    UnsupportedProtocolVersion,
    InvalidParentState
}
