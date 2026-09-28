namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Final outcome of evaluating an ordered recording rule set.
/// </summary>
public enum RecordingRuleDecisionKind
{
    NoMatch,
    Record,
    Ignore,
}

/// <summary>
/// Decision together with the rule that produced it.
/// </summary>
public sealed record RecordingRuleDecision
{
    private RecordingRuleDecision(
        RecordingRuleDecisionKind kind,
        RecordingRule? matchedRule)
    {
        Kind = kind;
        MatchedRule = matchedRule;
    }

    public static RecordingRuleDecision NoMatch { get; } =
        new(RecordingRuleDecisionKind.NoMatch, null);

    public RecordingRuleDecisionKind Kind { get; }

    public RecordingRule? MatchedRule { get; }

    internal static RecordingRuleDecision FromRule(
        RecordingRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var kind = rule.Action switch
        {
            RecordingRuleAction.Record =>
                RecordingRuleDecisionKind.Record,

            RecordingRuleAction.Ignore =>
                RecordingRuleDecisionKind.Ignore,

            _ => throw new InvalidOperationException(
                $"Unsupported recording rule action: {rule.Action}."),
        };

        return new RecordingRuleDecision(kind, rule);
    }
}
