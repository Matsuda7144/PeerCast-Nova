namespace PeerCast.Nova.Network.Pcp;

public enum PcpRecordingSourceCompletionOutcome
{
    NormalEof,
    SourceFault,
    DeliveryFailed,
    TimedOut,
    Cancelled,
    CleanupIncomplete,
    Failed,
}

public enum PcpRecordingSourceCompletionPhase
{
    Acquisition,
    PacketDrain,
    TerminalNotification,
    ChildStop,
    LeaseCleanup,
    Finished,
}

/// <summary>Fixed-size source facts; no file path, exception, reader or writer ownership.</summary>
public sealed record PcpRecordingSourceEvidence
{
    public bool AdmissionClosed { get; init; }
    public bool OwnerFaultRequested { get; init; }
    public bool AcceptedTerminalFault { get; init; }
    public bool TerminalNotificationSelected { get; init; }
    public bool TerminalNotificationCompleted { get; init; }
    public int RejectedLeaseCleanupPendingCount { get; init; }
    public long RejectedLeaseCleanupFailureCount { get; init; }
    public int PendingAcquisitionCount { get; init; }
    public long AcquiredLeaseCount { get; init; }
    public long RetiredLeaseCount { get; init; }
    public int ActiveLeaseCount { get; init; }
    public int CleanupPendingLeaseCount { get; init; }
    public long EofNotificationCount { get; init; }
    public long FaultNotificationCount { get; init; }
    public long SourceFaultLeaseCount { get; init; }
    public long AbortedLeaseCount { get; init; }
    public long DeliveryFailedLeaseCount { get; init; }
    public long ChildStoppedCount { get; init; }
    public long CleanupCompletedLeaseCount { get; init; }
    public long CleanupFailedLeaseCount { get; init; }
    public long RegistrationCleanupFailedLeaseCount { get; init; }
    public long BridgeCleanupFailedLeaseCount { get; init; }
    public long OwnerCleanupFailedLeaseCount { get; init; }
    public long AcquisitionFailureCount { get; init; }
    public long PendingPacketCount { get; init; }
    public long DeliveredPacketCount { get; init; }
    public long DeliveredByteCount { get; init; }
    public long DiscardedPacketCount { get; init; }
    public long DiscardedByteCount { get; init; }

    public bool PacketsSettled => PendingAcquisitionCount == 0 && PendingPacketCount == 0;
    public bool DeliveryFailed => DeliveryFailedLeaseCount != 0 || DiscardedPacketCount != 0;
    public bool EofNotified => TerminalNotificationCompleted && !OwnerFaultRequested &&
        PendingAcquisitionCount == 0 && SourceFaultLeaseCount == 0 && AbortedLeaseCount == 0 &&
        EofNotificationCount == AcquiredLeaseCount;
    public bool ChildrenStopped => ChildStoppedCount == AcquiredLeaseCount && PendingAcquisitionCount == 0;
    public bool CleanupCompleted => PendingAcquisitionCount == 0 && CleanupPendingLeaseCount == 0 &&
        RejectedLeaseCleanupPendingCount == 0 && RejectedLeaseCleanupFailureCount == 0 &&
        CleanupFailedLeaseCount == 0 && AcquisitionFailureCount == 0 && CleanupCompletedLeaseCount == AcquiredLeaseCount;

    internal PcpRecordingSourceEvidence Add(PcpRecordingSourceEvidence lease) => this with
    {
        ActiveLeaseCount = checked(ActiveLeaseCount + lease.ActiveLeaseCount),
        CleanupPendingLeaseCount = checked(CleanupPendingLeaseCount + lease.CleanupPendingLeaseCount),
        EofNotificationCount = checked(EofNotificationCount + lease.EofNotificationCount),
        FaultNotificationCount = checked(FaultNotificationCount + lease.FaultNotificationCount),
        SourceFaultLeaseCount = checked(SourceFaultLeaseCount + lease.SourceFaultLeaseCount),
        AbortedLeaseCount = checked(AbortedLeaseCount + lease.AbortedLeaseCount),
        DeliveryFailedLeaseCount = checked(DeliveryFailedLeaseCount + lease.DeliveryFailedLeaseCount),
        ChildStoppedCount = checked(ChildStoppedCount + lease.ChildStoppedCount),
        CleanupCompletedLeaseCount = checked(CleanupCompletedLeaseCount + lease.CleanupCompletedLeaseCount),
        CleanupFailedLeaseCount = checked(CleanupFailedLeaseCount + lease.CleanupFailedLeaseCount),
        RegistrationCleanupFailedLeaseCount = checked(RegistrationCleanupFailedLeaseCount + lease.RegistrationCleanupFailedLeaseCount),
        BridgeCleanupFailedLeaseCount = checked(BridgeCleanupFailedLeaseCount + lease.BridgeCleanupFailedLeaseCount),
        OwnerCleanupFailedLeaseCount = checked(OwnerCleanupFailedLeaseCount + lease.OwnerCleanupFailedLeaseCount),
        PendingPacketCount = checked(PendingPacketCount + lease.PendingPacketCount),
        DeliveredPacketCount = checked(DeliveredPacketCount + lease.DeliveredPacketCount),
        DeliveredByteCount = checked(DeliveredByteCount + lease.DeliveredByteCount),
        DiscardedPacketCount = checked(DiscardedPacketCount + lease.DiscardedPacketCount),
        DiscardedByteCount = checked(DiscardedByteCount + lease.DiscardedByteCount),
    };
}

public sealed record PcpRecordingSourceCompletionResult(
    PcpRecordingSourceCompletionOutcome Outcome,
    PcpRecordingSourceCompletionPhase Phase,
    bool DeadlineExpired,
    PcpRecordingSourceEvidence Evidence)
{
    // The source factory receives neither the writer-owned SessionId nor its terminal observer.
    public bool WriterFinalizationObserved { get; }
}
