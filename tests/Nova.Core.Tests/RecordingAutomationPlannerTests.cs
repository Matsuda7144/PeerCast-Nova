using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Core.Tests;

public sealed class RecordingAutomationPlannerTests
{
    private static readonly ChannelId TestChannelId = new(
        Guid.ParseExact(
            "531DC8DFC7FB42928AC2C0A626517A87",
            "N"));

    [Fact]
    public void Evaluate_IdleToRecord_RequestsSingleStart()
    {
        var rule = CreateRule(
            RecordingRuleAction.Record,
            priority: 100);
        var ruleSet = new RecordingRuleSet([rule]);

        var plan = RecordingAutomationPlanner.Evaluate(
            RecordingAutomationState.Idle,
            CreateCandidate(),
            ruleSet);

        Assert.Equal(
            RecordingAutomationAction.Start,
            plan.Action);
        Assert.True(plan.NextState.IsRecording);
        Assert.Equal(
            rule.Id,
            plan.NextState.ActiveRuleId);
        Assert.Same(
            rule,
            plan.Decision.MatchedRule);
    }

    [Fact]
    public void Evaluate_RecordingToSameRecord_DoesNotRestart()
    {
        var rule = CreateRule(
            RecordingRuleAction.Record,
            priority: 100);
        var current =
            RecordingAutomationState.Recording(rule.Id);

        var plan = RecordingAutomationPlanner.Evaluate(
            current,
            CreateCandidate(),
            new RecordingRuleSet([rule]));

        Assert.Equal(
            RecordingAutomationAction.NoChange,
            plan.Action);
        Assert.Same(
            current,
            plan.NextState);
    }

    [Fact]
    public void Evaluate_RecordingToDifferentRecord_UpdatesProvenanceWithoutRestart()
    {
        var previousRule = CreateRule(
            RecordingRuleAction.Record,
            priority: 50);
        var selectedRule = CreateRule(
            RecordingRuleAction.Record,
            priority: 100);

        var plan = RecordingAutomationPlanner.Evaluate(
            RecordingAutomationState.Recording(
                previousRule.Id),
            CreateCandidate(),
            new RecordingRuleSet([selectedRule]));

        Assert.Equal(
            RecordingAutomationAction.NoChange,
            plan.Action);
        Assert.True(plan.NextState.IsRecording);
        Assert.Equal(
            selectedRule.Id,
            plan.NextState.ActiveRuleId);
    }

    [Fact]
    public void Evaluate_RecordingToIgnore_RequestsStop()
    {
        var previousRule = CreateRule(
            RecordingRuleAction.Record);
        var ignoreRule = CreateRule(
            RecordingRuleAction.Ignore);

        var plan = RecordingAutomationPlanner.Evaluate(
            RecordingAutomationState.Recording(
                previousRule.Id),
            CreateCandidate(),
            new RecordingRuleSet([ignoreRule]));

        Assert.Equal(
            RecordingAutomationAction.Stop,
            plan.Action);
        Assert.Same(
            RecordingAutomationState.Idle,
            plan.NextState);
        Assert.Equal(
            RecordingRuleDecisionKind.Ignore,
            plan.Decision.Kind);
    }

    [Fact]
    public void Evaluate_RecordingToNoMatch_RequestsStop()
    {
        var previousRule = CreateRule(
            RecordingRuleAction.Record);

        var noMatchRule = new RecordingRule(
            Guid.NewGuid(),
            "No match",
            RecordingRuleAction.Record,
            new RecordingRuleCriteria(
                name: new RecordingTextPattern(
                    "missing",
                    RecordingTextMatchMode.Exact)));

        var plan = RecordingAutomationPlanner.Evaluate(
            RecordingAutomationState.Recording(
                previousRule.Id),
            CreateCandidate(),
            new RecordingRuleSet([noMatchRule]));

        Assert.Equal(
            RecordingAutomationAction.Stop,
            plan.Action);
        Assert.Same(
            RecordingAutomationState.Idle,
            plan.NextState);
        Assert.Equal(
            RecordingRuleDecisionKind.NoMatch,
            plan.Decision.Kind);
    }

    [Fact]
    public void Evaluate_IdleToIgnore_DoesNothing()
    {
        var ignoreRule = CreateRule(
            RecordingRuleAction.Ignore);

        var current = RecordingAutomationState.Idle;

        var plan = RecordingAutomationPlanner.Evaluate(
            current,
            CreateCandidate(),
            new RecordingRuleSet([ignoreRule]));

        Assert.Equal(
            RecordingAutomationAction.NoChange,
            plan.Action);
        Assert.Same(current, plan.NextState);
    }

    [Fact]
    public void Evaluate_EmptyRuleSetWhileIdle_DoesNothing()
    {
        var current = RecordingAutomationState.Idle;

        var plan = RecordingAutomationPlanner.Evaluate(
            current,
            CreateCandidate(),
            new RecordingRuleSet([]));

        Assert.Equal(
            RecordingAutomationAction.NoChange,
            plan.Action);
        Assert.Same(current, plan.NextState);
        Assert.Equal(
            RecordingRuleDecisionKind.NoMatch,
            plan.Decision.Kind);
    }

    [Fact]
    public void RecordingState_EmptyRuleIdentifier_IsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => RecordingAutomationState.Recording(
                Guid.Empty));
    }

    private static RecordingRule CreateRule(
        RecordingRuleAction action,
        int priority = 0)
    {
        return new RecordingRule(
            Guid.NewGuid(),
            "Rule",
            action,
            RecordingRuleCriteria.MatchAll,
            priority);
    }

    private static RecordingCandidate CreateCandidate()
    {
        return new RecordingCandidate(
            TestChannelId,
            new ChannelMetadataSnapshot(
                name: "Channel",
                genre: "Games",
                bitrateKbps: 2000));
    }
}
