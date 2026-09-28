namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Pure recording policy evaluator. It does not start or stop recording.
/// </summary>
public static class RecordingRuleEngine
{
    public static RecordingRuleDecision Evaluate(
        RecordingCandidate candidate,
        IEnumerable<RecordingRule> rules)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(rules);

        RecordingRule? winner = null;

        foreach (var rule in rules)
        {
            ArgumentNullException.ThrowIfNull(rule);

            if (!rule.IsEnabled ||
                !rule.Criteria.Matches(candidate))
            {
                continue;
            }

            if (winner is null ||
                rule.Priority > winner.Priority)
            {
                winner = rule;
            }
        }

        return winner is null
            ? RecordingRuleDecision.NoMatch
            : RecordingRuleDecision.FromRule(winner);
    }

    public static RecordingRuleEvaluationReport EvaluateDetailed(
        RecordingCandidate candidate,
        IEnumerable<RecordingRule> rules)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(rules);

        var observations = new List<RuleObservation>();
        var selectedIndex = -1;

        foreach (var rule in rules)
        {
            ArgumentNullException.ThrowIfNull(rule);

            if (!rule.IsEnabled)
            {
                observations.Add(
                    new RuleObservation(
                        rule,
                        RecordingRuleEvaluationStatus.Disabled,
                        Array.Empty<RecordingRuleCriterion>()));
                continue;
            }

            var failedCriteria = rule.Criteria.GetFailedCriteria(candidate);

            if (failedCriteria.Count != 0)
            {
                observations.Add(
                    new RuleObservation(
                        rule,
                        RecordingRuleEvaluationStatus.NoMatch,
                        failedCriteria));
                continue;
            }

            var currentIndex = observations.Count;
            observations.Add(
                new RuleObservation(
                    rule,
                    RecordingRuleEvaluationStatus.Matched,
                    Array.Empty<RecordingRuleCriterion>()));

            if (selectedIndex < 0 ||
                rule.Priority > observations[selectedIndex].Rule.Priority)
            {
                selectedIndex = currentIndex;
            }
        }

        RecordingRuleDecision decision;

        if (selectedIndex < 0)
        {
            decision = RecordingRuleDecision.NoMatch;
        }
        else
        {
            var selected = observations[selectedIndex];
            observations[selectedIndex] = selected with
            {
                Status = RecordingRuleEvaluationStatus.Selected,
            };
            decision = RecordingRuleDecision.FromRule(selected.Rule);
        }

        return new RecordingRuleEvaluationReport(
            decision,
            observations.Select(
                static observation => new RecordingRuleEvaluation(
                    observation.Rule,
                    observation.Status,
                    observation.FailedCriteria)));
    }

    private sealed record RuleObservation(
        RecordingRule Rule,
        RecordingRuleEvaluationStatus Status,
        IReadOnlyList<RecordingRuleCriterion> FailedCriteria);
}
