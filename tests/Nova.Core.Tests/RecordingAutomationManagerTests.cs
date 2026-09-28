using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Core.Tests;

public sealed class RecordingAutomationManagerTests
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
    public async Task Evaluate_ReusesCoordinatorForSameChannel()
    {
        var sink = new TestRecordingSink();
        using var manager = new RecordingAutomationManager(sink);
        var rule = CreateRule(RecordingRuleAction.Record);
        var ruleSet = new RecordingRuleSet([rule]);

        var first = await manager.EvaluateAsync(
            CreateCandidate(FirstChannelId),
            ruleSet,
            CancellationToken.None);

        var second = await manager.EvaluateAsync(
            CreateCandidate(FirstChannelId),
            ruleSet,
            CancellationToken.None);

        Assert.Equal(
            RecordingAutomationExecutionResultKind.Started,
            first.Kind);
        Assert.Equal(
            RecordingAutomationExecutionResultKind.NoChange,
            second.Kind);
        Assert.Equal(1, manager.Count);
        Assert.Single(sink.StartRequests);
    }

    [Fact]
    public async Task Evaluate_DifferentChannelsOwnIndependentSessions()
    {
        var sink = new TestRecordingSink();
        using var manager = new RecordingAutomationManager(sink);
        var ruleSet = new RecordingRuleSet(
            [CreateRule(RecordingRuleAction.Record)]);

        var first = await manager.EvaluateAsync(
            CreateCandidate(FirstChannelId),
            ruleSet,
            CancellationToken.None);

        var second = await manager.EvaluateAsync(
            CreateCandidate(SecondChannelId),
            ruleSet,
            CancellationToken.None);

        Assert.Equal(
            RecordingAutomationExecutionResultKind.Started,
            first.Kind);
        Assert.Equal(
            RecordingAutomationExecutionResultKind.Started,
            second.Kind);
        Assert.Equal(2, manager.Count);
        Assert.Equal(2, sink.StartRequests.Count);

        Assert.True(manager.TryGetState(
            FirstChannelId,
            out var firstState));
        Assert.True(manager.TryGetState(
            SecondChannelId,
            out var secondState));

        Assert.NotNull(firstState);
        Assert.NotNull(secondState);
        Assert.NotEqual(
            firstState.Session!.SessionId,
            secondState.Session!.SessionId);
    }

    [Fact]
    public async Task Evaluate_DifferentChannelsAreNotGloballySerialized()
    {
        var sink = new BlockingStartRecordingSink();
        using var manager = new RecordingAutomationManager(sink);
        var ruleSet = new RecordingRuleSet(
            [CreateRule(RecordingRuleAction.Record)]);

        var firstTask = manager.EvaluateAsync(
                CreateCandidate(FirstChannelId),
                ruleSet,
                CancellationToken.None)
            .AsTask();

        await sink.FirstStartEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        var secondTask = manager.EvaluateAsync(
                CreateCandidate(SecondChannelId),
                ruleSet,
                CancellationToken.None)
            .AsTask();

        await sink.SecondStartEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        Assert.False(firstTask.IsCompleted);
        Assert.False(secondTask.IsCompleted);

        sink.ReleaseStarts.TrySetResult();

        await Task.WhenAll(firstTask, secondTask);

        Assert.Equal(2, manager.Count);
    }

    [Fact]
    public async Task Evaluate_ChannelLimit_IsFailClosed()
    {
        var sink = new TestRecordingSink();
        using var manager = new RecordingAutomationManager(
            sink,
            maximumChannelCount: 1);
        var ruleSet = new RecordingRuleSet([]);

        _ = await manager.EvaluateAsync(
            CreateCandidate(FirstChannelId),
            ruleSet,
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await manager.EvaluateAsync(
                CreateCandidate(SecondChannelId),
                ruleSet,
                CancellationToken.None));

        Assert.Equal(1, manager.Count);
    }

    [Fact]
    public void TryGetState_UnknownChannel_ReturnsFalse()
    {
        using var manager = new RecordingAutomationManager(
            new TestRecordingSink());

        Assert.False(manager.TryGetState(
            FirstChannelId,
            out var state));
        Assert.Null(state);
    }

    [Fact]
    public async Task Dispose_PreventsNewEvaluation()
    {
        var manager = new RecordingAutomationManager(
            new TestRecordingSink());

        manager.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await manager.EvaluateAsync(
                CreateCandidate(FirstChannelId),
                new RecordingRuleSet([]),
                CancellationToken.None));

        Assert.Throws<ObjectDisposedException>(
            () => _ = manager.Count);
    }

    [Fact]
    public void Constructor_InvalidMaximumChannelCount_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RecordingAutomationManager(
                new TestRecordingSink(),
                maximumChannelCount: 0));
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

    private sealed class TestRecordingSink : IRecordingSink
    {
        private readonly object gate = new();

        public List<RecordingSinkStartRequest> StartRequests { get; } = [];

        public ValueTask<RecordingSinkStartResult> StartAsync(
            RecordingSinkStartRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (gate)
            {
                StartRequests.Add(request);
            }

            var session = new RecordingSinkSession(
                new RecordingSessionId(Guid.NewGuid()),
                request.Candidate.ChannelId,
                request.SelectedRuleId);

            return ValueTask.FromResult(
                RecordingSinkStartResult.Started(session));
        }

        public ValueTask<RecordingSinkStopResult> StopAsync(
            RecordingSinkStopRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                RecordingSinkStopResult.Stopped);
        }
    }

    private sealed class BlockingStartRecordingSink : IRecordingSink
    {
        private int startCount;

        public TaskCompletionSource FirstStartEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SecondStartEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseStarts { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<RecordingSinkStartResult> StartAsync(
            RecordingSinkStartRequest request,
            CancellationToken cancellationToken)
        {
            var count = Interlocked.Increment(ref startCount);

            if (count == 1)
            {
                FirstStartEntered.TrySetResult();
            }
            else if (count == 2)
            {
                SecondStartEntered.TrySetResult();
            }

            await ReleaseStarts.Task.WaitAsync(
                cancellationToken);

            return RecordingSinkStartResult.Started(
                new RecordingSinkSession(
                    new RecordingSessionId(Guid.NewGuid()),
                    request.Candidate.ChannelId,
                    request.SelectedRuleId));
        }

        public ValueTask<RecordingSinkStopResult> StopAsync(
            RecordingSinkStopRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                RecordingSinkStopResult.Stopped);
        }
    }
}
