using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Core.Tests;

public sealed class RecordingAutomationExecutorTests
{
    private static readonly ChannelId TestChannelId = new(
        Guid.ParseExact(
            "531DC8DFC7FB42928AC2C0A626517A87",
            "N"));

    [Fact]
    public async Task Start_Success_AdvancesOnlyAfterSinkConfirmation()
    {
        var rule = CreateRule(RecordingRuleAction.Record);
        var candidate = CreateCandidate();
        var plan = RecordingAutomationPlanner.Evaluate(
            RecordingAutomationState.Idle,
            candidate,
            new RecordingRuleSet([rule]));
        var session = CreateSession(
            candidate.ChannelId,
            rule.Id);
        var sink = new TestSink(
            RecordingSinkStartResult.Started(session));

        var result = await RecordingAutomationExecutor.ExecuteAsync(
            RecordingAutomationExecutionState.Idle,
            plan,
            candidate,
            sink,
            CancellationToken.None);

        Assert.Equal(
            RecordingAutomationExecutionResultKind.Started,
            result.Kind);
        Assert.True(
            result.State.AutomationState.IsRecording);
        Assert.Equal(
            rule.Id,
            result.State.AutomationState.ActiveRuleId);
        Assert.Same(
            session,
            result.State.Session);
        Assert.Equal(1, sink.StartCallCount);
        Assert.Equal(0, sink.StopCallCount);
    }

    [Fact]
    public async Task Start_Rejected_PreservesIdleState()
    {
        var rule = CreateRule(RecordingRuleAction.Record);
        var candidate = CreateCandidate();
        var plan = RecordingAutomationPlanner.Evaluate(
            RecordingAutomationState.Idle,
            candidate,
            new RecordingRuleSet([rule]));
        var sink = new TestSink(
            RecordingSinkStartResult.Rejected);

        var result = await RecordingAutomationExecutor.ExecuteAsync(
            RecordingAutomationExecutionState.Idle,
            plan,
            candidate,
            sink,
            CancellationToken.None);

        Assert.Equal(
            RecordingAutomationExecutionResultKind.StartRejected,
            result.Kind);
        Assert.Same(
            RecordingAutomationExecutionState.Idle,
            result.State);
    }

    [Fact]
    public async Task Start_AlreadyRunning_AdoptsReturnedSession()
    {
        var rule = CreateRule(RecordingRuleAction.Record);
        var candidate = CreateCandidate();
        var plan = RecordingAutomationPlanner.Evaluate(
            RecordingAutomationState.Idle,
            candidate,
            new RecordingRuleSet([rule]));
        var session = CreateSession(
            candidate.ChannelId,
            Guid.NewGuid());
        var sink = new TestSink(
            RecordingSinkStartResult.AlreadyRunning(session));

        var result = await RecordingAutomationExecutor.ExecuteAsync(
            RecordingAutomationExecutionState.Idle,
            plan,
            candidate,
            sink,
            CancellationToken.None);

        Assert.Equal(
            RecordingAutomationExecutionResultKind.StartAlreadyRunning,
            result.Kind);
        Assert.Same(session, result.State.Session);
        Assert.Equal(
            rule.Id,
            result.State.AutomationState.ActiveRuleId);
    }

    [Fact]
    public async Task NoChange_Recording_DoesNotCallSinkAndUpdatesRuleProvenance()
    {
        var previousRule = CreateRule(
            RecordingRuleAction.Record,
            priority: 10);
        var selectedRule = CreateRule(
            RecordingRuleAction.Record,
            priority: 20);
        var candidate = CreateCandidate();
        var session = CreateSession(
            candidate.ChannelId,
            previousRule.Id);
        var current = RecordingAutomationExecutionState.Recording(
            RecordingAutomationState.Recording(
                previousRule.Id),
            session);
        var plan = RecordingAutomationPlanner.Evaluate(
            current.AutomationState,
            candidate,
            new RecordingRuleSet([selectedRule]));
        var sink = new TestSink(
            RecordingSinkStartResult.Rejected);

        var result = await RecordingAutomationExecutor.ExecuteAsync(
            current,
            plan,
            candidate,
            sink,
            CancellationToken.None);

        Assert.Equal(
            RecordingAutomationExecutionResultKind.NoChange,
            result.Kind);
        Assert.Same(session, result.State.Session);
        Assert.Equal(
            selectedRule.Id,
            result.State.AutomationState.ActiveRuleId);
        Assert.Equal(0, sink.StartCallCount);
        Assert.Equal(0, sink.StopCallCount);
    }

    [Theory]
    [InlineData(
        RecordingSinkStopResultKind.Stopped,
        RecordingAutomationExecutionResultKind.Stopped)]
    [InlineData(
        RecordingSinkStopResultKind.AlreadyStopped,
        RecordingAutomationExecutionResultKind.StopAlreadyStopped)]
    [InlineData(
        RecordingSinkStopResultKind.Faulted,
        RecordingAutomationExecutionResultKind.StopFaulted)]
    public async Task Stop_Confirmed_ClearsSession(
        RecordingSinkStopResultKind sinkKind,
        RecordingAutomationExecutionResultKind expectedKind)
    {
        var recordRule = CreateRule(
            RecordingRuleAction.Record);
        var ignoreRule = CreateRule(
            RecordingRuleAction.Ignore);
        var candidate = CreateCandidate();
        var session = CreateSession(
            candidate.ChannelId,
            recordRule.Id);
        var current = RecordingAutomationExecutionState.Recording(
            RecordingAutomationState.Recording(
                recordRule.Id),
            session);
        var plan = RecordingAutomationPlanner.Evaluate(
            current.AutomationState,
            candidate,
            new RecordingRuleSet([ignoreRule]));
        var sink = new TestSink(
            RecordingSinkStartResult.Rejected,
            CreateStopResult(sinkKind));

        var result = await RecordingAutomationExecutor.ExecuteAsync(
            current,
            plan,
            candidate,
            sink,
            CancellationToken.None);

        Assert.Equal(expectedKind, result.Kind);
        Assert.Same(
            RecordingAutomationExecutionState.Idle,
            result.State);
        Assert.Equal(0, sink.StartCallCount);
        Assert.Equal(1, sink.StopCallCount);
    }

    [Fact]
    public async Task Stop_Rejected_PreservesActiveSession()
    {
        var recordRule = CreateRule(
            RecordingRuleAction.Record);
        var ignoreRule = CreateRule(
            RecordingRuleAction.Ignore);
        var candidate = CreateCandidate();
        var session = CreateSession(
            candidate.ChannelId,
            recordRule.Id);
        var current = RecordingAutomationExecutionState.Recording(
            RecordingAutomationState.Recording(
                recordRule.Id),
            session);
        var plan = RecordingAutomationPlanner.Evaluate(
            current.AutomationState,
            candidate,
            new RecordingRuleSet([ignoreRule]));
        var sink = new TestSink(
            RecordingSinkStartResult.Rejected,
            RecordingSinkStopResult.Rejected);

        var result = await RecordingAutomationExecutor.ExecuteAsync(
            current,
            plan,
            candidate,
            sink,
            CancellationToken.None);

        Assert.Equal(
            RecordingAutomationExecutionResultKind.StopRejected,
            result.Kind);
        Assert.Same(current, result.State);
        Assert.Same(session, result.State.Session);
    }

    [Fact]
    public async Task Start_SessionForDifferentChannel_IsRejectedAsContractViolation()
    {
        var rule = CreateRule(RecordingRuleAction.Record);
        var candidate = CreateCandidate();
        var plan = RecordingAutomationPlanner.Evaluate(
            RecordingAutomationState.Idle,
            candidate,
            new RecordingRuleSet([rule]));
        var otherChannel = new ChannelId(Guid.NewGuid());
        var sink = new TestSink(
            RecordingSinkStartResult.Started(
                CreateSession(
                    otherChannel,
                    rule.Id)));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await RecordingAutomationExecutor.ExecuteAsync(
                RecordingAutomationExecutionState.Idle,
                plan,
                candidate,
                sink,
                CancellationToken.None));
    }

    [Fact]
    public async Task PreCancelledExecution_DoesNotCallSink()
    {
        var rule = CreateRule(RecordingRuleAction.Record);
        var candidate = CreateCandidate();
        var plan = RecordingAutomationPlanner.Evaluate(
            RecordingAutomationState.Idle,
            candidate,
            new RecordingRuleSet([rule]));
        var sink = new TestSink(
            RecordingSinkStartResult.Rejected);
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await RecordingAutomationExecutor.ExecuteAsync(
                RecordingAutomationExecutionState.Idle,
                plan,
                candidate,
                sink,
                source.Token));

        Assert.Equal(0, sink.StartCallCount);
        Assert.Equal(0, sink.StopCallCount);
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

    private static RecordingSinkSession CreateSession(
        ChannelId channelId,
        Guid startedByRuleId)
    {
        return new RecordingSinkSession(
            new RecordingSessionId(Guid.NewGuid()),
            channelId,
            startedByRuleId);
    }

    private static RecordingSinkStopResult CreateStopResult(
        RecordingSinkStopResultKind kind)
    {
        return kind switch
        {
            RecordingSinkStopResultKind.Stopped =>
                RecordingSinkStopResult.Stopped,

            RecordingSinkStopResultKind.AlreadyStopped =>
                RecordingSinkStopResult.AlreadyStopped,

            RecordingSinkStopResultKind.Rejected =>
                RecordingSinkStopResult.Rejected,

            RecordingSinkStopResultKind.Faulted =>
                RecordingSinkStopResult.Faulted,

            _ => throw new ArgumentOutOfRangeException(
                nameof(kind)),
        };
    }

    private sealed class TestSink : IRecordingSink
    {
        private readonly RecordingSinkStartResult startResult;
        private readonly RecordingSinkStopResult stopResult;

        public TestSink(
            RecordingSinkStartResult startResult,
            RecordingSinkStopResult? stopResult = null)
        {
            this.startResult = startResult;
            this.stopResult =
                stopResult ?? RecordingSinkStopResult.Stopped;
        }

        public int StartCallCount { get; private set; }

        public int StopCallCount { get; private set; }

        public ValueTask<RecordingSinkStartResult> StartAsync(
            RecordingSinkStartRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            StartCallCount++;

            return ValueTask.FromResult(startResult);
        }

        public ValueTask<RecordingSinkStopResult> StopAsync(
            RecordingSinkStopRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            StopCallCount++;

            return ValueTask.FromResult(stopResult);
        }
    }
}
