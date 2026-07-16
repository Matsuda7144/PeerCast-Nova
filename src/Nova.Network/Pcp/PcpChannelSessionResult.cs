namespace PeerCast.Nova.Network.Pcp;

public enum PcpChannelSessionEndReason
{
    Stopped,
    RelayFull,
    HttpRejected,
    ProtocolRejected,
    QuitReceived,
    ConnectionEnded,
}

/// <summary>
/// Privacy-safe terminal summary. It deliberately contains no peer address,
/// channel identifier, remote session identifier, metadata, or exception.
/// </summary>
public sealed record PcpChannelSessionResult(
    PcpChannelSessionEndReason EndReason,
    int AttemptCount,
    int ReconnectCount,
    int? HttpStatusCode,
    long ChannelMessageCount,
    long PacketCount,
    long PayloadByteCount);
