namespace PeerCast.Nova.Network.Pcp;

public enum PcpRelayChildState
{
    Accepting,
    Stopping,
    Stopped,
}

public enum PcpRelayChildEndReason
{
    OwnerRemoved,
    FanoutStopped,
    SlowConsumer,
    SinkFailed,
    UpstreamChanged,
    PacingLimited,
}

/// <summary>
/// Fixed-field, content-free status for one local relay-child registration.
/// Pending counts include a packet currently being sent.
/// </summary>
public sealed record PcpRelayChildStatusSnapshot
{
    internal PcpRelayChildStatusSnapshot(
        PcpRelayChildState state,
        PcpRelayChildEndReason? endReason,
        int pendingPacketCount,
        long pendingByteCount,
        long deliveredPacketCount,
        long deliveredByteCount,
        long discardedPacketCount,
        long discardedByteCount)
    {
        State = state;
        EndReason = endReason;
        PendingPacketCount = pendingPacketCount;
        PendingByteCount = pendingByteCount;
        DeliveredPacketCount = deliveredPacketCount;
        DeliveredByteCount = deliveredByteCount;
        DiscardedPacketCount = discardedPacketCount;
        DiscardedByteCount = discardedByteCount;
    }

    public PcpRelayChildState State { get; }

    public PcpRelayChildEndReason? EndReason { get; }

    public int PendingPacketCount { get; }

    public int InFlightPacketCount { get; internal init; }

    public bool CleanupFailed { get; internal init; }

    public bool StartupPending { get; internal init; }

    public bool StartupFailed { get; internal init; }

    public int QueuedPacketCount => PendingPacketCount - InFlightPacketCount;

    public long PendingByteCount { get; }

    public long DeliveredPacketCount { get; }

    public long DeliveredByteCount { get; }

    public long DiscardedPacketCount { get; }

    public long DiscardedByteCount { get; }
}
