using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Core.Recording;

public enum RecordingAutomaticStopNotAdmittedReason
{
    CancelledBeforeAdmission,
    StaleGeneration,
    ManualMismatch,
    NoActiveSession,
    AlreadyTerminal,
    AlreadyStoppingByOtherOperation,
    Rejected,
    ProvenanceUnavailable,
    Unavailable,
    FailureBeforeAdmission,
}

/// <summary>
/// Immutable automatic user-stop authority. ActiveRuleId is the committed
/// winner at admission, not the rule that originally started the session.
/// </summary>
public sealed class RecordingAutomaticStopAdmission
{
    internal RecordingAutomaticStopAdmission(
        ChannelId channelId,
        RecordingSessionId sessionId,
        Guid activeRuleId,
        Task<RecordingAutomationStopResult> completion)
    {
        ChannelId = channelId;
        SessionId = sessionId;
        ActiveRuleId = activeRuleId;
        Completion = completion;
    }

    public ChannelId ChannelId { get; }

    public RecordingSessionId SessionId { get; }

    public Guid ActiveRuleId { get; }

    public Task<RecordingAutomationStopResult> Completion { get; }

    /// <summary>
    /// An observed failure to resolve terminal authority. Completion remains
    /// pending; this diagnostic is never a substitute for terminal cleanup.
    /// </summary>
    public RecordingFaultDescriptor? ResolutionFault => Volatile.Read(ref resolutionFault);

    private RecordingFaultDescriptor? resolutionFault;

    internal void RecordResolutionFault(Exception exception) =>
        Volatile.Write(ref resolutionFault, RecordingFaultDescriptor.FromException(exception));
}

public sealed record RecordingAutomaticStopAdmissionResult
{
    private RecordingAutomaticStopAdmissionResult(
        RecordingAutomaticStopAdmission? admission,
        RecordingAutomaticStopNotAdmittedReason? reason,
        bool isNew,
        RecordingFaultDescriptor? fault = null)
    {
        Admission = admission;
        Reason = reason;
        IsNew = isNew;
        Fault = fault;
    }

    public RecordingAutomaticStopAdmission? Admission { get; }

    public RecordingAutomaticStopNotAdmittedReason? Reason { get; }

    public bool IsNew { get; }

    public RecordingFaultDescriptor? Fault { get; }

    internal static RecordingAutomaticStopAdmissionResult Admitted(
        RecordingAutomaticStopAdmission admission,
        bool isNew) => new(admission, null, isNew);

    internal static RecordingAutomaticStopAdmissionResult NotAdmitted(
        RecordingAutomaticStopNotAdmittedReason reason,
        RecordingFaultDescriptor? fault = null) => new(null, reason, false, fault);
}
