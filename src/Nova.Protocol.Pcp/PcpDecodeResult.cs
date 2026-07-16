namespace PeerCast.Nova.Protocol.Pcp;

public readonly record struct PcpDecodeResult(
    PcpDecodeStatus Status,
    SequencePosition Consumed,
    SequencePosition Examined,
    PcpParseError Error,
    long ErrorOffset)
{
    public bool HasError => Status == PcpDecodeStatus.ProtocolError;
}
