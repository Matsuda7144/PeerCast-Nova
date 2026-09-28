namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Diagnostic result for one rule without duplicating candidate metadata.
/// </summary>
public sealed record RecordingRuleEvaluation
{
    internal RecordingRuleEvaluation(
        RecordingRule rule,
        RecordingRuleEvaluationStatus status,
        IReadOnlyList<RecordingRuleCriterion> failedCriteria)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(failedCriteria);

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (status == RecordingRuleEvaluationStatus.NoMatch &&
            failedCriteria.Count == 0)
        {
            throw new ArgumentException(
                "A non-matching rule must identify at least one failed criterion.",
                nameof(failedCriteria));
        }

        if (status != RecordingRuleEvaluationStatus.NoMatch &&
            failedCriteria.Count != 0)
        {
            throw new ArgumentException(
                "Only non-matching rules may expose failed criteria.",
                nameof(failedCriteria));
        }

        Rule = rule;
        Status = status;
        FailedCriteria = failedCriteria.Count == 0
            ? Array.Empty<RecordingRuleCriterion>()
            : failedCriteria.ToArray();
    }

    public RecordingRule Rule { get; }

    public RecordingRuleEvaluationStatus Status { get; }

    public IReadOnlyList<RecordingRuleCriterion> FailedCriteria { get; }
}
