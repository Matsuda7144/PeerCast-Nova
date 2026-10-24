namespace PeerCast.Nova.Network.Pcp;

/// <summary>Fixed-size, content-free aggregates across current and retired children.</summary>
public sealed record PcpRelayFanoutEvidence
{
    public bool AdmissionClosed { get; init; }
    public long RegisteredChildCount { get; init; }
    public long EndedChildCount { get; init; }
    public int RemainingChildCount { get; init; }
    public long QueuedPacketCount { get; init; }
    public long InFlightPacketCount { get; init; }
    public long PendingByteCount { get; init; }
    public long DeliveredPacketCount { get; init; }
    public long DeliveredByteCount { get; init; }
    public long DiscardedPacketCount { get; init; }
    public long DiscardedByteCount { get; init; }
    public long SinkFailedChildCount { get; init; }
    public long SlowConsumerChildCount { get; init; }
    public long OwnerRemovedChildCount { get; init; }
    public long FanoutStoppedChildCount { get; init; }
    public long CleanupFailedChildCount { get; init; }
    public long StartupPendingChildCount { get; init; }
    public long StartupFailedChildCount { get; init; }

    public bool QueuesEmpty => QueuedPacketCount == 0;
    public bool SendsSettled => QueuesEmpty && InFlightPacketCount == 0 && StartupPendingChildCount == 0;
    public bool DeliveryFailed => DiscardedPacketCount != 0 || SinkFailedChildCount != 0 || SlowConsumerChildCount != 0 || StartupFailedChildCount != 0;

    internal PcpRelayFanoutEvidence Add(PcpRelayChildStatusSnapshot child) => this with
    {
        QueuedPacketCount = checked(QueuedPacketCount + child.QueuedPacketCount),
        InFlightPacketCount = checked(InFlightPacketCount + child.InFlightPacketCount),
        PendingByteCount = checked(PendingByteCount + child.PendingByteCount),
        DeliveredPacketCount = checked(DeliveredPacketCount + child.DeliveredPacketCount),
        DeliveredByteCount = checked(DeliveredByteCount + child.DeliveredByteCount),
        DiscardedPacketCount = checked(DiscardedPacketCount + child.DiscardedPacketCount),
        DiscardedByteCount = checked(DiscardedByteCount + child.DiscardedByteCount),
        SinkFailedChildCount = checked(SinkFailedChildCount + (child.EndReason == PcpRelayChildEndReason.SinkFailed ? 1 : 0)),
        SlowConsumerChildCount = checked(SlowConsumerChildCount + (child.EndReason == PcpRelayChildEndReason.SlowConsumer ? 1 : 0)),
        OwnerRemovedChildCount = checked(OwnerRemovedChildCount + (child.EndReason == PcpRelayChildEndReason.OwnerRemoved ? 1 : 0)),
        FanoutStoppedChildCount = checked(FanoutStoppedChildCount + (child.EndReason == PcpRelayChildEndReason.FanoutStopped ? 1 : 0)),
        CleanupFailedChildCount = checked(CleanupFailedChildCount + (child.CleanupFailed ? 1 : 0)),
        StartupPendingChildCount = checked(StartupPendingChildCount + (child.StartupPending ? 1 : 0)),
        StartupFailedChildCount = checked(StartupFailedChildCount + (child.StartupFailed ? 1 : 0)),
    };
}

public enum PcpRelayCompletionOutcome
{
    Completed,
    TimedOut,
    Cancelled,
    DeliveryFailed,
    CleanupIncomplete,
    Failed,
}

/// <summary>Local send/child barriers, not remote delivery or playback proof.</summary>
public sealed record PcpRelayFanoutCompletionResult(
    PcpRelayCompletionOutcome Outcome,
    bool SendsCompleted,
    bool ChildrenStopped,
    bool CleanupCompleted,
    PcpRelayFanoutEvidence Evidence);

public enum PcpRelayCleanupOutcome { Completed, Incomplete, Failed }

/// <summary>Every child task must really finish; a bounded wait expiring is Incomplete.</summary>
public sealed record PcpRelayFanoutCleanupResult(
    PcpRelayCleanupOutcome Outcome,
    bool ChildrenStopped,
    bool CleanupCompleted,
    PcpRelayFanoutEvidence Evidence);

public sealed record PcpHttpFlvCompletionResult(
    PcpRelayCompletionOutcome Outcome,
    bool SourceRemoved,
    bool CleanupCompleted,
    PcpRelayFanoutCompletionResult Fanout)
{
    public bool SourceCleanupFailed { get; init; }
}
