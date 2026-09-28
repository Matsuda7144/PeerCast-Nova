namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Converts rule decisions into idempotent automatic recording transitions.
/// It never starts or stops recording itself.
/// </summary>
public static class RecordingAutomationPlanner
{
    public static RecordingAutomationPlan Evaluate(
        RecordingAutomationState currentState,
        RecordingCandidate candidate,
        RecordingRuleSet ruleSet)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(ruleSet);

        return Plan(
            currentState,
            ruleSet.Evaluate(candidate));
    }

    public static RecordingAutomationPlan Plan(
        RecordingAutomationState currentState,
        RecordingRuleDecision decision)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        ArgumentNullException.ThrowIfNull(decision);

        if (currentState.IsRecording &&
            currentState.ActiveRuleId is null)
        {
            return new RecordingAutomationPlan(
                RecordingAutomationAction.NoChange,
                currentState,
                decision);
        }

        if (decision.Kind == RecordingRuleDecisionKind.Record)
        {
            var matchedRule = decision.MatchedRule ??
                throw new InvalidOperationException(
                    "A Record decision must identify its selected rule.");

            var nextState = currentState.IsRecording &&
                currentState.ActiveRuleId == matchedRule.Id
                ? currentState
                : RecordingAutomationState.Recording(
                    matchedRule.Id);

            return new RecordingAutomationPlan(
                currentState.IsRecording
                    ? RecordingAutomationAction.NoChange
                    : RecordingAutomationAction.Start,
                nextState,
                decision);
        }

        if (currentState.IsRecording)
        {
            return new RecordingAutomationPlan(
                RecordingAutomationAction.Stop,
                RecordingAutomationState.Idle,
                decision);
        }

        return new RecordingAutomationPlan(
            RecordingAutomationAction.NoChange,
            currentState,
            decision);
    }
}
