namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Detailed recording decision and ordered per-rule diagnostics.
/// </summary>
public sealed record RecordingRuleEvaluationReport
{
    internal RecordingRuleEvaluationReport(
        RecordingRuleDecision decision,
        IEnumerable<RecordingRuleEvaluation> ruleEvaluations)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(ruleEvaluations);

        Decision = decision;
        RuleEvaluations = ruleEvaluations.ToArray();
    }

    public RecordingRuleDecision Decision { get; }

    public IReadOnlyList<RecordingRuleEvaluation> RuleEvaluations { get; }
}
