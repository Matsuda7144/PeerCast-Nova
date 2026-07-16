using System.Collections.Immutable;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

public enum PcpCompatibilityOutcome
{
    Accepted,
    RelayFull,
    HttpRejected,
    ProtocolRejected
}

public sealed record PcpCompatibilityProbeResult(
    PcpCompatibilityOutcome Outcome,
    int HttpStatusCode,
    string? Server,
    string? ContentType,
    PcpOlehInfo? RemotePeer,
    PcpAtomName? TerminalAtom,
    uint? TerminalCode,
    ImmutableArray<PcpAtomName> ReceivedAtomNames)
{
    public PcpPostHandshakeObservation? PostHandshakeObservation { get; init; }
}
