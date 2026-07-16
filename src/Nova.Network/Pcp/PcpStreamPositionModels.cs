namespace PeerCast.Nova.Network.Pcp;

public enum PcpStreamPositionObservationKind
{
    Initial,
    Advanced,
    Wrapped,
    Duplicate,
    BackwardDiscontinuity,
}

public readonly record struct PcpStreamPositionObservation(
    PcpStreamPositionObservationKind Kind,
    uint WirePosition,
    uint? PreviousWirePosition,
    long AbsolutePosition);

/// <summary>
/// Reports an ambiguous or backward wire position without moving Core state.
/// </summary>
public sealed record PcpStreamDiscontinuity(
    uint PreviousWirePosition,
    uint ObservedWirePosition,
    long AbsolutePositionBeforeObservation);
