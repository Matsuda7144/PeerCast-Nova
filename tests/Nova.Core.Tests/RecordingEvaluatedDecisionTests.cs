using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Core.Tests;

public sealed class RecordingEvaluatedDecisionTests
{
    [Theory]
    [InlineData("Record", "Idle")]
    [InlineData("Record", "Automatic")]
    [InlineData("Record", "Manual")]
    [InlineData("Ignore", "Idle")]
    [InlineData("Ignore", "Automatic")]
    [InlineData("Ignore", "Manual")]
    [InlineData("NoMatch", "Idle")]
    [InlineData("NoMatch", "Automatic")]
    [InlineData("NoMatch", "Manual")]
    public async Task DecisionBoundaryUsesSameDecisionAndPreservesLegacyPlanner(string kind, string origin)
    {
        var channel = new ChannelId(Guid.NewGuid()); var candidate = new RecordingCandidate(channel, new ChannelMetadataSnapshot(name: "test"));
        var rule = new RecordingRule(Guid.NewGuid(), "test", kind == "Ignore" ? RecordingRuleAction.Ignore : RecordingRuleAction.Record, RecordingRuleCriteria.MatchAll);
        var rules = kind == "NoMatch" ? new RecordingRuleSet([]) : new([rule]);
        var decision = rules.Evaluate(candidate);
        var state = origin switch { "Manual" => RecordingAutomationState.Manual, "Automatic" => RecordingAutomationState.Recording(Guid.NewGuid()), _ => RecordingAutomationState.Idle };
        var legacy = RecordingAutomationPlanner.Evaluate(state, candidate, rules);
        var plan = RecordingAutomationPlanner.Plan(state, decision);
        Assert.Equal(legacy, plan);
        var sink = new Sink();
        var session = origin == "Manual" ? RecordingSinkSession.Manual(new(Guid.NewGuid()), channel)
            : new RecordingSinkSession(new(Guid.NewGuid()), channel, state.ActiveRuleId ?? rule.Id);
        var initial = origin == "Idle" ? RecordingAutomationExecutionState.Idle : origin == "Manual"
            ? RecordingAutomationExecutionState.ManualRecording(session) : RecordingAutomationExecutionState.Recording(state, session);
        using var coordinator = new RecordingAutomationChannelCoordinator(channel, sink, initial);
        var result = await coordinator.EvaluateDecisionAsync(candidate, decision, null, CancellationToken.None);
        Assert.Same(decision, result.Plan.Decision);
        Assert.Equal(legacy.Action, result.Plan.Action);
        Assert.Equal(legacy.NextState, result.Plan.NextState);
        if (result.State.Session is not null) await coordinator.StopAsync(CancellationToken.None);
        using var manager = new RecordingAutomationManager(sink);
        result = await manager.EvaluateDecisionAsync(candidate, decision, null, CancellationToken.None);
        Assert.Same(decision, result.Plan.Decision);
        await manager.ShutdownAsync(CancellationToken.None);
    }

    private sealed class Sink : IRecordingSink
    {
        public ValueTask<RecordingSinkStartResult> StartAsync(RecordingSinkStartRequest request, CancellationToken token) =>
            ValueTask.FromResult(RecordingSinkStartResult.Started(new RecordingSinkSession(new RecordingSessionId(Guid.NewGuid()), request.Candidate.ChannelId, request.RuleId!.Value)));
        public ValueTask<RecordingSinkStopResult> StopAsync(RecordingSinkStopRequest request, CancellationToken token) =>
            ValueTask.FromResult(RecordingSinkStopResult.Stopped);
    }
}
