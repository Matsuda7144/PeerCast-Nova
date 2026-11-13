using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

public enum PcpDownstreamChildEndReason
{
    Stopped,
    RelayFull,
    ProtocolRejected,
    HandshakeTimedOut,
    QuitReceived,
    ConnectionEnded,
    FanoutStopped,
    SlowConsumer,
    WriteTimedOut,
    WriteFailed,
    AdmissionClosed,
    PingCompleted,
}

/// <summary>
/// Fixed-field, content-free summary for one downstream child. It contains no
/// address, channel/session identifier, agent, metadata, payload, or exception.
/// </summary>
public sealed record PcpDownstreamChildResult(
    PcpDownstreamChildEndReason EndReason,
    int? HttpStatusCode,
    bool HandshakeAccepted,
    bool CleanupCompleted,
    long DeliveredPacketCount,
    long DeliveredByteCount,
    long DiscardedPacketCount,
    long DiscardedByteCount)
{
    public PcpQuitInfo? Quit { get; init; }
    /// <summary>Point-in-time completed writes; late cleanup may still update owner/global totals.</summary>
    public PcpDownstreamTrafficSnapshot? Traffic { get; init; }
    public bool TrafficFinal { get; init; }
}
