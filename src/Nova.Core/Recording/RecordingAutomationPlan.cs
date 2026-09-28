namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Pure transition result. The caller owns execution of Start or Stop.
/// </summary>
public sealed record RecordingAutomationPlan
{
    internal RecordingAutomationPlan(
        RecordingAutomationAction action,
        RecordingAutomationState nextState,
        RecordingRuleDecision decision)
    {
        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }

        ArgumentNullException.ThrowIfNull(nextState);
        ArgumentNullException.ThrowIfNull(decision);

        Action = action;
        NextState = nextState;
        Decision = decision;
    }

    public RecordingAutomationAction Action { get; }

    public RecordingAutomationState NextState { get; }

    public RecordingRuleDecision Decision { get; }
}
