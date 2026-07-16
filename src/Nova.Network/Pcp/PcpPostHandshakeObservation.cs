using System.Collections.Immutable;

namespace PeerCast.Nova.Network.Pcp;

public enum PcpPostHandshakeStopReason
{
    TopLevelAtomLimitReached,
    TimedOut,
    AtomSizeLimitExceeded,
    ConnectionEnded,
    QuitReceived,
}

public sealed record PcpPostHandshakeObservation(
    ImmutableArray<PcpAtomShape> Atoms,
    PcpPostHandshakeStopReason StopReason,
    long ObservedWireBytes);
