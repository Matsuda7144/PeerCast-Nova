using System.Collections.Immutable;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

internal sealed record PcpRelayHandshakeResult(
    PcpCompatibilityOutcome Outcome,
    int HttpStatusCode,
    string? Server,
    string? ContentType,
    PcpOlehInfo? RemotePeer,
    PcpAtomName? TerminalAtom,
    uint? TerminalCode,
    ImmutableArray<PcpAtomName> ReceivedAtomNames)
{
    public PcpCompatibilityProbeResult ToCompatibilityResult()
    {
        return new PcpCompatibilityProbeResult(
            Outcome,
            HttpStatusCode,
            Server,
            ContentType,
            RemotePeer,
            TerminalAtom,
            TerminalCode,
            ReceivedAtomNames);
    }
}
