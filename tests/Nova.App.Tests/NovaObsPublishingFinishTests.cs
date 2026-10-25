using System.Text.Json;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Network.Rtmp;

namespace PeerCast.Nova.App.Tests;

public sealed class NovaObsPublishingFinishTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ProductionAdapterFinishesExistingOwnerWithoutPublisherOrObsEos()
    {
        var owner = new NovaObsPublishingRuntime(new() { Ingest = new() { Port = 0 } });
        await using var controller = new NovaObsPublishingLifecycleController(owner);
        controller.RequestStart();
        var result = await controller.RequestFinishAsync(Limit).WaitAsync(Limit);
        AssertLocal(result);
        Assert.Equal(ObsPublishWriterEvidence.NotRequired, result.LocalTermination!.WriterEvidence);
        Assert.Equal(ObsPublishChannelEndReason.LocalTerminated, (await owner.Completion).EndReason);
        Assert.Equal("終了（Novaローカル）", controller.Read().StateText);
        Assert.False(owner.Snapshot.ListenerReady);
    }

    [Fact]
    public async Task LocalFinishPreservesNotObservedWriterAndAllowsOnlyFreshExplicitGeneration()
    {
        var first = new Owner { AutoFinish = true };
        var second = new Owner { AutoFinish = true };
        var factory = new Factory(first, second);
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        await composition.Commands.RequestStartAsync();
        var identity = composition.Read();
        var result = await composition.RequestFinishAsync(Limit).WaitAsync(Limit);
        AssertLocal(result);
        Assert.Equal(ObsPublishWriterEvidence.NotObserved, result.LocalTermination!.WriterEvidence);
        Assert.Equal(ObsPublishWriterEvidence.NotObserved, composition.Read().WriterEvidence);
        Assert.Equal(identity.SessionId, result.SessionId);
        Assert.True(composition.ReadCommands().CanStart);
        Assert.Equal(1, factory.Creates); // Reads and Finish never auto-restart.
        await composition.RequestStartAsync();
        Assert.Equal(2, factory.Creates);
        Assert.Equal(2, composition.Read().Generation);
        Assert.NotEqual(identity.SessionId, composition.Read().SessionId);
        await composition.RequestFinishAsync(Limit);
        Assert.All(new[] { first, second }, owner => Assert.Equal(1, owner.Disposals));
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(result));
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(composition.Read()));
    }

    [Fact]
    public async Task DuplicateFinishSharesOneRequestAndFirstDeadlineAndToken()
    {
        var owner = new Owner();
        await using var controller = new NovaObsPublishingLifecycleController(owner);
        controller.RequestStart();
        var finish = controller.RequestFinishAsync(Limit);
        await owner.FinishEntered.Task.WaitAsync(Limit);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Same(finish, controller.RequestFinishAsync(TimeSpan.FromMilliseconds(1), cancelled.Token));
        owner.End(owner.Local());
        AssertLocal(await finish.WaitAsync(Limit));
        Assert.Same(finish, controller.RequestFinishAsync(Limit));
        Assert.Equal(1, owner.Finishes);
        Assert.Equal(1, owner.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopOrDisposeDuringFinishIsCancellationNotLocalSuccess(bool dispose)
    {
        var owner = new Owner();
        await using var controller = new NovaObsPublishingLifecycleController(owner);
        controller.RequestStart();
        var finish = controller.RequestFinishAsync(Limit);
        await owner.FinishEntered.Task.WaitAsync(Limit);
        var stop = dispose ? controller.DisposeAsync().AsTask() : controller.RequestStopAsync();
        var result = await finish.WaitAsync(Limit);
        await stop.WaitAsync(Limit);
        Assert.Equal(NovaObsPublishingCompletionKind.Cancelled, result.Kind);
        Assert.True(result.CleanupCompleted);
        Assert.False(result.LocalCompletedProven);
        Assert.False(result.NormalEosProven);
        Assert.True(result.IdentityMatched);
        Assert.Equal(1, owner.Disposals);
    }

    [Fact]
    public async Task FinishDuringStartWaitsForSettledStartAndForwardsOnlyRemainingDeadline()
    {
        var owner = new Owner { BlockStart = true, AutoFinish = true };
        await using var controller = new NovaObsPublishingLifecycleController(owner);
        var start = Task.Run(() => controller.RequestStart());
        await owner.StartEntered.Task.WaitAsync(Limit);
        var finish = controller.RequestFinishAsync(Limit);
        Assert.Equal(NovaObsPublishingLifecycleState.Stopping, controller.Read().LifecycleState);
        Assert.False(finish.IsCompleted);
        Assert.Equal(0, owner.Finishes);
        owner.ReleaseStart.TrySetResult();
        await start.WaitAsync(Limit);
        AssertLocal(await finish.WaitAsync(Limit));
        Assert.InRange(owner.FinishTimeout, TimeSpan.Zero, Limit);
        Assert.Equal(1, owner.Finishes);
        Assert.Equal(1, owner.Disposals);
    }

    [Fact]
    public async Task DeadlineIncludesBlockedStart_NoLateStartupCanRepairTerminalOrRestart()
    {
        var owner = new Owner { BlockStart = true };
        var controller = new NovaObsPublishingLifecycleController(owner);
        var start = Task.Run(() => controller.RequestStart());
        await owner.StartEntered.Task.WaitAsync(Limit);
        var result = await controller.RequestFinishAsync(TimeSpan.FromMilliseconds(100)).WaitAsync(Limit);
        Assert.Equal(NovaObsPublishingCompletionKind.TimedOut, result.Kind);
        Assert.False(result.CleanupCompleted);
        Assert.False(result.OwnerCompletionObserved);
        Assert.Equal(0, owner.Finishes);
        Assert.Equal(0, owner.Disposals); // Cannot dispose an unsettled synchronous Start.
        owner.ReleaseStart.TrySetResult();
        await start.WaitAsync(Limit);
        await owner.DisposeEntered.Task.WaitAsync(Limit);
        Assert.Same(result, await controller.Completion);
        Assert.Equal(NovaObsPublishingStartResult.ClosedGeneration, controller.RequestStart());
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task CompositionShutdownDoesNotExtendFinishDeadlineWithBlockedStartCommand()
    {
        var owner = new Owner { BlockStart = true };
        var composition = NovaGuiNextPublishingComposition.Create(new Factory(owner));
        var start = composition.RequestStartAsync();
        await owner.StartEntered.Task.WaitAsync(Limit);
        try
        {
            var finish = composition.RequestFinishAsync(TimeSpan.FromMilliseconds(100));
            var disposal = composition.DisposeAsync().AsTask();
            var result = await finish.WaitAsync(Limit);
            Assert.Equal(NovaObsPublishingCompletionKind.TimedOut, result.Kind);
            await Assert.ThrowsAsync<InvalidOperationException>(() => disposal.WaitAsync(Limit));
            Assert.False(result.CleanupCompleted);
            Assert.False(start.IsCompleted);
            Assert.Equal(0, owner.Disposals);
            Assert.False(composition.ReadCommands().CanStart);
        }
        finally
        {
            owner.ReleaseStart.TrySetResult();
            await start.WaitAsync(Limit);
            await owner.DisposeEntered.Task.WaitAsync(Limit);
        }
        Assert.Equal(1, owner.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BoundedFinishObservesTimeoutOrCallerCancellationWithoutInventingOwnerEvidence(bool cancel)
    {
        var owner = new Owner { IgnoreFinishCancellation = true };
        var controller = new NovaObsPublishingLifecycleController(owner);
        controller.RequestStart();
        using var cancellation = new CancellationTokenSource();
        var finish = controller.RequestFinishAsync(cancel ? Limit : TimeSpan.FromMilliseconds(100), cancellation.Token);
        if (cancel)
        {
            await owner.FinishEntered.Task.WaitAsync(Limit);
            cancellation.Cancel();
        }
        var result = await finish.WaitAsync(Limit);
        Assert.Equal(cancel ? NovaObsPublishingCompletionKind.Cancelled : NovaObsPublishingCompletionKind.TimedOut, result.Kind);
        Assert.False(result.LocalCompletedProven);
        Assert.False(result.CleanupCompleted); // Disposal alone is not a matching owner terminal result.
        Assert.False(result.OwnerCompletionObserved);
        owner.End(owner.Local());
        Assert.Same(result, await controller.Completion);
        Assert.False(result.LocalCompletedProven);
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task CleanupWaitUsesSameDeadline_LateDisposalNeverUpgradesOrEnablesRestart()
    {
        var owner = new Owner { AutoFinish = true, BlockDispose = true };
        var factory = new Factory(owner, new Owner());
        var composition = NovaGuiNextPublishingComposition.Create(factory);
        await composition.RequestStartAsync();
        var result = await composition.RequestFinishAsync(TimeSpan.FromMilliseconds(200)).WaitAsync(Limit);
        Assert.Equal(NovaObsPublishingCompletionKind.TimedOut, result.Kind);
        Assert.False(result.CleanupCompleted);
        Assert.False(composition.ReadCommands().CanStart);
        owner.ReleaseDispose.TrySetResult();
        await owner.DisposeFinished.Task.WaitAsync(Limit);
        Assert.Same(result, await composition.Lifecycle.Completion);
        await composition.RequestStartAsync();
        Assert.Equal(1, factory.Creates);
        await Assert.ThrowsAsync<InvalidOperationException>(() => composition.DisposeAsync().AsTask());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task WrongIdentityOrDifferentCompletionResultIsRejected(int mismatch)
    {
        var owner = new Owner();
        var controller = new NovaObsPublishingLifecycleController(owner);
        controller.RequestStart();
        var finish = controller.RequestFinishAsync(Limit);
        await owner.FinishEntered.Task.WaitAsync(Limit);
        var local = owner.Local();
        if (mismatch == 0) local = local with { SessionId = new ChannelSessionId(Guid.NewGuid()) };
        if (mismatch == 1) local = local with { ChannelId = new ChannelId(Guid.NewGuid()) };
        owner.End(local, mismatch == 2 ? local with { WriterEvidence = ObsPublishWriterEvidence.NotRequired } : local);
        var result = await finish.WaitAsync(Limit);
        Assert.Equal(NovaObsPublishingLifecycleFailure.IdentityUnproven, result.Failure);
        Assert.False(result.IdentityMatched);
        Assert.False(result.CleanupCompleted);
        Assert.False(result.LocalCompletedProven);
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task OwnerCompletionIsIndependentEvenAfterTypedResultWasReturned()
    {
        var owner = new Owner { AutoFinish = true, HoldCompletion = true };
        var controller = new NovaObsPublishingLifecycleController(owner);
        controller.RequestStart();
        using var cancellation = new CancellationTokenSource();
        var finish = controller.RequestFinishAsync(Limit, cancellation.Token);
        await owner.FinishEntered.Task.WaitAsync(Limit);
        cancellation.Cancel();
        var result = await finish.WaitAsync(Limit);
        Assert.Equal(NovaObsPublishingCompletionKind.Cancelled, result.Kind);
        Assert.False(result.OwnerCompletionObserved);
        Assert.False(result.CleanupCompleted);
        Assert.NotNull(result.LocalTermination);
        owner.HoldCompletion = false;
        owner.End(owner.Local());
        Assert.Same(result, await controller.Completion);
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task ActualCleanupFailureRejectsLocalCompletedAndReplacement()
    {
        var owner = new Owner { AutoFinish = true, ThrowDispose = true };
        var factory = new Factory(owner, new Owner());
        var composition = NovaGuiNextPublishingComposition.Create(factory);
        await composition.RequestStartAsync();
        var result = await composition.RequestFinishAsync(Limit);
        Assert.Equal(NovaObsPublishingCompletionKind.CleanupIncomplete, result.Kind);
        Assert.Equal(NovaObsPublishingLifecycleFailure.CleanupFailed, result.Failure);
        Assert.False(result.CleanupCompleted);
        Assert.False(composition.ReadCommands().CanStart);
        await composition.RequestStartAsync();
        Assert.Equal(1, factory.Creates);
        Assert.Equal(1, owner.Disposals);
        await Assert.ThrowsAsync<InvalidOperationException>(() => composition.DisposeAsync().AsTask());
    }

    [Theory]
    [InlineData(ObsPublishLocalTerminationOutcome.TimedOut, nameof(NovaObsPublishingCompletionKind.TimedOut))]
    [InlineData(ObsPublishLocalTerminationOutcome.DeliveryFailed, nameof(NovaObsPublishingCompletionKind.DeliveryFailed))]
    [InlineData(ObsPublishLocalTerminationOutcome.CleanupIncomplete, nameof(NovaObsPublishingCompletionKind.CleanupIncomplete))]
    [InlineData(ObsPublishLocalTerminationOutcome.Failed, nameof(NovaObsPublishingCompletionKind.Failed))]
    public async Task OwnerFailureOutcomesStayDistinctAndNeverPermitRestart(ObsPublishLocalTerminationOutcome outcome, string expected)
    {
        var owner = new Owner { AutoFinish = true, Outcome = outcome };
        var composition = NovaGuiNextPublishingComposition.Create(new Factory(owner, new Owner()));
        await composition.RequestStartAsync();
        var result = await composition.RequestFinishAsync(Limit);
        Assert.Equal(expected, result.Kind.ToString());
        Assert.Equal(NovaObsPublishingLifecycleState.Failed, result.State);
        Assert.False(result.NormalEosProven);
        Assert.False(result.LocalCompletedProven);
        Assert.False(composition.ReadCommands().CanStart);
        Assert.Equal(outcome, composition.Read().LocalTerminationOutcome);
        await composition.RequestStartAsync();
        // Disposal still succeeds if cleanup itself was actually proven; never permits replacement.
        await composition.DisposeAsync();
        Assert.Equal(1, owner.Starts);
    }

    [Fact]
    public async Task ChangedLiveGenerationCannotValidatePreviouslyMatchingTerminal()
    {
        var owner = new Owner();
        var controller = new NovaObsPublishingLifecycleController(owner);
        controller.RequestStart();
        var finish = controller.RequestFinishAsync(Limit);
        await owner.FinishEntered.Task.WaitAsync(Limit);
        var old = owner.Local();
        owner.Current = owner.Current with { SessionId = new ChannelSessionId(Guid.NewGuid()), Revision = 9 };
        owner.End(old);
        var result = await finish;
        Assert.Equal(NovaObsPublishingLifecycleFailure.IdentityUnproven, result.Failure);
        Assert.False(result.LocalCompletedProven);
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task OldLifecycleFinishAndSnapshotCannotAffectReplacementOwner()
    {
        var first = new Owner { AutoFinish = true };
        var second = new Owner { AutoFinish = true };
        await using var composition = NovaGuiNextPublishingComposition.Create(new Factory(first, second));
        await composition.RequestStartAsync();
        var old = composition.Lifecycle;
        var result = await composition.RequestFinishAsync(Limit);
        AssertLocal(result);
        await composition.RequestStartAsync();
        var current = composition.Read();
        first.Current = first.Current with { Revision = 100, State = ChannelSessionState.Receiving };
        Assert.Same(result, await old.RequestFinishAsync(Limit));
        await old.DisposeAsync();
        Assert.Equal(current, composition.Read());
        Assert.Equal(0, second.Disposals);
        Assert.Equal(0, second.Finishes);
        await composition.RequestFinishAsync(Limit);
    }

    [Fact]
    public async Task InvalidUnstartedPreCancelledAndUnsupportedRequestsHaveNoFinishSideEffects()
    {
        var owner = new Owner();
        await using var controller = new NovaObsPublishingLifecycleController(owner);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => controller.RequestFinishAsync(TimeSpan.Zero));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => controller.RequestFinishAsync(TimeSpan.FromMinutes(2)));
        Assert.Equal(NovaObsPublishingLifecycleFailure.FinishUnavailable, (await controller.RequestFinishAsync(Limit)).Failure);
        Assert.Equal(0, owner.Starts);
        controller.RequestStart();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Equal(NovaObsPublishingCompletionKind.Cancelled, (await controller.RequestFinishAsync(Limit, cancelled.Token)).Kind);
        Assert.Equal(NovaObsPublishingLifecycleState.Running, controller.Read().LifecycleState);
        Assert.Equal(0, owner.Finishes);
        Assert.Equal(0, owner.Disposals);
        await using var unsupported = new NovaObsPublishingLifecycleController(new UnsupportedOwner());
        unsupported.RequestStart();
        Assert.Equal(NovaObsPublishingLifecycleFailure.FinishUnavailable, (await unsupported.RequestFinishAsync(Limit)).Failure);
        Assert.Equal(NovaObsPublishingLifecycleState.Running, unsupported.Read().LifecycleState);
    }

    [Fact]
    public async Task StopAloneNeverInvokesFinishAndKeepsForcedCancellationSemantics()
    {
        var owner = new Owner();
        await using var controller = new NovaObsPublishingLifecycleController(owner);
        controller.RequestStart();
        var result = await controller.RequestStopAsync();
        Assert.Equal(NovaObsPublishingCompletionKind.Cancelled, result.Kind);
        Assert.False(result.FinishRequested);
        Assert.False(result.NormalEosProven);
        Assert.Equal(0, owner.Finishes);
        Assert.Equal(1, owner.Disposals);
    }

    [Fact]
    public async Task StopWinningFinishAllowsReplacementOnlyAfterMatchingCancellationAndCleanup()
    {
        var old = new Owner();
        await using var composition = NovaGuiNextPublishingComposition.Create(new Factory(old, new Owner()));
        await composition.RequestStartAsync();
        var finish = composition.RequestFinishAsync(Limit);
        await old.FinishEntered.Task.WaitAsync(Limit);
        await composition.Lifecycle.RequestStopAsync();
        var result = await finish;
        Assert.Equal(NovaObsPublishingCompletionKind.Cancelled, result.Kind);
        Assert.True(result.IdentityMatched);
        Assert.True(result.OwnerCompletionObserved);
        Assert.True(result.CleanupCompleted);
        Assert.True(composition.ReadCommands().CanStart);
        await composition.RequestStartAsync();
        Assert.Equal(2, composition.Read().Generation);
    }

    [Fact]
    public async Task IndependentObsEosWinningBeforeOwnerAdmissionKeepsItsProofAndCause()
    {
        var owner = new Owner { ObsEosWins = true };
        await using var composition = NovaGuiNextPublishingComposition.Create(new Factory(owner, new Owner()));
        await composition.RequestStartAsync();
        var result = await composition.RequestFinishAsync(Limit);
        Assert.Equal(NovaObsPublishingCompletionKind.ObsNormalEos, result.Kind);
        Assert.True(result.NormalEosProven);
        Assert.False(result.LocalCompletedProven);
        Assert.True(result.CleanupCompleted);
        Assert.True(result.IdentityMatched);
        Assert.True(composition.ReadCommands().CanStart);
        Assert.Equal("停止（OBS正常EOS）", composition.Read().StateText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalCompletedLabelWithoutOwnerShutdownOrUpstreamProofIsRejected(bool missingShutdown)
    {
        var owner = new Owner();
        var controller = new NovaObsPublishingLifecycleController(owner);
        controller.RequestStart();
        var finish = controller.RequestFinishAsync(Limit);
        await owner.FinishEntered.Task.WaitAsync(Limit);
        var local = owner.Local();
        if (!missingShutdown) local = local with { UpstreamEnded = false };
        owner.End(local, shutdown: !missingShutdown);
        var result = await finish;
        Assert.Equal(missingShutdown ? NovaObsPublishingCompletionKind.CleanupIncomplete : NovaObsPublishingCompletionKind.Failed, result.Kind);
        Assert.False(result.LocalCompletedProven);
        if (missingShutdown) await Assert.ThrowsAsync<InvalidOperationException>(() => controller.DisposeAsync().AsTask());
        else await controller.DisposeAsync();
    }

    [Fact]
    public async Task OwnerCompletionExceptionDuringFinishCannotBecomeCleanupOrNormalSuccess()
    {
        var owner = new Owner();
        var controller = new NovaObsPublishingLifecycleController(owner);
        controller.RequestStart();
        var finish = controller.RequestFinishAsync(Limit);
        await owner.FinishEntered.Task.WaitAsync(Limit);
        owner.Completed.TrySetException(new IOException("SECRET exception"));
        owner.End(owner.Local());
        var result = await finish;
        Assert.Equal(NovaObsPublishingCompletionKind.Failed, result.Kind);
        Assert.False(result.CleanupCompleted);
        Assert.False(result.OwnerCompletionObserved);
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(result));
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.DisposeAsync().AsTask());
    }

    private static void AssertLocal(NovaObsPublishingLifecycleResult result)
    {
        Assert.Equal(NovaObsPublishingLifecycleState.Stopped, result.State);
        Assert.Equal(NovaObsPublishingCompletionKind.LocalCompleted, result.Kind);
        Assert.Equal(NovaObsPublishingLifecycleFailure.None, result.Failure);
        Assert.True(result.CleanupCompleted);
        Assert.True(result.LocalCompletedProven);
        Assert.True(result.OwnerCompletionObserved);
        Assert.True(result.IdentityMatched);
        Assert.False(result.NormalEosProven);
        Assert.Equal(ObsPublishChannelEndReason.LocalTerminated, result.EndReason);
    }

    private sealed class Factory(params Owner[] owners) : INovaObsPublishingRuntimeFactory
    {
        internal int Creates;
        public INovaObsPublishingRuntime Create() => owners[Interlocked.Increment(ref Creates) - 1];
    }

    internal class UnsupportedOwner : INovaObsPublishingLifecycleRuntime
    {
        private ObsPublishStatusSnapshot _current = new(new ChannelId(Guid.NewGuid()), new ChannelSessionId(Guid.NewGuid()),
            1, ChannelSessionState.Created, false, false, false, "rtmp://SECRET", "SECRET", "http://SECRET", null, null, null);
        internal UnsupportedOwner() => Proof = new(_current.ChannelId.Value, _current.SessionId.Value);
        internal NovaObsPublishingGenerationProof Proof { get; }
        internal ObsPublishStatusSnapshot Current
        {
            get { lock (Proof.Gate) return _current; }
            set
            {
                lock (Proof.Gate)
                {
                    if (value.ChannelId.Value != Proof.ChannelId || value.SessionId.Value != Proof.SessionId) Proof.Invalidate();
                    _current = value;
                }
            }
        }
        internal Func<ObsPublishStatusSnapshot>? ReadSnapshot;
        internal TaskCompletionSource<ObsPublishChannelResult> Completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Func<Task<ObsPublishChannelResult>>? ReadCompletion;
        public Task<ObsPublishChannelResult> Completion => ReadCompletion is { } read ? read() : Completed.Task;
        public ObsPublishStatusSnapshot Snapshot => ReadSnapshot is { } read ? read() : Current;
        public virtual void Start(CancellationToken cancellationToken) => Current = Current with { Revision = 2, State = ChannelSessionState.Negotiating, ListenerReady = true };
        public virtual ValueTask DisposeAsync()
        {
            Completed.TrySetResult(new(ObsPublishChannelEndReason.HostCancelled, 0, 0, 0, 0,
                PcpRelayDrainOutcome.NotRequired, false, true, null, null));
            return ValueTask.CompletedTask;
        }
    }

    internal sealed class Owner : UnsupportedOwner, INovaObsPublishingGenerationRuntime
    {
        public NovaObsPublishingGenerationProof GenerationProof => Proof;
        internal Action? BeforeFinish;
        internal Action? BeforeDispose;
        internal bool AutoFinish;
        internal bool BlockStart;
        internal bool BlockDispose;
        internal bool ThrowDispose;
        internal bool HoldCompletion;
        internal bool IgnoreFinishCancellation;
        internal bool ObsEosWins;
        internal ObsPublishLocalTerminationOutcome Outcome = ObsPublishLocalTerminationOutcome.LocalCompleted;
        internal int Starts, Finishes, Disposals;
        internal TimeSpan FinishTimeout;
        internal TaskCompletionSource StartEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource FinishEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource DisposeEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseDispose = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource DisposeFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<ObsPublishLocalTerminationResult> _finish = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void Start(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Starts);
            StartEntered.TrySetResult();
            if (BlockStart) ReleaseStart.Task.GetAwaiter().GetResult();
            base.Start(cancellationToken);
        }

        public Task<ObsPublishLocalTerminationResult> RequestLocalTerminationAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            BeforeFinish?.Invoke();
            Interlocked.Increment(ref Finishes);
            FinishTimeout = timeout;
            FinishEntered.TrySetResult();
            if (ObsEosWins)
            {
                Current = Current with { Revision = 3, State = ChannelSessionState.Stopped, EndReason = ObsPublishChannelEndReason.PublisherStopped };
                Completed.TrySetResult(new(ObsPublishChannelEndReason.PublisherStopped, 1, 10, 1, 0,
                    PcpRelayDrainOutcome.Completed, true, true, new(ObsRtmpIngestEndReason.PublisherStopped, 1, 10), null));
                _finish.TrySetResult(Local(ObsPublishLocalTerminationOutcome.Failed) with { Phase = ObsPublishLocalTerminationPhase.Admission, Cleanup = null });
            }
            if (AutoFinish) End(Local(Outcome));
            return _finish.Task; // Deliberately independent evidence/cleanup; owner may be uncooperative.
        }

        internal ObsPublishLocalTerminationResult Local(ObsPublishLocalTerminationOutcome outcome = ObsPublishLocalTerminationOutcome.LocalCompleted) =>
            new(Current.ChannelId, Current.SessionId, outcome, ObsPublishLocalTerminationPhase.Finished, false, true, true, true,
                new(ObsRtmpQuiescenceOutcome.Completed, new(ObsRtmpIngestEndReason.Quiesced, 1, 10)),
                null, null, null, null, ObsPublishWriterEvidence.NotObserved, new(true, true, true, true, false, true, true));

        internal void End(ObsPublishLocalTerminationResult local, ObsPublishLocalTerminationResult? attached = null, bool shutdown = true)
        {
            var reason = local.Outcome == ObsPublishLocalTerminationOutcome.LocalCompleted ? ObsPublishChannelEndReason.LocalTerminated :
                local.Outcome == ObsPublishLocalTerminationOutcome.Cancelled ? ObsPublishChannelEndReason.HostCancelled : ObsPublishChannelEndReason.InternalFailure;
            Current = Current with { Revision = Current.Revision + 1, State = ChannelSessionState.Stopped, EndReason = reason };
            _finish.TrySetResult(local);
            if (!HoldCompletion) Completed.TrySetResult(new(reason, 1, 10, 1, 0, PcpRelayDrainOutcome.Completed, true, shutdown,
                local.Ingress?.FinalResult, null)
            { LocalTermination = attached ?? local });
        }

        public override async ValueTask DisposeAsync()
        {
            BeforeDispose?.Invoke();
            Interlocked.Increment(ref Disposals);
            DisposeEntered.TrySetResult();
            if (BlockDispose) await ReleaseDispose.Task;
            if (ThrowDispose) throw new InvalidOperationException("SECRET cleanup");
            if (!Completed.Task.IsCompleted && !HoldCompletion && !IgnoreFinishCancellation)
            {
                if (Finishes > 0) End(Local(ObsPublishLocalTerminationOutcome.Cancelled));
                else await base.DisposeAsync();
            }
            DisposeFinished.TrySetResult();
        }
    }
}
