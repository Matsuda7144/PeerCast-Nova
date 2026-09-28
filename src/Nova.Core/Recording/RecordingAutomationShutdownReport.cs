namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Ordered results from one multi-channel graceful shutdown pass.
/// </summary>
public sealed record RecordingAutomationShutdownReport
{
    internal RecordingAutomationShutdownReport(
        IEnumerable<RecordingAutomationStopResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        Results = results.ToArray();
    }

    public IReadOnlyList<RecordingAutomationStopResult> Results { get; }

    public bool AllInactive =>
        Results.All(static result =>
            !result.State.AutomationState.IsRecording &&
            result.State.Session is null);
}
