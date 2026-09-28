using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Core.Tests;

public sealed class RecordingAutomationChannelCoordinatorTests
{
    private static readonly ChannelId TestChannelId = new(
        Guid.ParseExact(
            "531DC8DFC7FB42928AC2C0A626517A87",
            "N"));

    [Fact]
    public async Task ConcurrentRecordObservations_IssueOnlyOneStart()
    {
        var rule = CreateRule(
            RecordingRuleAction.Record);
        var rules = new RecordingRuleSet([rule]);
        var candidate = CreateCandidate(TestChannelId);
        var session = CreateSession(
            TestChannelId,
            rule.Id);
        var sink = new BlockingStartSink(
            RecordingSinkStartResult.Started(session));
        var coordinator =
            new RecordingAutomationChannelCoordinator(
                TestChannelId,
                sink);

        var first = coordinator
            .EvaluateAsync(
                candidate,
                rules,
                CancellationToken.None)
            .AsTask();

        await sink.StartEntered;

        var second = coordinator
            .EvaluateAsync(
                candidate,
                rules,
                CancellationToken.None)
            .AsTask();

        Assert.Equal(1, sink.StartCallCount);
        Assert.False(second.IsCompleted);

        sink.ReleaseStart();

        var firstResult = await first;
        var secondResult = await second;

        Assert.Equal(
            RecordingAutomationExecutionResultKind.Started,
            firstResult.Kind);
        Assert.Equal(
            RecordingAutomationExecutionResultKind.NoChange,
            secondResult.Kind);
        Assert.Equal(1, sink.StartCallCount);
        Assert.True(
            coordinator.State.AutomationState.IsRecording);
        Assert.Same(
            session,
            coordinator.State.Session);
    }

    [Fact]
    public async Task SinkFailure_DoesNotAdvanceCoordinatorState()
    {
        var rule = CreateRule(
            RecordingRuleAction.Record);
        var sink = new ThrowingStartSink();
        var coordinator =
            new RecordingAutomationChannelCoordinator(
                TestChannelId,
                sink);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await coordinator.EvaluateAsync(
                CreateCandidate(TestChannelId),
                new RecordingRuleSet([rule]),
                CancellationToken.None));

        Assert.Same(
            RecordingAutomationExecutionState.Idle,
            coordinator.State);
    }

    [Fact]
    public async Task RejectedStart_RemainsIdleAndCanBeRetried()
    {
        var rule = CreateRule(
            RecordingRuleAction.Record);
        var sink = new FixedSink(
            RecordingSinkStartResult.Rejected);
        var coordinator =
            new RecordingAutomationChannelCoordinator(
                TestChannelId,
                sink);
        var candidate = CreateCandidate(TestChannelId);
        var rules = new RecordingRuleSet([rule]);

        var first = await coordinator.EvaluateAsync(
            candidate,
            rules,
            CancellationToken.None);
        var second = await coordinator.EvaluateAsync(
            candidate,
            rules,
            CancellationToken.None);

        Assert.Equal(
            RecordingAutomationExecutionResultKind.StartRejected,
            first.Kind);
        Assert.Equal(
            RecordingAutomationExecutionResultKind.StartRejected,
            second.Kind);
        Assert.Equal(2, sink.StartCallCount);
        Assert.Same(
            RecordingAutomationExecutionState.Idle,
            coordinator.State);
    }

    [Fact]
    public async Task RecordingThenIgnore_StopsAndReturnsToIdle()
    {
        var recordRule = CreateRule(
            RecordingRuleAction.Record);
        var ignoreRule = CreateRule(
            RecordingRuleAction.Ignore);
        var session = CreateSession(
            TestChannelId,
            recordRule.Id);
        var initialState =
            RecordingAutomationExecutionState.Recording(
                RecordingAutomationState.Recording(
                    recordRule.Id),
                session);
        var sink = new FixedSink(
            RecordingSinkStartResult.Rejected,
            RecordingSinkStopResult.Stopped);
        var coordinator =
            new RecordingAutomationChannelCoordinator(
                TestChannelId,
                sink,
                initialState);

        var result = await coordinator.EvaluateAsync(
            CreateCandidate(TestChannelId),
            new RecordingRuleSet([ignoreRule]),
            CancellationToken.None);

        Assert.Equal(
            RecordingAutomationExecutionResultKind.Stopped,
            result.Kind);
        Assert.Equal(1, sink.StopCallCount);
        Assert.Same(
            RecordingAutomationExecutionState.Idle,
            coordinator.State);
    }

    [Fact]
    public async Task CandidateForDifferentChannel_IsRejectedBeforeSinkUse()
    {
        var sink = new FixedSink(
            RecordingSinkStartResult.Rejected);
        var coordinator =
            new RecordingAutomationChannelCoordinator(
                TestChannelId,
                sink);
        var otherChannel = new ChannelId(Guid.NewGuid());

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await coordinator.EvaluateAsync(
                CreateCandidate(otherChannel),
                new RecordingRuleSet(
                    [CreateRule(RecordingRuleAction.Record)]),
                CancellationToken.None));

        Assert.Equal(0, sink.StartCallCount);
        Assert.Equal(0, sink.StopCallCount);
    }

    [Fact]
    public void InitialSessionForDifferentChannel_IsRejected()
    {
        var otherChannel = new ChannelId(Guid.NewGuid());
        var ruleId = Guid.NewGuid();
        var initialState =
            RecordingAutomationExecutionState.Recording(
                RecordingAutomationState.Recording(ruleId),
                CreateSession(
                    otherChannel,
                    ruleId));

        Assert.Throws<ArgumentException>(
            () => new RecordingAutomationChannelCoordinator(
                TestChannelId,
                new FixedSink(
                    RecordingSinkStartResult.Rejected),
                initialState));
    }

    [Fact]
    public async Task Dispose_PreventsFurtherEvaluationWithoutUsingSink()
    {
        var sink = new FixedSink(
            RecordingSinkStartResult.Rejected);
        var coordinator =
            new RecordingAutomationChannelCoordinator(
                TestChannelId,
                sink);

        coordinator.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await coordinator.EvaluateAsync(
                CreateCandidate(TestChannelId),
                new RecordingRuleSet(
                    [CreateRule(RecordingRuleAction.Record)]),
                CancellationToken.None));

        Assert.Equal(0, sink.StartCallCount);
        Assert.Equal(0, sink.StopCallCount);
    }

    [Fact]
    public async Task PreCancelledWait_DoesNotEnterSink()
    {
        var rule = CreateRule(
            RecordingRuleAction.Record);
        var sink = new FixedSink(
            RecordingSinkStartResult.Rejected);
        var coordinator =
            new RecordingAutomationChannelCoordinator(
                TestChannelId,
                sink);
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await coordinator.EvaluateAsync(
                CreateCandidate(TestChannelId),
                new RecordingRuleSet([rule]),
                source.Token));

        Assert.Equal(0, sink.StartCallCount);
        Assert.Same(
            RecordingAutomationExecutionState.Idle,
            coordinator.State);
    }

    private static RecordingRule CreateRule(
        RecordingRuleAction action)
    {
        return new RecordingRule(
            Guid.NewGuid(),
            "Rule",
            action,
            RecordingRuleCriteria.MatchAll);
    }

    private static RecordingCandidate CreateCandidate(
        ChannelId channelId)
    {
        return new RecordingCandidate(
            channelId,
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

    private sealed class FixedSink : IRecordingSink
    {
        private readonly RecordingSinkStartResult startResult;
        private readonly RecordingSinkStopResult stopResult;

        public FixedSink(
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

    private sealed class ThrowingStartSink : IRecordingSink
    {
        public ValueTask<RecordingSinkStartResult> StartAsync(
            RecordingSinkStartRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            throw new InvalidOperationException(
                "Synthetic recorder failure.");
        }

        public ValueTask<RecordingSinkStopResult> StopAsync(
            RecordingSinkStopRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class BlockingStartSink : IRecordingSink
    {
        private readonly RecordingSinkStartResult result;
        private readonly TaskCompletionSource startEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseStart =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int startCallCount;

        public BlockingStartSink(
            RecordingSinkStartResult result)
        {
            this.result = result;
        }

        public Task StartEntered => startEntered.Task;

        public int StartCallCount =>
            Volatile.Read(ref startCallCount);

        public void ReleaseStart()
        {
            releaseStart.TrySetResult();
        }

        public async ValueTask<RecordingSinkStartResult> StartAsync(
            RecordingSinkStartRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            Interlocked.Increment(ref startCallCount);
            startEntered.TrySetResult();

            await releaseStart.Task.WaitAsync(
                cancellationToken);

            return result;
        }

        public ValueTask<RecordingSinkStopResult> StopAsync(
            RecordingSinkStopRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
