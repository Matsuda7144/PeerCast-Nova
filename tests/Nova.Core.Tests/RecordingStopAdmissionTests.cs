using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Core.Tests;

public sealed class RecordingStopAdmissionTests
{
    private static readonly ChannelId Channel = new(Guid.NewGuid());
    private static readonly Guid RuleA = Guid.NewGuid();
    private static readonly Guid RuleB = Guid.NewGuid();

    [Fact]
    public async Task StopBeforeAdmissionCancellationHasNoEffect()
    {
        var sink = new AdmissionSink();
        using var coordinator = Coordinator(sink);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var result = await coordinator.RequestAutomaticStopAsync(sink.Session.SessionId, cancelled.Token);
        Assert.Equal(RecordingAutomaticStopNotAdmittedReason.CancelledBeforeAdmission, result.Reason);
        Assert.Null(result.Admission);
        Assert.Equal(0, sink.AdmissionCalls);
    }

    [Fact]
    public async Task StopAdmissionProducesExactSessionReceipt()
    {
        var sink = new AdmissionSink();
        using var coordinator = Coordinator(sink);
        var result = await coordinator.RequestAutomaticStopAsync(sink.Session.SessionId);
        var receipt = Assert.IsType<RecordingAutomaticStopAdmission>(result.Admission);
        Assert.True(result.IsNew);
        Assert.Equal(Channel, receipt.ChannelId);
        Assert.Equal(sink.Session.SessionId, receipt.SessionId);
        Assert.Equal(RuleA, receipt.ActiveRuleId);
        Assert.False(receipt.Completion.IsCompleted);
        Assert.True(coordinator.TryGetAutomaticStopAdmission(receipt.SessionId, out var recovered));
        Assert.Same(receipt, recovered);
        sink.Finish(RecordingSinkStopResult.Stopped);
        Assert.Equal(RecordingAutomationStopResultKind.Stopped, (await receipt.Completion).Kind);
        Assert.Null(coordinator.State.Session);
    }

    [Fact]
    public async Task CancellationAfterAdmissionDoesNotEraseAdmission()
    {
        var sink = new AdmissionSink();
        using var coordinator = Coordinator(sink);
        using var cancelled = new CancellationTokenSource();
        var receipt = (await coordinator.RequestAutomaticStopAsync(sink.Session.SessionId, cancelled.Token)).Admission!;
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receipt.Completion.WaitAsync(cancelled.Token));
        Assert.False(receipt.Completion.IsCompleted);
        Assert.True(coordinator.TryGetAutomaticStopAdmission(receipt.SessionId, out var recovered));
        Assert.Same(receipt, recovered);
        sink.Finish(RecordingSinkStopResult.Stopped);
        await receipt.Completion;
    }

    [Theory]
    [InlineData(RecordingSinkStopResultKind.Stopped, RecordingAutomationStopResultKind.Stopped)]
    [InlineData(RecordingSinkStopResultKind.NoDecodableVideoStart, RecordingAutomationStopResultKind.NoDecodableVideoStart)]
    [InlineData(RecordingSinkStopResultKind.Faulted, RecordingAutomationStopResultKind.Faulted)]
    public async Task AdmittedPendingCompletionResolvesExactTerminal(
        RecordingSinkStopResultKind sinkKind,
        RecordingAutomationStopResultKind expected)
    {
        var sink = new AdmissionSink();
        using var coordinator = Coordinator(sink);
        var receipt = (await coordinator.RequestAutomaticStopAsync(sink.Session.SessionId)).Admission!;
        sink.Finish(sinkKind switch
        {
            RecordingSinkStopResultKind.Stopped => RecordingSinkStopResult.Stopped,
            RecordingSinkStopResultKind.NoDecodableVideoStart => RecordingSinkStopResult.NoDecodableVideoStart,
            _ => RecordingSinkStopResult.Faulted,
        });
        var terminal = await receipt.Completion;
        Assert.Equal(expected, terminal.Kind);
        Assert.Null(terminal.State.Session);
        Assert.Null(coordinator.State.Session);
    }

    [Fact]
    public async Task NaturalTerminalBeforeAdmissionDoesNotClaimUserStop()
    {
        var sink = new AdmissionSink { Terminal = true };
        using var coordinator = Coordinator(sink);
        var result = await coordinator.RequestAutomaticStopAsync(sink.Session.SessionId);
        Assert.Equal(RecordingAutomaticStopNotAdmittedReason.AlreadyTerminal, result.Reason);
        Assert.Null(result.Admission);
        Assert.False(sink.TryGetStopAdmission(sink.Session, out _));
    }

    [Fact]
    public async Task TerminalRaceAfterAdmissionRetainsUserStopCausality()
    {
        var sink = new AdmissionSink { CompleteInsideAdmission = true };
        using var coordinator = Coordinator(sink);
        var receipt = (await coordinator.RequestAutomaticStopAsync(sink.Session.SessionId)).Admission!;
        Assert.Equal(RecordingAutomationStopResultKind.Stopped, (await receipt.Completion).Kind);
        Assert.True(coordinator.TryGetAutomaticStopAdmission(receipt.SessionId, out var recovered));
        Assert.Same(receipt, recovered);
    }

    [Fact]
    public async Task DuplicateStopUsesSameGenerationSafely()
    {
        var sink = new AdmissionSink();
        using var coordinator = Coordinator(sink);
        var first = await coordinator.RequestAutomaticStopAsync(sink.Session.SessionId);
        var second = await coordinator.RequestAutomaticStopAsync(sink.Session.SessionId);
        Assert.True(first.IsNew);
        Assert.False(second.IsNew);
        Assert.Same(first.Admission, second.Admission);
        Assert.Equal(1, sink.AdmissionCalls);
        sink.Finish(RecordingSinkStopResult.Stopped);
        await first.Admission!.Completion;
    }

    [Fact]
    public async Task StaleGenerationCannotBeAdmitted()
    {
        var sink = new AdmissionSink();
        using var coordinator = Coordinator(sink);
        var result = await coordinator.RequestAutomaticStopAsync(new RecordingSessionId(Guid.NewGuid()));
        Assert.Equal(RecordingAutomaticStopNotAdmittedReason.StaleGeneration, result.Reason);
        Assert.Equal(0, sink.AdmissionCalls);
        Assert.Same(sink.Session, coordinator.State.Session);
    }

    [Fact]
    public async Task ManualSessionCannotBeAdmittedByAutomaticStop()
    {
        var sink = new AdmissionSink { Session = RecordingSinkSession.Manual(new(Guid.NewGuid()), Channel) };
        using var coordinator = new RecordingAutomationChannelCoordinator(
            Channel, sink, RecordingAutomationExecutionState.ManualRecording(sink.Session));
        var result = await coordinator.RequestAutomaticStopAsync(sink.Session.SessionId);
        Assert.Equal(RecordingAutomaticStopNotAdmittedReason.ManualMismatch, result.Reason);
        Assert.Equal(0, sink.AdmissionCalls);
    }

    [Fact]
    public async Task MissingSessionCannotBeAdmitted()
    {
        var sink = new AdmissionSink();
        using var coordinator = new RecordingAutomationChannelCoordinator(Channel, sink);
        var result = await coordinator.RequestAutomaticStopAsync(sink.Session.SessionId);
        Assert.Equal(RecordingAutomaticStopNotAdmittedReason.NoActiveSession, result.Reason);
        Assert.Equal(0, sink.AdmissionCalls);
    }

    [Fact]
    public async Task ReplacementSessionUnaffectedByOldCompletion()
    {
        var sink = new AdmissionSink();
        using var coordinator = Coordinator(sink);
        var receipt = (await coordinator.RequestAutomaticStopAsync(sink.Session.SessionId)).Admission!;
        var replacing = coordinator.StartManualAsync(Candidate(), CancellationToken.None).AsTask();
        Assert.False(replacing.IsCompleted);
        sink.Finish(RecordingSinkStopResult.Stopped);
        await receipt.Completion;
        var replacement = await replacing;
        Assert.Equal(RecordingSessionOrigin.Manual, replacement.State.Session!.Origin);
        Assert.NotEqual(receipt.SessionId, replacement.State.Session.SessionId);
        Assert.Same(replacement.State.Session, coordinator.State.Session);
        await receipt.Completion;
        Assert.Same(replacement.State.Session, coordinator.State.Session);
    }

    [Fact]
    public async Task DefensiveExactCompletionCannotClearReplacementState()
    {
        var sink = new AdmissionSink();
        using var coordinator = Coordinator(sink);
        var receipt = (await coordinator.RequestAutomaticStopAsync(sink.Session.SessionId)).Admission!;
        var replacement = new RecordingSinkSession(new(Guid.NewGuid()), Channel, RuleB);
        var replacementState = RecordingAutomationExecutionState.Recording(
            RecordingAutomationState.Recording(RuleB), replacement);
        // Normal mutations wait for the receipt (covered separately). Inject a
        // replacement to exercise the finalizer's defensive exact-session guard.
        typeof(RecordingAutomationChannelCoordinator).GetField(
            "state", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(coordinator, replacementState);
        sink.Finish(RecordingSinkStopResult.Stopped);
        Assert.Same(replacementState, (await receipt.Completion).State);
        Assert.Same(replacementState, coordinator.State);
        Assert.Equal(RuleB, coordinator.State.AutomationState.ActiveRuleId);
    }

    [Fact]
    public async Task CurrentActiveRuleIdCapturedAtAdmission()
    {
        var sink = new AdmissionSink();
        using var coordinator = Coordinator(sink, RuleB);
        var receipt = (await coordinator.RequestAutomaticStopAsync(sink.Session.SessionId)).Admission!;
        Assert.Equal(RuleA, sink.Session.AutomaticRuleId);
        Assert.Equal(RuleB, receipt.ActiveRuleId);
        sink.Finish(RecordingSinkStopResult.Stopped);
        await receipt.Completion;
    }

    [Fact]
    public async Task StartedByAActiveBPendingStopRetainsB()
    {
        var sink = new AdmissionSink();
        using var coordinator = Coordinator(sink);
        await coordinator.EvaluateAsync(Candidate(), Rules(RuleB), CancellationToken.None);
        Assert.Equal(RuleB, coordinator.State.AutomationState.ActiveRuleId);
        Assert.Equal(RuleA, coordinator.State.Session!.AutomaticRuleId);
        var receipt = (await coordinator.RequestAutomaticStopAsync(sink.Session.SessionId)).Admission!;
        using var wait = new CancellationTokenSource();
        wait.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receipt.Completion.WaitAsync(wait.Token));
        sink.Finish(RecordingSinkStopResult.Stopped);
        await receipt.Completion;
        Assert.Equal(RuleB, receipt.ActiveRuleId);
    }

    [Fact]
    public async Task UnexpectedExceptionAfterAdmissionStillExposesAdmissionIdentity()
    {
        var sink = new AdmissionSink { ThrowAfterAdmission = true };
        using var coordinator = Coordinator(sink);
        var receipt = (await coordinator.RequestAutomaticStopAsync(sink.Session.SessionId)).Admission!;
        Assert.True(coordinator.TryGetAutomaticStopAdmission(receipt.SessionId, out var recovered));
        Assert.Same(receipt, recovered);
        Assert.Equal(1, sink.AdmissionCalls);
        sink.Finish(RecordingSinkStopResult.Stopped);
        await receipt.Completion;
    }

    [Fact]
    public async Task UnsupportedSinkAdmissionFailsClosed()
    {
        var legacy = new LegacySink();
        using var coordinator = new RecordingAutomationChannelCoordinator(
            Channel, legacy, RecordingAutomationExecutionState.Recording(
                RecordingAutomationState.Recording(RuleA), legacy.Session));
        var result = await coordinator.RequestAutomaticStopAsync(legacy.Session.SessionId);
        Assert.Equal(RecordingAutomaticStopNotAdmittedReason.Unavailable, result.Reason);
        Assert.Equal(0, legacy.Stops);
        Assert.Equal(RecordingAutomationStopResultKind.Stopped,
            (await coordinator.StopAsync(CancellationToken.None)).Kind);
        Assert.Equal(1, legacy.Stops);
    }

    [Fact]
    public async Task LegacyAlreadyStoppingDoesNotCreateUserAdmission()
    {
        var sink = new AdmissionSink { OtherStop = true };
        using var coordinator = Coordinator(sink);
        var result = await coordinator.RequestAutomaticStopAsync(sink.Session.SessionId);
        Assert.Equal(RecordingAutomaticStopNotAdmittedReason.AlreadyStoppingByOtherOperation, result.Reason);
        Assert.Null(result.Admission);
    }

    [Fact]
    public async Task PendingOwnerPreventsIdleEviction()
    {
        var sink = new AdmissionSink();
        using var manager = new RecordingAutomationManager(sink, maximumChannelCount: 1);
        var start = await manager.EvaluateAsync(Candidate(), Rules(RuleA), CancellationToken.None);
        var receipt = (await manager.RequestAutomaticStopAsync(Channel, start.State.Session!.SessionId)).Admission!;
        Assert.Equal(1, manager.Count);
        Assert.True(manager.TryGetAutomaticStopAdmission(Channel, receipt.SessionId, out var recovered));
        Assert.Same(receipt, recovered);
        var stale = await manager.RequestAutomaticStopAsync(Channel, new RecordingSessionId(Guid.NewGuid()));
        Assert.Equal(RecordingAutomaticStopNotAdmittedReason.StaleGeneration, stale.Reason);
        Assert.Equal(1, manager.Count);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await manager.StartManualAsync(
            Candidate(new ChannelId(Guid.NewGuid())), CancellationToken.None));
        sink.Finish(RecordingSinkStopResult.Stopped);
        await receipt.Completion;
        await manager.ShutdownAsync(CancellationToken.None);
        Assert.False(manager.AcceptingEvaluations);
        Assert.True(manager.TryGetAutomaticStopAdmission(Channel, receipt.SessionId, out recovered));
        Assert.Same(receipt, recovered);
    }

    [Fact]
    public async Task ShutdownAfterAdmissionDrainsOrOwnsCompletion()
    {
        var sink = new AdmissionSink();
        using var manager = new RecordingAutomationManager(sink);
        var start = await manager.EvaluateAsync(Candidate(), Rules(RuleA), CancellationToken.None);
        var receipt = (await manager.RequestAutomaticStopAsync(Channel, start.State.Session!.SessionId)).Admission!;
        using var shutdownWait = new CancellationTokenSource();
        var shutdown = manager.ShutdownAsync(shutdownWait.Token).AsTask();
        Assert.False(shutdown.IsCompleted);
        Assert.False(manager.AcceptingEvaluations);
        Assert.Equal(RecordingAutomaticStopNotAdmittedReason.Unavailable,
            (await manager.RequestAutomaticStopAsync(Channel, receipt.SessionId)).Reason);
        shutdownWait.Cancel();
        Assert.False(shutdown.IsCompleted);
        sink.Finish(RecordingSinkStopResult.Stopped);
        await receipt.Completion;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => shutdown);
        await manager.ShutdownAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ShutdownClosesQueuedAdmissionBeforeEffect()
    {
        var sink = new AdmissionSink { BlockStart = true };
        using var manager = new RecordingAutomationManager(sink);
        var evaluation = manager.EvaluateAsync(Candidate(), Rules(RuleA), CancellationToken.None).AsTask();
        await sink.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var request = manager.RequestAutomaticStopAsync(Channel, sink.Session.SessionId).AsTask();
        Assert.False(request.IsCompleted);
        var shutdown = manager.ShutdownAsync(CancellationToken.None).AsTask();
        Assert.Equal(RecordingAutomaticStopNotAdmittedReason.CancelledBeforeAdmission, (await request).Reason);
        await evaluation;
        await shutdown;
        Assert.Equal(0, sink.AdmissionCalls);
    }

    [Fact]
    public async Task PendingEntryPinSurvivesDefensiveIdleStateAndReleasesAfterCompletion()
    {
        var sink = new AdmissionSink();
        using var manager = new RecordingAutomationManager(sink, maximumChannelCount: 1);
        var start = await manager.EvaluateAsync(Candidate(), Rules(RuleA), CancellationToken.None);
        var receipt = (await manager.RequestAutomaticStopAsync(Channel, start.State.Session!.SessionId)).Admission!;
        var entries = (System.Collections.IDictionary)typeof(RecordingAutomationManager).GetField(
            "coordinators", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(manager)!;
        var entry = entries[Channel]!;
        var coordinator = (RecordingAutomationChannelCoordinator)entry.GetType().GetProperty("Coordinator")!.GetValue(entry)!;
        typeof(RecordingAutomationChannelCoordinator).GetField(
            "state", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(coordinator, RecordingAutomationExecutionState.Idle);
        Assert.Equal(RecordingAutomaticStopNotAdmittedReason.NoActiveSession,
            (await manager.RequestAutomaticStopAsync(Channel, new(Guid.NewGuid()))).Reason);
        Assert.Equal(1, manager.Count);
        sink.Finish(RecordingSinkStopResult.Stopped);
        await receipt.Completion;
        await (Task)entry.GetType().GetProperty("StopOwner")!.GetValue(entry)!;
        Assert.Equal(0, manager.Count);
        Assert.True(manager.TryGetAutomaticStopAdmission(Channel, receipt.SessionId, out var recovered));
        Assert.Same(receipt, recovered);
    }

    [Fact]
    public async Task NonCooperativeSourceRemainsOwnedAndDoesNotClaimCleanShutdown()
    {
        var sink = new AdmissionSink();
        var manager = new RecordingAutomationManager(sink);
        var start = await manager.EvaluateAsync(Candidate(), Rules(RuleA), CancellationToken.None);
        var receipt = (await manager.RequestAutomaticStopAsync(Channel, start.State.Session!.SessionId)).Admission!;
        var shutdown = manager.ShutdownAsync(CancellationToken.None).AsTask();
        Assert.False(receipt.Completion.IsCompleted);
        Assert.False(shutdown.IsCompleted);
        Assert.Throws<InvalidOperationException>(manager.Dispose);
        sink.Finish(RecordingSinkStopResult.Stopped);
        await shutdown;
        manager.Dispose();
    }

    [Fact]
    public async Task NoUnobservedCompletionException()
    {
        var sink = new AdmissionSink();
        var coordinator = Coordinator(sink);
        var receipt = (await coordinator.RequestAutomaticStopAsync(sink.Session.SessionId)).Admission!;
        sink.FailCompletion(new IOException("private"));
        await sink.WrapperObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // Use a subsequent exact duplicate as a deterministic channel-gate
        // barrier after the worker has observed the fault.
        var owner = typeof(RecordingAutomationChannelCoordinator).GetField(
            "automaticStopOwner", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        await Assert.IsAssignableFrom<Task>(owner.GetValue(coordinator));
        Assert.NotNull(receipt.ResolutionFault);
        Assert.DoesNotContain("private", receipt.ResolutionFault!.ToString());
        Assert.False(receipt.Completion.IsCompleted);
        Assert.Throws<InvalidOperationException>(coordinator.Dispose);
    }

    private static RecordingAutomationChannelCoordinator Coordinator(AdmissionSink sink, Guid? winner = null) =>
        new(Channel, sink, RecordingAutomationExecutionState.Recording(
            RecordingAutomationState.Recording(winner ?? RuleA), sink.Session));

    private static RecordingCandidate Candidate(ChannelId? channel = null) =>
        new(channel ?? Channel, new ChannelMetadataSnapshot(name: "test"));

    private static RecordingRuleSet Rules(Guid id) => new([
        new RecordingRule(id, "test", RecordingRuleAction.Record, RecordingRuleCriteria.MatchAll),
    ]);

    private sealed class LegacySink : IRecordingSink
    {
        public RecordingSinkSession Session { get; } = new(new(Guid.NewGuid()), Channel, RuleA);
        public int Stops { get; private set; }
        public ValueTask<RecordingSinkStartResult> StartAsync(RecordingSinkStartRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(RecordingSinkStartResult.Started(Session));
        public ValueTask<RecordingSinkStopResult> StopAsync(RecordingSinkStopRequest request, CancellationToken cancellationToken)
        {
            Stops++;
            return ValueTask.FromResult(RecordingSinkStopResult.Stopped);
        }
    }

    private sealed class AdmissionSink : IRecordingStopAdmissionSink
    {
        private readonly TaskCompletionSource<RecordingSinkStopResult> terminal =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private RecordingSinkStopAdmission? receipt;
        public RecordingSinkSession Session { get; set; } = new(new(Guid.NewGuid()), Channel, RuleA);
        public bool Terminal { get; set; }
        public bool OtherStop { get; set; }
        public bool CompleteInsideAdmission { get; set; }
        public bool ThrowAfterAdmission { get; set; }
        public bool BlockStart { get; set; }
        public TaskCompletionSource StartEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int AdmissionCalls { get; private set; }
        public TaskCompletionSource WrapperObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Finish(RecordingSinkStopResult result) => terminal.TrySetResult(result);
        public void FailCompletion(Exception exception)
        {
            terminal.TrySetException(exception);
            WrapperObserved.TrySetResult();
        }

        public RecordingSinkStopAdmissionResult RequestStopAdmission(
            RecordingSinkStopRequest request, CancellationToken admissionToken = default)
        {
            AdmissionCalls++;
            if (Terminal)
            {
                return RecordingSinkStopAdmissionResult.NotAdmitted(RecordingSinkStopNotAdmittedReason.AlreadyTerminal);
            }
            if (OtherStop || receipt is not null)
            {
                return RecordingSinkStopAdmissionResult.NotAdmitted(
                    RecordingSinkStopNotAdmittedReason.AlreadyStoppingByOtherOperation);
            }
            receipt = new(Session, terminal.Task);
            if (CompleteInsideAdmission)
            {
                Finish(RecordingSinkStopResult.Stopped);
            }
            if (ThrowAfterAdmission)
            {
                throw new IOException("return lost after effect");
            }
            return RecordingSinkStopAdmissionResult.Admitted(receipt);
        }

        public bool TryGetStopAdmission(RecordingSinkSession session, out RecordingSinkStopAdmission? admission)
        {
            admission = receipt?.Session.SessionId == session.SessionId ? receipt : null;
            return admission is not null;
        }

        public async ValueTask<RecordingSinkStartResult> StartAsync(RecordingSinkStartRequest request, CancellationToken cancellationToken)
        {
            if (BlockStart)
            {
                StartEntered.TrySetResult();
                await releaseStart.Task.WaitAsync(cancellationToken);
            }
            Session = request.Origin == RecordingSessionOrigin.Manual
                ? RecordingSinkSession.Manual(new(Guid.NewGuid()), request.Candidate.ChannelId)
                : new(new(Guid.NewGuid()), request.Candidate.ChannelId, request.RuleId!.Value);
            return RecordingSinkStartResult.Started(Session);
        }

        public ValueTask<RecordingSinkStopResult> StopAsync(RecordingSinkStopRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(RecordingSinkStopResult.Stopped);
    }
}
