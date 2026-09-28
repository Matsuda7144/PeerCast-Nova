using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Core.Tests;

public sealed class RecordingRuleEngineTests
{
    private static readonly ChannelId TestChannelId = new(
        Guid.ParseExact(
            "531DC8DFC7FB42928AC2C0A626517A87",
            "N"));

    [Fact]
    public void Evaluate_NoMatchingRule_ReturnsNoMatch()
    {
        var result = RecordingRuleEngine.Evaluate(
            CreateCandidate(name: "ゾエアch"),
            []);

        Assert.Equal(
            RecordingRuleDecisionKind.NoMatch,
            result.Kind);
        Assert.Null(result.MatchedRule);
    }

    [Fact]
    public void Criteria_EmptyRequiresExplicitMatchAll()
    {
        Assert.True(RecordingRuleCriteria.MatchAll.IsMatchAll);
        Assert.Throws<ArgumentException>(
            () => new RecordingRuleCriteria());
    }

    [Fact]
    public void Evaluate_ChannelIdMatch_ReturnsRecord()
    {
        var rule = CreateRule(
            RecordingRuleAction.Record,
            new RecordingRuleCriteria(
                channelId: TestChannelId));

        var result = RecordingRuleEngine.Evaluate(
            CreateCandidate(),
            [rule]);

        Assert.Equal(
            RecordingRuleDecisionKind.Record,
            result.Kind);
        Assert.Same(rule, result.MatchedRule);
    }

    [Fact]
    public void Evaluate_TextContains_IsCaseInsensitive()
    {
        var rule = CreateRule(
            RecordingRuleAction.Record,
            new RecordingRuleCriteria(
                name: new RecordingTextPattern("nova")));

        var result = RecordingRuleEngine.Evaluate(
            CreateCandidate(name: "PeerCast NOVA"),
            [rule]);

        Assert.Equal(
            RecordingRuleDecisionKind.Record,
            result.Kind);
    }

    [Fact]
    public void Evaluate_TextContains_UsesCompatibilityNormalization()
    {
        var rule = CreateRule(
            RecordingRuleAction.Record,
            new RecordingRuleCriteria(
                name: new RecordingTextPattern("PeerCast")));

        var result = RecordingRuleEngine.Evaluate(
            CreateCandidate(name: "ＰｅｅｒＣａｓｔ Ｎｏｖａ"),
            [rule]);

        Assert.Equal(
            RecordingRuleDecisionKind.Record,
            result.Kind);
    }

    [Fact]
    public void Evaluate_MultipleCriteria_RequireAllConditions()
    {
        var rule = CreateRule(
            RecordingRuleAction.Record,
            new RecordingRuleCriteria(
                name: new RecordingTextPattern("ゾエア"),
                genre: new RecordingTextPattern("ゲーム"),
                minimumBitrateKbps: 1000));

        var matched = RecordingRuleEngine.Evaluate(
            CreateCandidate(
                name: "ゾエアch",
                genre: "ゲーム",
                bitrateKbps: 2000),
            [rule]);

        var rejected = RecordingRuleEngine.Evaluate(
            CreateCandidate(
                name: "ゾエアch",
                genre: "雑談",
                bitrateKbps: 2000),
            [rule]);

        Assert.Equal(
            RecordingRuleDecisionKind.Record,
            matched.Kind);
        Assert.Equal(
            RecordingRuleDecisionKind.NoMatch,
            rejected.Kind);
    }

    [Fact]
    public void Evaluate_UnknownBitrate_DoesNotMatchBitrateConstraint()
    {
        var rule = CreateRule(
            RecordingRuleAction.Record,
            new RecordingRuleCriteria(
                minimumBitrateKbps: 1000));

        var result = RecordingRuleEngine.Evaluate(
            CreateCandidate(bitrateKbps: null),
            [rule]);

        Assert.Equal(
            RecordingRuleDecisionKind.NoMatch,
            result.Kind);
    }

    [Fact]
    public void Evaluate_HigherPriorityRuleWins()
    {
        var record = CreateRule(
            RecordingRuleAction.Record,
            RecordingRuleCriteria.MatchAll,
            priority: 100);

        var ignore = CreateRule(
            RecordingRuleAction.Ignore,
            RecordingRuleCriteria.MatchAll,
            priority: 200);

        var result = RecordingRuleEngine.Evaluate(
            CreateCandidate(),
            [record, ignore]);

        Assert.Equal(
            RecordingRuleDecisionKind.Ignore,
            result.Kind);
        Assert.Same(ignore, result.MatchedRule);
    }

    [Fact]
    public void Evaluate_EqualPriority_PreservesRuleOrder()
    {
        var first = CreateRule(
            RecordingRuleAction.Record,
            RecordingRuleCriteria.MatchAll,
            priority: 100);

        var second = CreateRule(
            RecordingRuleAction.Ignore,
            RecordingRuleCriteria.MatchAll,
            priority: 100);

        var result = RecordingRuleEngine.Evaluate(
            CreateCandidate(),
            [first, second]);

        Assert.Equal(
            RecordingRuleDecisionKind.Record,
            result.Kind);
        Assert.Same(first, result.MatchedRule);
    }

    [Fact]
    public void Evaluate_DisabledRule_IsIgnored()
    {
        var rule = CreateRule(
            RecordingRuleAction.Record,
            RecordingRuleCriteria.MatchAll,
            isEnabled: false);

        var result = RecordingRuleEngine.Evaluate(
            CreateCandidate(),
            [rule]);

        Assert.Equal(
            RecordingRuleDecisionKind.NoMatch,
            result.Kind);
    }

    [Fact]
    public void Evaluate_RegexRule_Matches()
    {
        var rule = CreateRule(
            RecordingRuleAction.Record,
            new RecordingRuleCriteria(
                name: new RecordingTextPattern(
                    "^ゾエア.*ch$",
                    RecordingTextMatchMode.Regex)));

        var result = RecordingRuleEngine.Evaluate(
            CreateCandidate(name: "ゾエア TEST ch"),
            [rule]);

        Assert.Equal(
            RecordingRuleDecisionKind.Record,
            result.Kind);
    }

    [Fact]
    public void Evaluate_RegexTimeout_FailsClosedForNormalAndDetailedEvaluation()
    {
        var rule = CreateRule(
            RecordingRuleAction.Record,
            new RecordingRuleCriteria(
                name: new RecordingTextPattern(
                    "^(a+)+$",
                    RecordingTextMatchMode.Regex)));
        var candidate = CreateCandidate(
            name: string.Concat(
                new string('a', 50_000),
                "!"));

        var normal = RecordingRuleEngine.Evaluate(
            candidate,
            [rule]);
        var detailed = RecordingRuleEngine.EvaluateDetailed(
            candidate,
            [rule]);

        Assert.Equal(
            RecordingRuleDecisionKind.NoMatch,
            normal.Kind);
        Assert.Equal(
            RecordingRuleDecisionKind.NoMatch,
            detailed.Decision.Kind);

        var evaluation = Assert.Single(detailed.RuleEvaluations);
        Assert.Equal(
            RecordingRuleEvaluationStatus.NoMatch,
            evaluation.Status);
        Assert.Equal(
            [RecordingRuleCriterion.Name],
            evaluation.FailedCriteria);
    }

    [Fact]
    public void TextPattern_InvalidRegex_IsRejectedAtConstruction()
    {
        Assert.Throws<ArgumentException>(
            () => new RecordingTextPattern(
                "[",
                RecordingTextMatchMode.Regex));
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
