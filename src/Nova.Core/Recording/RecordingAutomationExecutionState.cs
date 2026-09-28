namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Retains both policy state and the concrete recorder session, if any.
/// </summary>
public sealed record RecordingAutomationExecutionState
{
    private RecordingAutomationExecutionState(
        RecordingAutomationState automationState,
        RecordingSinkSession? session)
    {
        AutomationState = automationState;
        Session = session;
    }

    public static RecordingAutomationExecutionState Idle { get; } =
        new(
            RecordingAutomationState.Idle,
            session: null);

    public RecordingAutomationState AutomationState { get; }

    public RecordingSinkSession? Session { get; }

    public static RecordingAutomationExecutionState Recording(
        RecordingAutomationState automationState,
        RecordingSinkSession session)
    {
        ArgumentNullException.ThrowIfNull(automationState);
        ArgumentNullException.ThrowIfNull(session);

        if (!automationState.IsRecording ||
            automationState.ActiveRuleId is null)
        {
            throw new ArgumentException(
                "A recording execution state requires an active automation recording state.",
                nameof(automationState));
        }

        return new RecordingAutomationExecutionState(
            automationState,
            session);
    }

    public static RecordingAutomationExecutionState ManualRecording(
        RecordingSinkSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.Origin != RecordingSessionOrigin.Manual)
        {
            throw new ArgumentException(
                "A manual execution state requires a manually started session.",
                nameof(session));
        }

        return new RecordingAutomationExecutionState(
            RecordingAutomationState.Manual,
            session);
    }
}
