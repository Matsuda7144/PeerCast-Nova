using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Core.Tests;

public sealed class RecordingAutomationShutdownTests
{
    private static readonly ChannelId FirstChannelId = new(
        Guid.ParseExact(
            "11111111111111111111111111111111",
            "N"));

    private static readonly ChannelId SecondChannelId = new(
        Guid.ParseExact(
            "22222222222222222222222222222222",
            "N"));

    [Fact]
    public async Task Coordinator_StopAsync_ActiveSessionStopsAndCommitsIdle()
    {
        var sink = new TestRecordingSink();
        using var coordinator = new RecordingAutomationChannelCoordinator(
            FirstChannelId,
            sink);

        _ = await coordinator.EvaluateAsync(
            CreateCandidate(FirstChannelId),
            CreateRecordRuleSet(),
            CancellationToken.None);

        var result = await coordinator.StopAsync(
            CancellationToken.None);

        Assert.Equal(
            RecordingAutomationStopResultKind.Stopped,
            result.Kind);
        Assert.Same(
            RecordingAutomationExecutionState.Idle,
            result.State);
        Assert.Same(
            RecordingAutomationExecutionState.Idle,
            coordinator.State);
        Assert.Single(sink.StopRequests);
    }

    [Fact]
    public async Task Coordinator_StopAsync_RejectedRetainsActiveSessionForRetry()
    {
        var sink = new TestRecordingSink(
            stopResults:
            [
                RecordingSinkStopResult.Rejected,
                RecordingSinkStopResult.Stopped,
            ]);
        using var coordinator = new RecordingAutomationChannelCoordinator(
            FirstChannelId,
            sink);

        _ = await coordinator.EvaluateAsync(
            CreateCandidate(FirstChannelId),
            CreateRecordRuleSet(),
            CancellationToken.None);

        var rejected = await coordinator.StopAsync(
            CancellationToken.None);

        Assert.Equal(
            RecordingAutomationStopResultKind.Rejected,
            rejected.Kind);
        Assert.True(rejected.State.AutomationState.IsRecording);
        Assert.NotNull(rejected.State.Session);

        var retried = await coordinator.StopAsync(
            CancellationToken.None);

        Assert.Equal(
            RecordingAutomationStopResultKind.Stopped,
            retried.Kind);
        Assert.Same(
            RecordingAutomationExecutionState.Idle,
            retried.State);
        Assert.Equal(2, sink.StopRequests.Count);
    }

    [Fact]
    public async Task Coordinator_StopAsync_FaultedSessionCommitsIdle()
    {
        var sink = new TestRecordingSink(
            stopResults:
            [
                RecordingSinkStopResult.Faulted,
            ]);
        using var coordinator = new RecordingAutomationChannelCoordinator(
            FirstChannelId,
            sink);

        _ = await coordinator.EvaluateAsync(
            CreateCandidate(FirstChannelId),
            CreateRecordRuleSet(),
            CancellationToken.None);

        var result = await coordinator.StopAsync(
            CancellationToken.None);

        Assert.Equal(
            RecordingAutomationStopResultKind.Faulted,
            result.Kind);
        Assert.Same(
            RecordingAutomationExecutionState.Idle,
            result.State);
        Assert.Same(
            RecordingAutomationExecutionState.Idle,
            coordinator.State);
    }

    [Fact]
    public async Task Coordinator_StopAsync_IdleDoesNotCallSink()
    {
        var sink = new TestRecordingSink();
        using var coordinator = new RecordingAutomationChannelCoordinator(
            FirstChannelId,
            sink);

        var result = await coordinator.StopAsync(
            CancellationToken.None);

        Assert.Equal(
            RecordingAutomationStopResultKind.NoActiveSession,
            result.Kind);
        Assert.Same(
            RecordingAutomationExecutionState.Idle,
            result.State);
        Assert.Empty(sink.StopRequests);
    }

    [Fact]
    public async Task Manager_ShutdownStopsAllChannelsInDeterministicOrder()
    {
        var sink = new TestRecordingSink();
        using var manager = new RecordingAutomationManager(sink);
        var rules = CreateRecordRuleSet();

        _ = await manager.EvaluateAsync(
            CreateCandidate(SecondChannelId),
            rules,
            CancellationToken.None);
        _ = await manager.EvaluateAsync(
            CreateCandidate(FirstChannelId),
            rules,
            CancellationToken.None);

        var report = await manager.ShutdownAsync(
            CancellationToken.None);

        Assert.True(report.AllInactive);
        Assert.Equal(2, report.Results.Count);
        Assert.Equal(
            FirstChannelId,
            report.Results[0].ChannelId);
        Assert.Equal(
            SecondChannelId,
            report.Results[1].ChannelId);
        Assert.All(
            report.Results,
            result => Assert.Equal(
                RecordingAutomationStopResultKind.Stopped,
                result.Kind));

        Assert.False(manager.AcceptingEvaluations);
    }

    [Fact]
    public async Task Manager_ShutdownRejectedSessionCanBeRetried()
    {
        var sink = new TestRecordingSink(
            stopResults:
            [
                RecordingSinkStopResult.Rejected,
                RecordingSinkStopResult.Stopped,
            ]);
        using var manager = new RecordingAutomationManager(sink);

        _ = await manager.EvaluateAsync(
            CreateCandidate(FirstChannelId),
            CreateRecordRuleSet(),
            CancellationToken.None);

        var first = await manager.ShutdownAsync(
            CancellationToken.None);

        Assert.False(first.AllInactive);
        Assert.Equal(
            RecordingAutomationStopResultKind.Rejected,
            Assert.Single(first.Results).Kind);

        var second = await manager.ShutdownAsync(
            CancellationToken.None);

        Assert.True(second.AllInactive);
        Assert.Equal(
            RecordingAutomationStopResultKind.Stopped,
            Assert.Single(second.Results).Kind);
    }

    [Fact]
    public async Task Manager_ShutdownPreventsNewEvaluations()
    {
        var sink = new TestRecordingSink();
        using var manager = new RecordingAutomationManager(sink);

        _ = await manager.ShutdownAsync(
            CancellationToken.None);

        Assert.False(manager.AcceptingEvaluations);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await manager.EvaluateAsync(
                CreateCandidate(FirstChannelId),
                CreateRecordRuleSet(),
                CancellationToken.None));
    }

    [Fact]
    public async Task Manager_ShutdownPreCancelledPreservesEvaluationAdmission()
    {
        var manager = new RecordingAutomationManager(
            new TestRecordingSink());

        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await manager.ShutdownAsync(
                source.Token));

        Assert.True(manager.AcceptingEvaluations);

        manager.Dispose();
    }

    private static RecordingRuleSet CreateRecordRuleSet()
    {
        return new RecordingRuleSet(
            [
                new RecordingRule(
                    Guid.ParseExact(
                        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                        "N"),
                    "Record",
                    RecordingRuleAction.Record,
                    RecordingRuleCriteria.MatchAll),
            ]);
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

    private sealed class TestRecordingSink : IRecordingSink
    {
        private readonly Queue<RecordingSinkStopResult> stopResults;

        public TestRecordingSink(
            IEnumerable<RecordingSinkStopResult>? stopResults = null)
        {
            this.stopResults = new Queue<RecordingSinkStopResult>(
                stopResults ??
                [RecordingSinkStopResult.Stopped]);
        }

        public List<RecordingSinkStopRequest> StopRequests { get; } = [];

        public ValueTask<RecordingSinkStartResult> StartAsync(
            RecordingSinkStartRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult(
                RecordingSinkStartResult.Started(
                    new RecordingSinkSession(
                        new RecordingSessionId(Guid.NewGuid()),
                        request.Candidate.ChannelId,
                        request.SelectedRuleId)));
        }

        public ValueTask<RecordingSinkStopResult> StopAsync(
            RecordingSinkStopRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopRequests.Add(request);

            return ValueTask.FromResult(
                stopResults.Count == 0
                    ? RecordingSinkStopResult.Stopped
                    : stopResults.Dequeue());
        }
    }
}
