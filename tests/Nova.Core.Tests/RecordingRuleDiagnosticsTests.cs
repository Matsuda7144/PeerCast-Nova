using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Core.Tests;

public sealed class RecordingRuleDiagnosticsTests
{
    private static readonly ChannelId TestChannelId = new(
        Guid.ParseExact(
            "531DC8DFC7FB42928AC2C0A626517A87",
            "N"));

    [Fact]
    public void EvaluateDetailed_NoRules_ReturnsNoMatchAndEmptyDiagnostics()
    {
        var report = RecordingRuleEngine.EvaluateDetailed(
            CreateCandidate(),
            []);

        Assert.Equal(
            RecordingRuleDecisionKind.NoMatch,
            report.Decision.Kind);
        Assert.Empty(report.RuleEvaluations);
    }

    [Fact]
    public void EvaluateDetailed_DisabledRule_IsReportedWithoutFailures()
    {
        var rule = CreateRule(
            RecordingRuleAction.Record,
            RecordingRuleCriteria.MatchAll,
            isEnabled: false);

        var report = RecordingRuleEngine.EvaluateDetailed(
            CreateCandidate(),
            [rule]);

        var evaluation = Assert.Single(report.RuleEvaluations);

        Assert.Equal(
            RecordingRuleEvaluationStatus.Disabled,
            evaluation.Status);
        Assert.Empty(evaluation.FailedCriteria);
        Assert.Equal(
            RecordingRuleDecisionKind.NoMatch,
            report.Decision.Kind);
    }

    [Fact]
    public void EvaluateDetailed_NoMatch_ReportsAllFailedCriteriaInStableOrder()
    {
        var rule = CreateRule(
            RecordingRuleAction.Record,
            new RecordingRuleCriteria(
                name: new RecordingTextPattern("expected"),
                genre: new RecordingTextPattern("games"),
                minimumBitrateKbps: 2000));

        var report = RecordingRuleEngine.EvaluateDetailed(
            CreateCandidate(
                name: "other",
                genre: "talk",
                bitrateKbps: 1000),
            [rule]);

        var evaluation = Assert.Single(report.RuleEvaluations);

        Assert.Equal(
            RecordingRuleEvaluationStatus.NoMatch,
            evaluation.Status);
        Assert.Equal(
            [
                RecordingRuleCriterion.Name,
                RecordingRuleCriterion.Genre,
                RecordingRuleCriterion.MinimumBitrateKbps,
            ],
            evaluation.FailedCriteria);
    }

    [Fact]
    public void EvaluateDetailed_HigherPriorityMatch_IsSelected()
    {
        var lower = CreateRule(
            RecordingRuleAction.Record,
            RecordingRuleCriteria.MatchAll,
            priority: 100);

        var higher = CreateRule(
            RecordingRuleAction.Ignore,
            RecordingRuleCriteria.MatchAll,
            priority: 200);

        var report = RecordingRuleEngine.EvaluateDetailed(
            CreateCandidate(),
            [lower, higher]);

        Assert.Equal(
            RecordingRuleDecisionKind.Ignore,
            report.Decision.Kind);
        Assert.Same(higher, report.Decision.MatchedRule);

        Assert.Equal(
            RecordingRuleEvaluationStatus.Matched,
            report.RuleEvaluations[0].Status);
        Assert.Equal(
            RecordingRuleEvaluationStatus.Selected,
            report.RuleEvaluations[1].Status);
    }

    [Fact]
    public void EvaluateDetailed_EqualPriority_PreservesFirstMatch()
    {
        var first = CreateRule(
            RecordingRuleAction.Record,
            RecordingRuleCriteria.MatchAll,
            priority: 100);

        var second = CreateRule(
            RecordingRuleAction.Ignore,
            RecordingRuleCriteria.MatchAll,
            priority: 100);

        var report = RecordingRuleEngine.EvaluateDetailed(
            CreateCandidate(),
            [first, second]);

        Assert.Same(first, report.Decision.MatchedRule);
        Assert.Equal(
            RecordingRuleEvaluationStatus.Selected,
            report.RuleEvaluations[0].Status);
        Assert.Equal(
            RecordingRuleEvaluationStatus.Matched,
            report.RuleEvaluations[1].Status);
    }

    [Fact]
    public void EvaluateDetailed_DecisionMatchesNormalEvaluation()
    {
        var rules = new[]
        {
            CreateRule(
                RecordingRuleAction.Record,
                new RecordingRuleCriteria(
                    name: new RecordingTextPattern("missing")),
                priority: 300),
            CreateRule(
                RecordingRuleAction.Ignore,
                new RecordingRuleCriteria(
                    genre: new RecordingTextPattern("games")),
                priority: 200),
            CreateRule(
                RecordingRuleAction.Record,
                RecordingRuleCriteria.MatchAll,
                priority: 100),
        };

        var candidate = CreateCandidate(
            name: "channel",
            genre: "games",
            bitrateKbps: 1500);

        var normal = RecordingRuleEngine.Evaluate(
            candidate,
            rules);
        var detailed = RecordingRuleEngine.EvaluateDetailed(
            candidate,
            rules);

        Assert.Equal(normal.Kind, detailed.Decision.Kind);
        Assert.Same(
            normal.MatchedRule,
            detailed.Decision.MatchedRule);
    }

    private static RecordingRule CreateRule(
        RecordingRuleAction action,
        RecordingRuleCriteria criteria,
        int priority = 0,
        bool isEnabled = true)
    {
        return new RecordingRule(
            Guid.NewGuid(),
            "Test rule",
            action,
            criteria,
            priority,
            isEnabled);
    }

    private static RecordingCandidate CreateCandidate(
        string? name = "Test channel",
        string? genre = null,
        int? bitrateKbps = null)
    {
        return new RecordingCandidate(
            TestChannelId,
            new ChannelMetadataSnapshot(
                name: name,
                genre: genre,
                bitrateKbps: bitrateKbps));
    }
}
