using System.Collections.ObjectModel;
using System.Globalization;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Core.Tests;

public sealed class RecordingActiveSessionSnapshotTests
{
    private static readonly ChannelId FirstChannelId = CreateChannelId(1);
    private static readonly ChannelId SecondChannelId = CreateChannelId(2);

    [Fact]
    public async Task SnapshotDoesNotCreateCoordinatorForUnknownChannel()
    {
        using var manager = new RecordingAutomationManager(
            new MonitorSink(),
            maximumChannelCount: 1);

        Assert.Empty(await manager.GetActiveRecordingSessionsAsync(
            CancellationToken.None));
        Assert.Empty(await manager.GetActiveRecordingSessionsAsync(
            CancellationToken.None));
        Assert.Equal(0, manager.Count);

        var started = await manager.StartManualAsync(
            CreateCandidate(FirstChannelId),
            CancellationToken.None);

        Assert.Equal(RecordingManualStartResultKind.Started, started.Kind);
        Assert.Equal(1, manager.Count);
        Assert.Equal(FirstChannelId, Assert.Single(
            await manager.GetActiveRecordingSessionsAsync(
                CancellationToken.None)).ChannelId);

        _ = await manager.StopManualAsync(
            FirstChannelId,
            started.State.Session!.SessionId,
            CancellationToken.None);
    }

    [Fact]
    public async Task ActiveManualSessionAppearsInSnapshot()
    {
        var sink = new MonitorSink();
        using var manager = new RecordingAutomationManager(sink);
        var started = await manager.StartManualAsync(
            CreateCandidate(FirstChannelId),
            CancellationToken.None);

        var session = Assert.Single(await manager.GetActiveRecordingSessionsAsync(
            CancellationToken.None));

        Assert.Equal(FirstChannelId, session.ChannelId);
        Assert.Equal(started.State.Session!.SessionId, session.SessionId);
        Assert.Equal(RecordingSessionOrigin.Manual, session.Origin);
        Assert.Null(session.AutomaticRuleId);

        _ = await manager.StopManualAsync(
            FirstChannelId,
            session.SessionId,
            CancellationToken.None);
    }

    [Fact]
    public async Task ActiveAutomaticSessionAppearsInSnapshot()
    {
        var sink = new MonitorSink();
        using var manager = new RecordingAutomationManager(sink);
        var rule = CreateRule(RecordingRuleAction.Record, 101);
        var started = await manager.EvaluateAsync(
            CreateCandidate(FirstChannelId),
            new RecordingRuleSet([rule]),
            CancellationToken.None);

        var session = Assert.Single(await manager.GetActiveRecordingSessionsAsync(
            CancellationToken.None));

        Assert.Equal(FirstChannelId, session.ChannelId);
        Assert.Equal(started.State.Session!.SessionId, session.SessionId);
        Assert.Equal(RecordingSessionOrigin.AutomaticRule, session.Origin);
        Assert.Equal(rule.Id, session.AutomaticRuleId);

        _ = await manager.StopAsync(FirstChannelId, CancellationToken.None);
    }

    [Fact]
    public async Task ManualAndAutomaticSessionsAppearTogether()
    {
        using var manager = new RecordingAutomationManager(new MonitorSink());
        var manual = await manager.StartManualAsync(
            CreateCandidate(FirstChannelId),
            CancellationToken.None);
        var rule = CreateRule(RecordingRuleAction.Record, 102);
        var automatic = await manager.EvaluateAsync(
            CreateCandidate(SecondChannelId),
            new RecordingRuleSet([rule]),
            CancellationToken.None);

        var sessions = await manager.GetActiveRecordingSessionsAsync(
            CancellationToken.None);

        Assert.Equal(2, sessions.Count);
        var manualSnapshot = Assert.Single(
            sessions,
            session => session.ChannelId == FirstChannelId);
        var automaticSnapshot = Assert.Single(
            sessions,
            session => session.ChannelId == SecondChannelId);
        Assert.Equal(manual.State.Session!.SessionId, manualSnapshot.SessionId);
        Assert.Equal(RecordingSessionOrigin.Manual, manualSnapshot.Origin);
        Assert.Equal(automatic.State.Session!.SessionId, automaticSnapshot.SessionId);
        Assert.Equal(RecordingSessionOrigin.AutomaticRule, automaticSnapshot.Origin);
        Assert.Equal(rule.Id, automaticSnapshot.AutomaticRuleId);

        _ = await manager.StopManualAsync(
            FirstChannelId,
            manualSnapshot.SessionId,
            CancellationToken.None);
        _ = await manager.StopAsync(SecondChannelId, CancellationToken.None);
    }

    [Fact]
    public async Task SnapshotIsIndependentOfCatalogMembership()
    {
        using var manager = new RecordingAutomationManager(new MonitorSink());
        var started = await manager.StartManualAsync(
            CreateCandidate(FirstChannelId),
            CancellationToken.None);

        // This Core-only test supplies no catalog, YP, or GUI state.
        var session = Assert.Single(await manager.GetActiveRecordingSessionsAsync(
            CancellationToken.None));

        Assert.Equal(started.State.Session, session);
        _ = await manager.StopManualAsync(
            FirstChannelId,
            session.SessionId,
            CancellationToken.None);
    }

    [Fact]
    public async Task ManualStopRemovesSessionFromSubsequentSnapshot()
    {
        using var manager = new RecordingAutomationManager(new MonitorSink());
        var started = await manager.StartManualAsync(
            CreateCandidate(FirstChannelId),
            CancellationToken.None);
        var sessionId = started.State.Session!.SessionId;

        var stopped = await manager.StopManualAsync(
            FirstChannelId,
            sessionId,
            CancellationToken.None);

        Assert.Equal(RecordingAutomationStopResultKind.Stopped, stopped.Kind);
        Assert.Empty(await manager.GetActiveRecordingSessionsAsync(
            CancellationToken.None));
        Assert.Equal(0, manager.Count);
    }

    [Fact]
    public async Task AutomaticStopRemovesSessionFromSubsequentSnapshot()
    {
        using var manager = new RecordingAutomationManager(new MonitorSink());
        var started = await manager.EvaluateAsync(
            CreateCandidate(FirstChannelId),
            new RecordingRuleSet([CreateRule(RecordingRuleAction.Record, 103)]),
            CancellationToken.None);

        var stopped = await manager.EvaluateAsync(
            CreateCandidate(FirstChannelId),
            new RecordingRuleSet([CreateRule(RecordingRuleAction.Ignore, 104)]),
            CancellationToken.None);

        Assert.Equal(RecordingAutomationExecutionResultKind.Stopped, stopped.Kind);
        Assert.NotNull(started.State.Session);
        Assert.Empty(await manager.GetActiveRecordingSessionsAsync(
            CancellationToken.None));
        Assert.Equal(0, manager.Count);
    }

    [Fact]
    public async Task TerminalManualSessionIsReconciledOut()
    {
        var sink = new MonitorSink();
        using var manager = new RecordingAutomationManager(sink);
        var started = await manager.StartManualAsync(
            CreateCandidate(FirstChannelId),
            CancellationToken.None);
        sink.MarkTerminal(started.State.Session!);

        var sessions = await manager.GetActiveRecordingSessionsAsync(
            CancellationToken.None);

        Assert.Empty(sessions);
        Assert.Equal(1, sink.StopCallCount);
        Assert.Equal(0, manager.Count);
        Assert.False(manager.TryGetState(FirstChannelId, out _));
    }

    [Fact]
    public async Task TerminalAutomaticSessionIsReconciledOut()
    {
        var sink = new MonitorSink();
        using var manager = new RecordingAutomationManager(sink);
        var started = await manager.EvaluateAsync(
            CreateCandidate(FirstChannelId),
            new RecordingRuleSet([CreateRule(RecordingRuleAction.Record, 105)]),
            CancellationToken.None);
        sink.MarkTerminal(started.State.Session!);

        var sessions = await manager.GetActiveRecordingSessionsAsync(
            CancellationToken.None);

        Assert.Empty(sessions);
        Assert.Equal(1, sink.StopCallCount);
        Assert.Equal(0, manager.Count);
        Assert.False(manager.TryGetState(FirstChannelId, out _));
    }

    [Fact]
    public async Task SnapshotReconciliationDoesNotCreateReplacementCoordinator()
    {
        var sink = new MonitorSink();
        using var manager = new RecordingAutomationManager(
            sink,
            maximumChannelCount: 1);
        var started = await manager.StartManualAsync(
            CreateCandidate(FirstChannelId),
            CancellationToken.None);
        sink.MarkTerminal(started.State.Session!);

        Assert.Empty(await manager.GetActiveRecordingSessionsAsync(
            CancellationToken.None));
        Assert.Equal(0, manager.Count);
        Assert.False(manager.TryGetState(FirstChannelId, out _));
        Assert.Empty(await manager.GetActiveRecordingSessionsAsync(
            CancellationToken.None));
        Assert.Equal(0, manager.Count);

        var replacement = await manager.StartManualAsync(
            CreateCandidate(FirstChannelId),
            CancellationToken.None);
        Assert.NotEqual(
            started.State.Session!.SessionId,
            replacement.State.Session!.SessionId);
        _ = await manager.StopManualAsync(
            FirstChannelId,
            replacement.State.Session.SessionId,
            CancellationToken.None);
    }

    [Fact]
    public async Task RepeatedSnapshotsDoNotPreventIdleEviction()
    {
        using var manager = new RecordingAutomationManager(new MonitorSink());
        var started = await manager.StartManualAsync(
            CreateCandidate(FirstChannelId),
            CancellationToken.None);

        for (var index = 0; index < 4; index++)
        {
            Assert.Equal(
                started.State.Session,
                Assert.Single(await manager.GetActiveRecordingSessionsAsync(
                    CancellationToken.None)));
            Assert.Equal(1, manager.Count);
        }

        _ = await manager.StopManualAsync(
            FirstChannelId,
            started.State.Session!.SessionId,
            CancellationToken.None);

        for (var index = 0; index < 4; index++)
        {
            Assert.Empty(await manager.GetActiveRecordingSessionsAsync(
                CancellationToken.None));
            Assert.Equal(0, manager.Count);
        }
    }

    [Fact]
    public async Task SequentialIdleChannelsStillReuseCapacity()
    {
        using var manager = new RecordingAutomationManager(
            new MonitorSink(),
            maximumChannelCount: 1);

        for (var index = 1; index <= 6; index++)
        {
            var result = await manager.EvaluateAsync(
                CreateCandidate(CreateChannelId(index)),
                new RecordingRuleSet([]),
                CancellationToken.None);

            Assert.Equal(RecordingAutomationExecutionResultKind.NoChange, result.Kind);
            Assert.Empty(await manager.GetActiveRecordingSessionsAsync(
                CancellationToken.None));
            Assert.Equal(0, manager.Count);
        }
    }

    [Fact]
    public async Task SnapshotDuringActiveStartReturnsOneCoherentIdentity()
    {
        var sink = new BlockingStartSink();
        using var manager = new RecordingAutomationManager(sink);
        var startTask = manager.StartManualAsync(
                CreateCandidate(FirstChannelId),
                CancellationToken.None)
            .AsTask();
        await sink.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var snapshotTask = manager.GetActiveRecordingSessionsAsync(
                CancellationToken.None)
            .AsTask();
        Assert.False(snapshotTask.IsCompleted);

        sink.ReleaseStart.TrySetResult();
        var started = await startTask.WaitAsync(TimeSpan.FromSeconds(5));
        var sessions = await snapshotTask.WaitAsync(TimeSpan.FromSeconds(5));

        var session = Assert.Single(sessions);
        Assert.Equal(started.State.Session, session);
        Assert.Equal(FirstChannelId, session.ChannelId);
        Assert.Equal(RecordingSessionOrigin.Manual, session.Origin);
    }

    [Fact]
    public async Task SnapshotDuringStopReturnsOnlyCoherentGeneration()
    {
        var sink = new BlockingStopSink();
        using var manager = new RecordingAutomationManager(sink);
        var started = await manager.StartManualAsync(
            CreateCandidate(FirstChannelId),
            CancellationToken.None);

        var stopTask = manager.StopManualAsync(
                FirstChannelId,
                started.State.Session!.SessionId,
                CancellationToken.None)
            .AsTask();
        await sink.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var snapshotTask = manager.GetActiveRecordingSessionsAsync(
                CancellationToken.None)
            .AsTask();
        Assert.False(snapshotTask.IsCompleted);

        sink.ReleaseStop.TrySetResult();
        var stopped = await stopTask.WaitAsync(TimeSpan.FromSeconds(5));
        var sessions = await snapshotTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(RecordingAutomationStopResultKind.Stopped, stopped.Kind);
        Assert.Empty(sessions);
        Assert.Equal(0, manager.Count);
    }

    [Fact]
    public async Task ReplacementSessionGenerationIsNotCollapsedDuringSnapshot()
    {
        var sink = new BlockingStopSink();
        using var manager = new RecordingAutomationManager(sink);
        var firstRule = CreateRule(RecordingRuleAction.Record, 106);
        var first = await manager.EvaluateAsync(
            CreateCandidate(FirstChannelId),
            new RecordingRuleSet([firstRule]),
            CancellationToken.None);

        var stopTask = manager.StopAsync(
                FirstChannelId,
                CancellationToken.None)
            .AsTask();
        await sink.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var snapshotTask = manager.GetActiveRecordingSessionsAsync(
                CancellationToken.None)
            .AsTask();
        var secondRule = CreateRule(RecordingRuleAction.Record, 107);
        var replacementTask = manager.EvaluateAsync(
                CreateCandidate(FirstChannelId),
                new RecordingRuleSet([secondRule]),
                CancellationToken.None)
            .AsTask();

        sink.ReleaseStop.TrySetResult();
        _ = await stopTask.WaitAsync(TimeSpan.FromSeconds(5));
        var replacement = await replacementTask.WaitAsync(TimeSpan.FromSeconds(5));
        var snapshotDuringReplacement = await snapshotTask.WaitAsync(
            TimeSpan.FromSeconds(5));

        var replacementSession = replacement.State.Session!;
        Assert.NotEqual(first.State.Session!.SessionId, replacementSession.SessionId);
        Assert.Equal(secondRule.Id, replacementSession.AutomaticRuleId);
        Assert.All(snapshotDuringReplacement, session =>
        {
            Assert.Equal(replacementSession.SessionId, session.SessionId);
            Assert.Equal(replacementSession.Origin, session.Origin);
            Assert.Equal(replacementSession.AutomaticRuleId, session.AutomaticRuleId);
        });

        var finalSnapshot = Assert.Single(
            await manager.GetActiveRecordingSessionsAsync(
                CancellationToken.None));
        Assert.Equal(replacementSession, finalSnapshot);
        _ = await manager.StopAsync(FirstChannelId, CancellationToken.None);
    }

    [Fact]
    public async Task SnapshotDuringShutdownCompletesWithoutDeadlock()
    {
        var sink = new BlockingStopSink();
        using var manager = new RecordingAutomationManager(sink);
        _ = await manager.StartManualAsync(
            CreateCandidate(FirstChannelId),
            CancellationToken.None);

        var shutdownTask = manager.ShutdownAsync(CancellationToken.None).AsTask();
        await sink.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var snapshotTask = manager.GetActiveRecordingSessionsAsync(
                CancellationToken.None)
            .AsTask();

        sink.ReleaseStop.TrySetResult();
        await shutdownTask.WaitAsync(TimeSpan.FromSeconds(5));
        var sessions = await snapshotTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(sessions);
    }

    [Fact]
    public async Task SnapshotDuringManagerDisposeCompletesWithoutDeadlock()
    {
        var sink = new MonitorSink { BlockStatusRead = true };
        var manager = new RecordingAutomationManager(sink);
        _ = await manager.StartManualAsync(
            CreateCandidate(FirstChannelId),
            CancellationToken.None);

        var snapshotTask = Task.Run(async () =>
            await manager.GetActiveRecordingSessionsAsync(
                CancellationToken.None));
        await sink.StatusReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var disposeStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var disposeTask = Task.Run(() =>
        {
            disposeStarted.TrySetResult();
            manager.Dispose();
        });
        await disposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        sink.ReleaseStatusRead.TrySetResult();
        var sessions = await snapshotTask.WaitAsync(TimeSpan.FromSeconds(5));
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(sessions);
        Assert.Equal(FirstChannelId, sessions[0].ChannelId);
    }

    [Fact]
    public async Task ChannelLimitRemainsEnforcedAcrossSnapshotsAndReusesCapacity()
    {
        using var manager = new RecordingAutomationManager(
            new MonitorSink(),
            maximumChannelCount: 1);
        var first = await manager.StartManualAsync(
            CreateCandidate(FirstChannelId),
            CancellationToken.None);

        Assert.Equal(FirstChannelId, Assert.Single(
            await manager.GetActiveRecordingSessionsAsync(
                CancellationToken.None)).ChannelId);
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await manager.StartManualAsync(
                CreateCandidate(SecondChannelId),
                CancellationToken.None));
        Assert.Equal(1, manager.Count);

        _ = await manager.StopManualAsync(
            FirstChannelId,
            first.State.Session!.SessionId,
            CancellationToken.None);
        Assert.Empty(await manager.GetActiveRecordingSessionsAsync(
            CancellationToken.None));

        var second = await manager.StartManualAsync(
            CreateCandidate(SecondChannelId),
            CancellationToken.None);
        Assert.Equal(RecordingManualStartResultKind.Started, second.Kind);
        _ = await manager.StopManualAsync(
            SecondChannelId,
            second.State.Session!.SessionId,
            CancellationToken.None);
    }

    [Fact]
    public async Task SnapshotCollectionIsReadOnly()
    {
        using var manager = new RecordingAutomationManager(new MonitorSink());
        var started = await manager.StartManualAsync(
            CreateCandidate(FirstChannelId),
            CancellationToken.None);
        var sessions = await manager.GetActiveRecordingSessionsAsync(
            CancellationToken.None);

        Assert.IsType<ReadOnlyCollection<RecordingSinkSession>>(sessions);
        var mutable = Assert.IsAssignableFrom<IList<RecordingSinkSession>>(sessions);
        Assert.Throws<NotSupportedException>(
            () => mutable[0] = started.State.Session!);

        _ = await manager.StopManualAsync(
            FirstChannelId,
            started.State.Session!.SessionId,
            CancellationToken.None);
    }

    [Fact]
    public async Task ReconciliationFailureFailsSnapshotAndReleasesOperationLease()
    {
        var sink = new MonitorSink { ThrowOnStatusRead = true };
        using var manager = new RecordingAutomationManager(sink);
        var started = await manager.StartManualAsync(
            CreateCandidate(FirstChannelId),
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await manager.GetActiveRecordingSessionsAsync(
                CancellationToken.None));

        var stopped = await manager.StopManualAsync(
            FirstChannelId,
            started.State.Session!.SessionId,
            CancellationToken.None);
        Assert.Equal(RecordingAutomationStopResultKind.Stopped, stopped.Kind);
        Assert.Equal(0, manager.Count);
    }

    [Fact]
    public async Task SnapshotAfterManagerDisposeUsesExistingLifecycleException()
    {
        var manager = new RecordingAutomationManager(new MonitorSink());
        manager.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await manager.GetActiveRecordingSessionsAsync(
                CancellationToken.None));
    }

    [Fact]
    public async Task CanceledSnapshotReleasesNoNewCoordinatorLease()
    {
        using var manager = new RecordingAutomationManager(new MonitorSink());
        var started = await manager.StartManualAsync(
            CreateCandidate(FirstChannelId),
            CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await manager.GetActiveRecordingSessionsAsync(
                cancellation.Token));

        Assert.Equal(1, manager.Count);
        var stopped = await manager.StopManualAsync(
            FirstChannelId,
            started.State.Session!.SessionId,
            CancellationToken.None);
        Assert.Equal(RecordingAutomationStopResultKind.Stopped, stopped.Kind);
        Assert.Equal(0, manager.Count);
    }

    private static RecordingCandidate CreateCandidate(ChannelId channelId)
    {
        return new RecordingCandidate(
            channelId,
            new ChannelMetadataSnapshot(
                name: "Channel",
                genre: "Games",
                bitrateKbps: 2000));
    }

    private static RecordingRule CreateRule(
        RecordingRuleAction action,
        int id)
    {
        return new RecordingRule(
            Guid.ParseExact(
                id.ToString("x32", CultureInfo.InvariantCulture),
                "N"),
            $"Rule {id}",
            action,
            RecordingRuleCriteria.MatchAll);
    }

    private static ChannelId CreateChannelId(int value)
    {
        return new ChannelId(
            Guid.ParseExact(
                value.ToString("x32", CultureInfo.InvariantCulture),
                "N"));
    }

    private sealed class MonitorSink :
        IRecordingSink,
        IRecordingSinkSessionMonitor
    {
        private readonly object gate = new();
        private readonly Dictionary<RecordingSessionId, RecordingSinkSessionLifecycleKind>
            statuses = [];

        public bool ThrowOnStatusRead { get; init; }

        public bool BlockStatusRead { get; init; }

        public TaskCompletionSource StatusReadEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseStatusRead { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int StopCallCount { get; private set; }

        public ValueTask<RecordingSinkStartResult> StartAsync(
            RecordingSinkStartRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = request.Origin == RecordingSessionOrigin.Manual
                ? RecordingSinkSession.Manual(
                    new RecordingSessionId(Guid.NewGuid()),
                    request.Candidate.ChannelId)
                : new RecordingSinkSession(
                    new RecordingSessionId(Guid.NewGuid()),
                    request.Candidate.ChannelId,
                    request.SelectedRuleId);

            lock (gate)
            {
                statuses[session.SessionId] =
                    RecordingSinkSessionLifecycleKind.Active;
            }

            return ValueTask.FromResult(RecordingSinkStartResult.Started(session));
        }

        public ValueTask<RecordingSinkStopResult> StopAsync(
            RecordingSinkStopRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                StopCallCount++;
                _ = statuses.Remove(request.Session.SessionId);
            }

            return ValueTask.FromResult(RecordingSinkStopResult.Stopped);
        }

        public bool TryGetSessionStatus(
            RecordingSinkSession session,
            out RecordingSinkSessionLifecycleStatus? status)
        {
            if (BlockStatusRead)
            {
                StatusReadEntered.TrySetResult();
                if (!ReleaseStatusRead.Task.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("The test did not release the status read.");
                }
            }

            if (ThrowOnStatusRead)
            {
                throw new InvalidOperationException("Synthetic status failure.");
            }

            lock (gate)
            {
                if (!statuses.TryGetValue(session.SessionId, out var kind))
                {
                    status = null;
                    return false;
                }

                status = new RecordingSinkSessionLifecycleStatus(session, kind);
                return true;
            }
        }

        public void MarkTerminal(RecordingSinkSession session)
        {
            lock (gate)
            {
                statuses[session.SessionId] =
                    RecordingSinkSessionLifecycleKind.Completed;
            }
        }
    }

    private sealed class BlockingStartSink : IRecordingSink
    {
        public TaskCompletionSource StartEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseStart { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<RecordingSinkStartResult> StartAsync(
            RecordingSinkStartRequest request,
            CancellationToken cancellationToken)
        {
            StartEntered.TrySetResult();
            await ReleaseStart.Task.WaitAsync(cancellationToken);
            var session = request.Origin == RecordingSessionOrigin.Manual
                ? RecordingSinkSession.Manual(
                    new RecordingSessionId(Guid.NewGuid()),
                    request.Candidate.ChannelId)
                : new RecordingSinkSession(
                    new RecordingSessionId(Guid.NewGuid()),
                    request.Candidate.ChannelId,
                    request.SelectedRuleId);
            return RecordingSinkStartResult.Started(session);
        }

        public ValueTask<RecordingSinkStopResult> StopAsync(
            RecordingSinkStopRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(RecordingSinkStopResult.Stopped);
        }
    }

    private sealed class BlockingStopSink : IRecordingSink
    {
        public TaskCompletionSource StopEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseStop { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<RecordingSinkStartResult> StartAsync(
            RecordingSinkStartRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = request.Origin == RecordingSessionOrigin.Manual
                ? RecordingSinkSession.Manual(
                    new RecordingSessionId(Guid.NewGuid()),
                    request.Candidate.ChannelId)
                : new RecordingSinkSession(
                    new RecordingSessionId(Guid.NewGuid()),
                    request.Candidate.ChannelId,
                    request.SelectedRuleId);
            return ValueTask.FromResult(RecordingSinkStartResult.Started(session));
        }

        public async ValueTask<RecordingSinkStopResult> StopAsync(
            RecordingSinkStopRequest request,
            CancellationToken cancellationToken)
        {
            StopEntered.TrySetResult();
            await ReleaseStop.Task.WaitAsync(cancellationToken);
            return RecordingSinkStopResult.Stopped;
        }
    }
}
