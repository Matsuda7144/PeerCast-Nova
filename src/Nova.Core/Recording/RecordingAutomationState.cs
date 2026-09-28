namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Immutable per-channel automatic recording state.
/// </summary>
public sealed record RecordingAutomationState
{
    private RecordingAutomationState(
        bool isRecording,
        Guid? activeRuleId)
    {
        IsRecording = isRecording;
        ActiveRuleId = activeRuleId;
    }

    public static RecordingAutomationState Idle { get; } =
        new(
            isRecording: false,
            activeRuleId: null);

    /// <summary>
    /// Active manual recording. Rules do not own or stop this session.
    /// </summary>
    public static RecordingAutomationState Manual { get; } =
        new(
            isRecording: true,
            activeRuleId: null);

    public bool IsRecording { get; }

    public Guid? ActiveRuleId { get; }

    public static RecordingAutomationState Recording(
        Guid activeRuleId)
    {
        if (activeRuleId == Guid.Empty)
        {
            throw new ArgumentException(
                "An active recording rule identifier cannot be empty.",
                nameof(activeRuleId));
        }

        return new RecordingAutomationState(
            isRecording: true,
            activeRuleId);
    }
}
