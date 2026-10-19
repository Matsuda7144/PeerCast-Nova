namespace PeerCast.Nova.Core.Recording;

public enum RecordingSinkStopNotAdmittedReason
{
    CancelledBeforeAdmission,
    AlreadyTerminal,
    AlreadyStoppingByOtherOperation,
    Unavailable,
    Rejected,
    FailureBeforeAdmission,
}

/// <summary>
/// Canonical evidence of one exact session's stop effect. Only a successfully
/// completed typed Completion proves terminal cleanup. Caller waits may cancel
/// independently of this task.
/// </summary>
public sealed class RecordingSinkStopAdmission
{
    public RecordingSinkStopAdmission(
        RecordingSinkSession session,
        Task<RecordingSinkStopResult> completion)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(completion);
        Session = session;
        Completion = completion;
    }

    public RecordingSinkSession Session { get; }

    public Task<RecordingSinkStopResult> Completion { get; }

    private RecordingFaultDescriptor? resolutionFault;

    public RecordingFaultDescriptor? ResolutionFault => Volatile.Read(ref resolutionFault);

    internal void RecordResolutionFault(Exception exception) =>
        Volatile.Write(ref resolutionFault, RecordingFaultDescriptor.FromException(exception));
}

public sealed record RecordingSinkStopAdmissionResult
{
    private RecordingSinkStopAdmissionResult(
        RecordingSinkStopAdmission? admission,
        RecordingSinkStopNotAdmittedReason? reason)
    {
        Admission = admission;
        Reason = reason;
    }

    public RecordingSinkStopAdmission? Admission { get; }

    public RecordingSinkStopNotAdmittedReason? Reason { get; }

    public static RecordingSinkStopAdmissionResult Admitted(
        RecordingSinkStopAdmission admission)
    {
        ArgumentNullException.ThrowIfNull(admission);
        return new(admission, null);
    }

    public static RecordingSinkStopAdmissionResult NotAdmitted(
        RecordingSinkStopNotAdmittedReason reason) => new(null, reason);
}
