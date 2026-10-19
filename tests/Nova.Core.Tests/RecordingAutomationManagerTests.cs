using System.Globalization;
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
    public async Task IdleObservationDoesNotRemainRetainedIndefinitely()
    {
        var sink = new TestRecordingSink();
        using var manager = new RecordingAutomationManager(
            sink,
            maximumChannelCount: 2);
        var ruleSet = new RecordingRuleSet([]);

        var result = await manager.EvaluateAsync(
            CreateCandidate(FirstChannelId),
            ruleSet,
            CancellationToken.None);

        Assert.Equal(RecordingAutomationExecutionResultKind.NoChange, result.Kind);
        Assert.Equal(0, manager.Count);
        Assert.False(manager.TryGetState(FirstChannelId, out _));
    }

    [Fact]
    public async Task SequentialDistinctIdleChannelsDoNotConsumeLifetimeBudget()
    {
        using var manager = new RecordingAutomationManager(
            new TestRecordingSink(),
            maximumChannelCount: 2);
        var ruleSet = new RecordingRuleSet([]);

        for (var value = 3; value <= 6; value++)
        {
            var channelId = CreateChannelId(value);
            var result = await manager.EvaluateAsync(
                CreateCandidate(channelId),
                ruleSet,
                CancellationToken.None);

            Assert.Equal(RecordingAutomationExecutionResultKind.NoChange, result.Kind);
            Assert.Equal(0, manager.Count);
            Assert.False(manager.TryGetState(channelId, out _));
        }
    }

    [Fact]
    public async Task ActiveAutomaticRecordingRemainsRetainedUntilAutomaticStop()
    {
        var sink = new TestRecordingSink();
        using var manager = new RecordingAutomationManager(
            sink,
            maximumChannelCount: 2);
        var recordRuleSet = new RecordingRuleSet(
            [CreateRule(RecordingRuleAction.Record)]);

        var started = await manager.EvaluateAsync(
            CreateCandidate(FirstChannelId),
            recordRuleSet,
            CancellationToken.None);
        var idleObservation = await manager.EvaluateAsync(
            CreateCandidate(SecondChannelId),
            new RecordingRuleSet([]),
            CancellationToken.None);

        Assert.Equal(RecordingAutomationExecutionResultKind.Started, started.Kind);
        Assert.Equal(RecordingAutomationExecutionResultKind.NoChange, idleObservation.Kind);
        Assert.Equal(1, manager.Count);
        Assert.True(manager.TryGetState(FirstChannelId, out var activeState));
        Assert.Equal(RecordingSessionOrigin.AutomaticRule, activeState!.Session!.Origin);

        var stopped = await manager.StopAsync(
            FirstChannelId,
            CancellationToken.None);

        Assert.Equal(RecordingAutomationStopResultKind.Stopped, stopped.Kind);
        Assert.Equal(0, manager.Count);
        Assert.False(manager.TryGetState(FirstChannelId, out _));
    }

    [Fact]
    public async Task StartTransitionPreventsPrematureEviction()
    {
        var sink = new BlockingStartRecordingSink();
        using var manager = new RecordingAutomationManager(sink);
        var evaluation = manager.EvaluateAsync(
                CreateCandidate(FirstChannelId),
                new RecordingRuleSet(
                    [CreateRule(RecordingRuleAction.Record)]),
                CancellationToken.None)
            .AsTask();

        await sink.FirstStartEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, manager.Count);
        Assert.True(manager.TryGetState(FirstChannelId, out var transitioningState));
        Assert.Same(RecordingAutomationExecutionState.Idle, transitioningState);

        sink.ReleaseStarts.TrySetResult();
        var result = await evaluation;

        Assert.Equal(RecordingAutomationExecutionResultKind.Started, result.Kind);
        Assert.Equal(1, manager.Count);
    }

    [Fact]
    public async Task ConcurrentObservationsForSameChannelUseOneCoordinator()
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
        await sink.FirstStartEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var secondTask = manager.EvaluateAsync(
                CreateCandidate(FirstChannelId),
                ruleSet,
                CancellationToken.None)
            .AsTask();

        Assert.False(secondTask.IsCompleted);
        Assert.Equal(1, manager.Count);
        sink.ReleaseStarts.TrySetResult();

        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.Equal(RecordingAutomationExecutionResultKind.Started, results[0].Kind);
        Assert.Equal(RecordingAutomationExecutionResultKind.NoChange, results[1].Kind);
        Assert.Equal(1, sink.StartCallCount);
        Assert.Equal(1, manager.Count);
    }

    [Fact]
    public async Task NewObservationDuringAutomaticStopKeepsCanonicalCoordinator()
    {
        var sink = new BlockingStopRecordingSink();
        using var manager = new RecordingAutomationManager(sink);
        var ruleSet = new RecordingRuleSet(
            [CreateRule(RecordingRuleAction.Record)]);
        _ = await manager.EvaluateAsync(
            CreateCandidate(FirstChannelId),
            ruleSet,
            CancellationToken.None);

        var stopTask = manager.StopAsync(
                FirstChannelId,
                CancellationToken.None)
            .AsTask();
        await sink.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var observationTask = manager.EvaluateAsync(
                CreateCandidate(FirstChannelId),
                ruleSet,
                CancellationToken.None)
            .AsTask();

        Assert.False(observationTask.IsCompleted);
        Assert.Equal(1, manager.Count);
        sink.ReleaseStop.TrySetResult();

        var stopped = await stopTask;
        var restarted = await observationTask;

        Assert.Equal(RecordingAutomationStopResultKind.Stopped, stopped.Kind);
        Assert.Equal(1, sink.StopCallCount);
        Assert.Equal(RecordingAutomationExecutionResultKind.Started, restarted.Kind);
        Assert.Equal(2, sink.StartCallCount);
        Assert.Equal(1, manager.Count);
        Assert.True(manager.TryGetState(FirstChannelId, out var activeState));
        Assert.True(activeState!.AutomationState.IsRecording);

        _ = await manager.StopAsync(FirstChannelId, CancellationToken.None);
        Assert.Equal(2, sink.StopCallCount);
        Assert.Equal(0, manager.Count);
    }

    [Fact]
    public async Task FailedStartCanBecomeEvictableWhenQuiescent()
    {
        using var manager = new RecordingAutomationManager(
            new ThrowingStartRecordingSink());

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await manager.EvaluateAsync(
                CreateCandidate(FirstChannelId),
                new RecordingRuleSet(
                    [CreateRule(RecordingRuleAction.Record)]),
                CancellationToken.None));

        Assert.Equal(0, manager.Count);
        Assert.False(manager.TryGetState(FirstChannelId, out _));
    }

    [Fact]
    public async Task ManualRecordingIsUnaffectedByAutomationCoordinatorEviction()
    {
        var sink = new TestRecordingSink();
        using var manager = new RecordingAutomationManager(sink);
        var candidate = CreateCandidate(FirstChannelId);
        var manualStart = await manager.StartManualAsync(
            candidate,
            CancellationToken.None);

        Assert.Equal(RecordingManualStartResultKind.Started, manualStart.Kind);
        Assert.Equal(1, manager.Count);

        var evaluation = await manager.EvaluateAsync(
            candidate,
            new RecordingRuleSet(
                [CreateRule(RecordingRuleAction.Record)]),
            CancellationToken.None);

        Assert.Equal(RecordingAutomationExecutionResultKind.NoChange, evaluation.Kind);
        Assert.Equal(1, manager.Count);
        Assert.True(manager.TryGetState(FirstChannelId, out var manualState));
        Assert.Equal(RecordingSessionOrigin.Manual, manualState!.Session!.Origin);
        Assert.Equal(manualStart.State.Session!.SessionId, manualState.Session.SessionId);
        Assert.Single(sink.StartRequests);

        var stopped = await manager.StopManualAsync(
            FirstChannelId,
            manualStart.State.Session.SessionId,
            CancellationToken.None);

        Assert.Equal(RecordingAutomationStopResultKind.Stopped, stopped.Kind);
        Assert.Equal(0, manager.Count);
    }

    [Fact]
    public async Task Evaluate_ChannelLimitStillRejectsRelevantStateAndReusesRetiredCapacity()
    {
        var sink = new TestRecordingSink();
        using var manager = new RecordingAutomationManager(
            sink,
            maximumChannelCount: 2);
        var ruleSet = new RecordingRuleSet(
            [CreateRule(RecordingRuleAction.Record)]);

        _ = await manager.EvaluateAsync(
            CreateCandidate(FirstChannelId),
            ruleSet,
            CancellationToken.None);
        _ = await manager.EvaluateAsync(
            CreateCandidate(SecondChannelId),
            ruleSet,
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await manager.EvaluateAsync(
                CreateCandidate(CreateChannelId(3)),
                ruleSet,
                CancellationToken.None));
        Assert.Equal(2, manager.Count);

        var stopped = await manager.StopAsync(
            FirstChannelId,
            CancellationToken.None);
        Assert.Equal(RecordingAutomationStopResultKind.Stopped, stopped.Kind);
        Assert.Equal(1, manager.Count);

        var reused = await manager.EvaluateAsync(
            CreateCandidate(CreateChannelId(3)),
            ruleSet,
            CancellationToken.None);

        Assert.Equal(RecordingAutomationExecutionResultKind.Started, reused.Kind);
        Assert.Equal(2, manager.Count);
    }

    [Fact]
    public async Task DisposeWithInFlightEvaluationCompletesAfterOperation()
    {
        var sink = new BlockingStartRecordingSink();
        var manager = new RecordingAutomationManager(sink);
        var evaluation = manager.EvaluateAsync(
                CreateCandidate(FirstChannelId),
                new RecordingRuleSet(
                    [CreateRule(RecordingRuleAction.Record)]),
                CancellationToken.None)
            .AsTask();
        await sink.FirstStartEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var disposeStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var dispose = Task.Run(() =>
        {
            disposeStarted.TrySetResult();
            manager.Dispose();
        });
        await disposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        sink.ReleaseStarts.TrySetResult();
        var result = await evaluation.WaitAsync(TimeSpan.FromSeconds(5));
        await dispose.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(RecordingAutomationExecutionResultKind.Started, result.Kind);
        Assert.Throws<ObjectDisposedException>(() => _ = manager.Count);
    }

    [Fact]
    public async Task Evaluate_ChannelLimit_IsFailClosed()
    {
        using var manager = new RecordingAutomationManager(
            new TestRecordingSink(),
            maximumChannelCount: 1);
        var recordRuleSet = new RecordingRuleSet(
            [CreateRule(RecordingRuleAction.Record)]);

        _ = await manager.EvaluateAsync(
            CreateCandidate(FirstChannelId),
            recordRuleSet,
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await manager.EvaluateAsync(
                CreateCandidate(SecondChannelId),
                recordRuleSet,
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

    private static ChannelId CreateChannelId(int value)
    {
        return new ChannelId(
            Guid.ParseExact(
                value.ToString("x32", CultureInfo.InvariantCulture),
                "N"));
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

            var session = request.Origin == RecordingSessionOrigin.Manual
                ? RecordingSinkSession.Manual(
                    new RecordingSessionId(Guid.NewGuid()),
                    request.Candidate.ChannelId)
                : new RecordingSinkSession(
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

        public int StartCallCount => Volatile.Read(ref startCount);

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

    private sealed class BlockingStopRecordingSink : IRecordingSink
    {
        private int startCallCount;
        private int stopCallCount;

        public TaskCompletionSource StopEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseStop { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int StartCallCount => Volatile.Read(ref startCallCount);

        public int StopCallCount => Volatile.Read(ref stopCallCount);

        public ValueTask<RecordingSinkStartResult> StartAsync(
            RecordingSinkStartRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref startCallCount);
            return ValueTask.FromResult(RecordingSinkStartResult.Started(
                new RecordingSinkSession(
                    new RecordingSessionId(Guid.NewGuid()),
                    request.Candidate.ChannelId,
                    request.SelectedRuleId)));
        }

        public async ValueTask<RecordingSinkStopResult> StopAsync(
            RecordingSinkStopRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref stopCallCount);
            StopEntered.TrySetResult();
            await ReleaseStop.Task.WaitAsync(cancellationToken);
            return RecordingSinkStopResult.Stopped;
        }
    }

    private sealed class ThrowingStartRecordingSink : IRecordingSink
    {
        public ValueTask<RecordingSinkStartResult> StartAsync(
            RecordingSinkStartRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Synthetic start failure.");
        }

        public ValueTask<RecordingSinkStopResult> StopAsync(
            RecordingSinkStopRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
