namespace PeerCast.Nova.Core.Recording;

public enum RecordingAutomationExecutionResultKind
{
    NoChange,
    Started,
    StartAlreadyRunning,
    StartRejected,
    Stopped,
    StopAlreadyStopped,
    StopRejected,
    StopFaulted,
}

/// <summary>
/// Result of applying one automation plan to the recorder boundary.
/// </summary>
public sealed record RecordingAutomationExecutionResult
{
    internal RecordingAutomationExecutionResult(
        RecordingAutomationExecutionResultKind kind,
        RecordingAutomationExecutionState state,
        RecordingAutomationPlan plan)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(plan);

        Kind = kind;
        State = state;
        Plan = plan;
    }

    public RecordingAutomationExecutionResultKind Kind { get; }

    public RecordingAutomationExecutionState State { get; }

    public RecordingAutomationPlan Plan { get; }
}
