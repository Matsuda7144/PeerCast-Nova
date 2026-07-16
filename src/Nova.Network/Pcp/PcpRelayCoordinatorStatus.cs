namespace PeerCast.Nova.Network.Pcp;

public enum PcpRelayCoordinatorState
{
    Created,
    Running,
    Stopping,
    Stopped,
}

public enum PcpRelayCoordinatorReadinessOutcome
{
    Ready,
    Stopped,
    Failed,
}

public enum PcpRelayCoordinatorEndReason
{
    Stopped,
    CallerCanceled,
    UpstreamEnded,
    UpstreamFailed,
    DownstreamFailed,
    InternalFailure,
}

public enum PcpRelayDrainOutcome
{
    NotRequired,
    Completed,
    TimedOut,
    Canceled,
}

/// <summary>
/// Content-free aggregate of packets accepted by the coordinator's bounded
/// fan-out. It remains available even when the upstream session faults before
/// it can return its terminal result.
/// </summary>
public sealed record PcpRelayIngressSnapshot(
    long AcceptedPacketCount,
    long AcceptedPayloadByteCount);

/// <summary>
/// Privacy-safe terminal relay summary. Its nested results also contain only
/// fixed enums, booleans, status codes, and numeric counters.
/// </summary>
public sealed record PcpRelayCoordinatorResult(
    PcpRelayCoordinatorEndReason EndReason,
    PcpRelayDrainOutcome DrainOutcome,
    PcpRelayIngressSnapshot Ingress,
    PcpChannelSessionResult? Upstream,
    PcpDownstreamRelayServerResult? Downstream,
    bool ShutdownCompleted);
