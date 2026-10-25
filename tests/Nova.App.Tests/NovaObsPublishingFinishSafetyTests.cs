using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Network.Rtmp;
using PeerCast.Nova.Network.Tests;
using Owner = PeerCast.Nova.App.Tests.NovaObsPublishingFinishTests.Owner;

namespace PeerCast.Nova.App.Tests;

public sealed class NovaObsPublishingFinishSafetyTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task DisposeFailedAsync(NovaObsPublishingLifecycleController controller) =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.DisposeAsync().AsTask().WaitAsync(Limit));

    [Theory]
    [InlineData(nameof(PublishingFinishPoint.BeforeNetwork))]
    [InlineData(nameof(PublishingFinishPoint.OwnerObserved))]
    [InlineData(nameof(PublishingFinishPoint.CleanupObserved))]
    [InlineData(nameof(PublishingFinishPoint.BeforeCommit))]
    public async Task SilentMonotonicExpiryNeverCommitsSuccess(string stageName)
    {
        var stage = Enum.Parse<PublishingFinishPoint>(stageName);
        var clock = new SilentClock(); var owner = new Owner { AutoFinish = true };
        var controller = new NovaObsPublishingLifecycleController(owner)
        {
            FinishTimeProvider = clock,
            FinishObserver = point => { if (point == stage) clock.Advance(TimeSpan.FromMinutes(2)); return Task.CompletedTask; },
        };
        controller.RequestStart();
        var result = await controller.RequestFinishAsync(Limit).WaitAsync(Limit);
        Assert.Equal(NovaObsPublishingCompletionKind.TimedOut, result.Kind);
        Assert.True(result.FinishDeadlineExpired);
        Assert.True(clock.CreatedTimers > 0);
        Assert.False(result.LocalCompletedProven);
        Assert.Equal(NovaObsPublishingLifecycleFailure.FinishTimedOut, controller.Read().LifecycleFailure);
        if (!result.CleanupCompleted) await DisposeFailedAsync(controller); else await controller.DisposeAsync();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CommitFenceRejectsCancellationStopAndDispose(int operation)
    {
        var entered = Signal(); var release = Signal();
        var owner = new Owner { AutoFinish = true };
        var controller = new NovaObsPublishingLifecycleController(owner)
        {
            FinishObserver = async point => { if (point == PublishingFinishPoint.BeforeCommit) { entered.TrySetResult(); await release.Task; } },
        };
        controller.RequestStart();
        using var cancellation = new CancellationTokenSource();
        var finish = controller.RequestFinishAsync(Limit, cancellation.Token);
        await entered.Task.WaitAsync(Limit);
        Task? stop = null;
        if (operation == 0) cancellation.Cancel();
        if (operation == 1) stop = controller.RequestStopAsync();
        if (operation == 2) stop = controller.DisposeAsync().AsTask();
        release.TrySetResult();
        var result = await finish.WaitAsync(Limit);
        Assert.Equal(NovaObsPublishingCompletionKind.Cancelled, result.Kind);
        Assert.False(result.LocalCompletedProven);
        Assert.False(result.NormalEosProven);
        if (stop is not null) await stop.WaitAsync(Limit);
        await controller.DisposeAsync();
        Assert.Equal(1, owner.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SynchronousExternalPrefixCannotBlockFinishTerminal(bool cleanup)
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var owner = new Owner { AutoFinish = cleanup };
        Action blocked = () => { entered.Set(); if (!release.Wait(Limit)) throw new TimeoutException(); };
        if (cleanup) owner.BeforeDispose = blocked; else owner.BeforeFinish = blocked;
        var controller = new NovaObsPublishingLifecycleController(owner); controller.RequestStart();
        try
        {
            var finish = controller.RequestFinishAsync(TimeSpan.FromMilliseconds(200));
            Assert.True(entered.Wait(Limit));
            var result = await finish.WaitAsync(Limit);
            Assert.Equal(NovaObsPublishingCompletionKind.TimedOut, result.Kind);
            Assert.False(result.CleanupCompleted);
            Assert.False(result.LocalCompletedProven);
            Assert.False(release.IsSet);
            release.Set();
            await controller.FinishWorker!.WaitAsync(Limit);
            await DisposeFailedAsync(controller);
        }
        finally { release.Set(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateCleanupSuccessOrFailureCannotChangeTerminalOrRead(bool fault)
    {
        var owner = new Owner { AutoFinish = true, BlockDispose = true, ThrowDispose = fault };
        var controller = new NovaObsPublishingLifecycleController(owner); controller.RequestStart();
        var result = await controller.RequestFinishAsync(TimeSpan.FromMilliseconds(150)).WaitAsync(Limit);
        Assert.Equal(NovaObsPublishingCompletionKind.TimedOut, result.Kind);
        var snapshot = controller.Read();
        owner.ReleaseDispose.TrySetResult();
        Assert.Equal(!fault, await controller.CleanupTask!.WaitAsync(Limit));
        Assert.Same(result, await controller.Completion);
        Assert.Equal(snapshot, controller.Read());
        if (fault) Assert.True(controller.LateFaultCount > 0);
        Assert.False(result.CleanupCompleted);
        await DisposeFailedAsync(controller);
    }

    [Theory]
    [InlineData(0)] // pending after Dispose
    [InlineData(1)] // faulted after Dispose
    [InlineData(2)] // null after Dispose
    [InlineData(3)] // synchronous getter fault
    [InlineData(4)] // cancelled after Dispose
    public async Task DisposeSuccessRequiresActualPostDisposeCompletion(int broken)
    {
        var owner = new Owner { AutoFinish = true };
        var pending = new TaskCompletionSource<ObsPublishChannelResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        owner.ReadCompletion = () =>
        {
            if (Interlocked.Increment(ref reads) == 1) return owner.Completed.Task;
            return broken switch
            {
                0 => pending.Task,
                1 => Task.FromException<ObsPublishChannelResult>(new IOException("private getter task")),
                2 => null!,
                3 => throw new IOException("private getter"),
                _ => Task.FromCanceled<ObsPublishChannelResult>(new CancellationToken(true)),
            };
        };
        var controller = new NovaObsPublishingLifecycleController(owner); controller.RequestStart();
        var result = await controller.RequestFinishAsync(Limit).WaitAsync(Limit);
        Assert.False(result.CleanupCompleted);
        Assert.False(result.LocalCompletedProven);
        Assert.Equal(NovaObsPublishingLifecycleState.Failed, result.State);
        await DisposeFailedAsync(controller);
        pending.TrySetResult(await owner.Completed.Task);
        Assert.Same(result, await controller.Completion);
    }

    [Fact]
    public async Task BlockedCompletionGetterAndLateFaultStayOwned()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var owner = new Owner { AutoFinish = true }; var reads = 0;
        owner.ReadCompletion = () =>
        {
            if (Interlocked.Increment(ref reads) == 1) return owner.Completed.Task;
            entered.Set(); if (!release.Wait(Limit)) throw new TimeoutException();
            throw new IOException("late private getter");
        };
        var controller = new NovaObsPublishingLifecycleController(owner); controller.RequestStart();
        try
        {
            var finish = controller.RequestFinishAsync(TimeSpan.FromMilliseconds(200));
            Assert.True(entered.Wait(Limit));
            var result = await finish.WaitAsync(Limit); var snapshot = controller.Read();
            Assert.Equal(NovaObsPublishingCompletionKind.TimedOut, result.Kind);
            release.Set();
            Assert.False(await controller.CleanupTask!.WaitAsync(Limit));
            Assert.True(controller.LateFaultCount > 0);
            Assert.Equal(snapshot, controller.Read());
            await DisposeFailedAsync(controller);
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task LateRuntimeFaultIsObservedAfterTimeout()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var owner = new Owner { BeforeFinish = () => { entered.Set(); if (!release.Wait(Limit)) throw new TimeoutException(); throw new IOException("late private runtime"); } };
        var controller = new NovaObsPublishingLifecycleController(owner); controller.RequestStart();
        try
        {
            var finish = controller.RequestFinishAsync(TimeSpan.FromMilliseconds(150));
            Assert.True(entered.Wait(Limit));
            var result = await finish.WaitAsync(Limit);
            release.Set();
            using var deadline = new CancellationTokenSource(Limit);
            while (controller.LateFaultCount == 0) await Task.Delay(5, deadline.Token);
            Assert.Same(result, await controller.Completion);
            await DisposeFailedAsync(controller);
        }
        finally { release.Set(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EqualCopyAndIdentityChangedDuringDisposeFailClosed(bool changedIdentity)
    {
        var owner = new Owner();
        if (changedIdentity) owner.BeforeDispose = () => owner.Current = owner.Current with { SessionId = new ChannelSessionId(Guid.NewGuid()) };
        var controller = new NovaObsPublishingLifecycleController(owner); controller.RequestStart();
        var finish = controller.RequestFinishAsync(Limit);
        await owner.FinishEntered.Task.WaitAsync(Limit);
        var local = owner.Local(); owner.End(local, changedIdentity ? local : local with { });
        var result = await finish.WaitAsync(Limit);
        Assert.Equal(NovaObsPublishingLifecycleFailure.IdentityUnproven, result.Failure);
        Assert.False(result.IdentityMatched);
        Assert.False(result.LocalCompletedProven);
        if (result.CleanupCompleted) await controller.DisposeAsync(); else await DisposeFailedAsync(controller);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task KnownHttpRecordingFailuresCannotBecomeLocalProof(int failure)
    {
        var owner = new Owner(); var controller = new NovaObsPublishingLifecycleController(owner); controller.RequestStart();
        var finish = controller.RequestFinishAsync(Limit); await owner.FinishEntered.Task.WaitAsync(Limit);
        var evidence = failure switch
        {
            1 => new PcpRecordingSourceEvidence { AcceptedTerminalFault = true },
            2 => new PcpRecordingSourceEvidence { RejectedLeaseCleanupFailureCount = 1 },
            _ => new PcpRecordingSourceEvidence { AcquiredLeaseCount = 1, CleanupFailedLeaseCount = 1, RegistrationCleanupFailedLeaseCount = 1 },
        };
        var local = failure == 0 ? owner.Local() with { HttpSourceCleanupFailed = true } : owner.Local() with { RecordingFinalEvidence = evidence };
        owner.End(local);
        var result = await finish.WaitAsync(Limit);
        Assert.Equal(NovaObsPublishingLifecycleFailure.FinishFailed, result.Failure);
        Assert.False(result.LocalCompletedProven);
        Assert.Same(local, result.LocalTermination);
        await controller.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopAndDisposeBeforeFinishNeverDispatchIt(bool dispose)
    {
        var owner = new Owner(); var controller = new NovaObsPublishingLifecycleController(owner); controller.RequestStart();
        var stopped = dispose ? controller.DisposeAsync().AsTask() : controller.RequestStopAsync();
        await controller.RequestFinishAsync(Limit).WaitAsync(Limit);
        await stopped.WaitAsync(Limit);
        Assert.Equal(0, owner.Finishes);
        Assert.False((await controller.Completion).FinishRequested);
        await controller.DisposeAsync();
    }

    [Fact]
    public async Task QueuedCommandBeforeLifecycleAdmissionDoesNotAdmitFinish()
    {
        var owner = new Owner(); await using var lifecycle = new NovaObsPublishingLifecycleController(owner);
        var wrapper = new QueuedStart(lifecycle); var commands = new NovaGuiNextPublishingCommands(wrapper);
        var start = commands.RequestStartAsync(); await wrapper.Entered.Task.WaitAsync(Limit);
        var rejected = await lifecycle.RequestFinishAsync(Limit);
        Assert.Equal(NovaObsPublishingLifecycleFailure.FinishUnavailable, rejected.Failure);
        Assert.Equal(0, owner.Starts); Assert.Equal(0, owner.Finishes);
        wrapper.Release.TrySetResult(); await start.WaitAsync(Limit); await commands.DisposeAsync();
    }

    [Fact]
    public async Task ReplacementFactoryFenceRejectsFinish()
    {
        var first = new Owner { AutoFinish = true }; var second = new Owner();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); var creates = 0;
        var factory = new Factory(() =>
        {
            if (Interlocked.Increment(ref creates) == 1) return first;
            entered.Set(); if (!release.Wait(Limit)) throw new TimeoutException(); return second;
        });
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        await composition.RequestStartAsync(); await composition.RequestFinishAsync(Limit);
        var replacement = composition.RequestStartAsync();
        try
        {
            Assert.True(entered.Wait(Limit));
            Assert.Equal(NovaObsPublishingLifecycleFailure.FinishUnavailable, (await composition.RequestFinishAsync(Limit)).Failure);
            Assert.Equal(0, second.Finishes);
        }
        finally { release.Set(); await replacement.WaitAsync(Limit); }
    }

    [Fact]
    public async Task ReadObservationHasNoFinishSideEffects()
    {
        var owner = new Owner(); await using var controller = new NovaObsPublishingLifecycleController(owner);
        _ = controller.Read(); _ = controller.Read();
        Assert.Equal(0, owner.Starts); Assert.Equal(0, owner.Finishes);
        controller.RequestStart(); _ = controller.Read(); _ = controller.Read();
        Assert.Equal(0, owner.Finishes); Assert.Equal(0, owner.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualOwnerLoopbackPreservesEosAndFinishAdmissionOrders(bool finishFirst)
    {
        var entered = Signal(); var release = Signal();
        var host = new ObsPublishChannelHost(new() { Ingest = new() { Port = 0 } })
        {
            TerminationObserver = async point =>
            {
                if (finishFirst && point == ObsPublishTerminationPoint.BeforeEndSelection) { entered.TrySetResult(); await release.Task; }
            },
        };
        var owner = NovaObsPublishingRuntime.ForOwner(host);
        await using var controller = new NovaObsPublishingLifecycleController(owner)
        {
            FinishObserver = async point =>
            {
                if (!finishFirst && point == PublishingFinishPoint.BeforeNetwork) { entered.TrySetResult(); await release.Task; }
            },
        };
        controller.RequestStart();
        using var publisher = new ObsRtmpTestClient(); await publisher.ConnectAsync(host.IngestEndPoint); await publisher.SetChunkBytesAsync(4096); await publisher.PublishAsync();
        await publisher.SendAsync(8, 1, 0, [0xAF, 0, 0x12, 0x10], id: 4);
        await publisher.SendAsync(9, 1, 0, [0x17, 0, 0, 0, 0, 1, 0x42, 0, 0x1E, 0xFF, 0xE1, 0, 2, 0x67, 0x42, 1, 0, 2, 0x68, 0xCE], id: 4);
        Assert.True(await host.HeadAvailable.WaitAsync(Limit));
        var finish = controller.RequestFinishAsync(Limit);
        try
        {
            await entered.Task.WaitAsync(Limit);
            await publisher.SendCommandAsync(0, "FCUnpublish", 6.0, null, "nova");
            Assert.Equal(ObsRtmpIngestEndReason.PublisherStopped, (await host.IngressCompletion.WaitAsync(Limit)).EndReason);
            if (!finishFirst) await host.Completion.WaitAsync(Limit);
            release.TrySetResult();
            var result = await finish.WaitAsync(Limit);
            Assert.Equal(finishFirst ? NovaObsPublishingCompletionKind.LocalCompleted : NovaObsPublishingCompletionKind.ObsNormalEos, result.Kind);
            Assert.Equal(finishFirst, result.LocalCompletedProven);
            Assert.Equal(!finishFirst, result.NormalEosProven);
            Assert.True(result.CleanupCompleted);
            if (finishFirst) Assert.Same(result.LocalTermination, (await host.Completion).LocalTermination);
            else Assert.Null((await host.Completion).LocalTermination);
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task ActualOwnerHttpCleanupFaultIsNotApplicationSuccess()
    {
        var host = new ObsPublishChannelHost(new() { Ingest = new() { Port = 0 } });
        var owner = NovaObsPublishingRuntime.ForOwner(host); var controller = new NovaObsPublishingLifecycleController(owner); controller.RequestStart();
        host.HttpSource!.AfterSourceCleanup = () => Task.FromException(new IOException("private HTTP cleanup fault"));
        var result = await controller.RequestFinishAsync(Limit).WaitAsync(Limit);
        Assert.False(result.LocalCompletedProven); Assert.False(result.CleanupCompleted);
        Assert.True(result.LocalTermination!.HttpSourceCleanupFailed);
        await DisposeFailedAsync(controller);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IdentityFailureKeepsActualCleanupSeparateAndBlocksReplacement(bool cleanupFault)
    {
        var owner = new Owner { AutoFinish = true, ThrowDispose = cleanupFault };
        owner.BeforeDispose = () => owner.Current = owner.Current with { SessionId = new ChannelSessionId(Guid.NewGuid()) };
        var creates = 0;
        var composition = NovaGuiNextPublishingComposition.Create(new Factory(() => { creates++; return owner; }));
        await composition.RequestStartAsync();
        var controller = Assert.IsType<NovaObsPublishingLifecycleController>(composition.Lifecycle);
        var result = await composition.RequestFinishAsync(Limit).WaitAsync(Limit);
        Assert.Equal(NovaObsPublishingLifecycleFailure.IdentityUnproven, result.Failure);
        Assert.False(result.IdentityMatched);
        Assert.False(result.CleanupCompleted);
        Assert.False(result.LocalCompletedProven);
        Assert.Equal(!cleanupFault, await controller.CleanupTask!.WaitAsync(Limit));
        Assert.False(composition.ReadCommands().CanStart);
        await composition.RequestStartAsync();
        Assert.Equal(1, creates);
        Assert.Equal(1, owner.Disposals);
        await Assert.ThrowsAsync<InvalidOperationException>(() => composition.DisposeAsync().AsTask().WaitAsync(Limit));
        Assert.Same(result, await controller.Completion);
        Assert.Equal(NovaObsPublishingLifecycleFailure.IdentityUnproven, controller.Read().LifecycleFailure);
        Assert.Equal(1, owner.Disposals);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AuditCompositionFinishDoesNotReadStatusUnderItsGate(bool deadlineFirst)
    {
        var owner = new Owner(); var clock = new ControlledClock();
        var composition = NovaGuiNextPublishingComposition.Create(new Factory(() => owner), clock);
        await composition.RequestStartAsync();
        var controller = Assert.IsType<NovaObsPublishingLifecycleController>(composition.Lifecycle);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var returned = Signal();
        owner.ReadSnapshot = () =>
        {
            entered.Set();
            try { if (!release.Wait(Limit)) throw new TimeoutException(); return owner.Current; }
            finally { returned.TrySetResult(); }
        };
        try
        {
            // The direct API must return while its external Snapshot is still blocked.
            var finish = composition.RequestFinishAsync(Limit);
            Assert.True(entered.Wait(Limit));
            Assert.False(returned.Task.IsCompleted);
            Assert.Same(finish, composition.RequestFinishAsync(Limit));
            composition.CloseAdmission();
            Assert.False(release.IsSet);
            Assert.False(returned.Task.IsCompleted);
            Assert.False(finish.IsCompleted);
            Assert.False(owner.DisposeEntered.Task.IsCompleted);
            if (deadlineFirst)
            {
                // Deadline is established BEFORE Dispose admission. Snapshot cannot
                // settle, and there is no wall-clock delay or scheduling assumption.
                clock.AdvanceAndFire(Limit + TimeSpan.FromTicks(1));
            }
            var disposal = composition.DisposeAsync().AsTask();
            // Actual owned Dispose entry proves shutdown acceptance is not blocked
            // by the external Snapshot. Its task remains shared with fallback Stop.
            await owner.DisposeEntered.Task.WaitAsync(Limit);
            if (!deadlineFirst)
            {
                // Clock remains before deadline; observe actual Dispose termination
                // before releasing Snapshot and allowing the Stop fence to run.
                Assert.False(finish.IsCompleted);
                await owner.DisposeFinished.Task.WaitAsync(Limit);
                release.Set();
            }
            var result = await finish.WaitAsync(Limit);
            Assert.Equal(deadlineFirst ? NovaObsPublishingCompletionKind.TimedOut : NovaObsPublishingCompletionKind.Cancelled, result.Kind);
            Assert.Equal(deadlineFirst, result.FinishDeadlineExpired);
            Assert.False(result.CleanupCompleted);
            Assert.False(result.LocalCompletedProven);
            Assert.False(result.NormalEosProven);
            Assert.True(await controller.CleanupTask!.WaitAsync(Limit));
            await Assert.ThrowsAsync<InvalidOperationException>(() => disposal.WaitAsync(Limit));
            Assert.Equal(0, owner.Finishes);
            Assert.Equal(1, owner.Starts);
            Assert.Equal(1, owner.Disposals);
            release.Set();
            await returned.Task.WaitAsync(Limit);
            await controller.FinishWorker!.WaitAsync(Limit);
            Assert.Same(result, await controller.Completion);
        }
        finally { owner.ReadSnapshot = null; release.Set(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuditProofInvalidationAfterFinalReadDefeatsCommit(bool unavailable)
    {
        var owner = new Owner { AutoFinish = true }; var entered = Signal(); var release = Signal();
        var controller = new NovaObsPublishingLifecycleController(owner)
        {
            FinishObserver = async point => { if (point == PublishingFinishPoint.BeforeCommit) { entered.TrySetResult(); await release.Task; } },
        };
        controller.RequestStart();
        var finish = controller.RequestFinishAsync(Limit); await entered.Task.WaitAsync(Limit);
        var old = owner.Current;
        if (unavailable)
        {
            owner.ReadSnapshot = () => throw new IOException("unavailable status");
            Assert.Equal(NovaObsPublishingAvailability.Unavailable, controller.Read().Availability);
            owner.ReadSnapshot = null; // Recovery of a snapshot cannot revalidate the proof.
        }
        else
        {
            owner.Current = owner.Current with { SessionId = new ChannelSessionId(Guid.NewGuid()) };
            owner.ReadSnapshot = () => old; // Even another matching stale snapshot cannot prove stability.
        }
        release.TrySetResult();
        var result = await finish.WaitAsync(Limit);
        Assert.Equal(NovaObsPublishingLifecycleFailure.IdentityUnproven, result.Failure);
        Assert.False(result.IdentityMatched);
        Assert.False(result.CleanupCompleted);
        Assert.False(result.LocalCompletedProven);
        Assert.True(await controller.CleanupTask!.WaitAsync(Limit));
        Assert.False(controller.HasCurrentGenerationProof);
        await DisposeFailedAsync(controller);
        Assert.Equal(1, owner.Disposals);
    }

    [Fact]
    public async Task AuditUnavailableObservationLatchesRestartClosedAfterTerminal()
    {
        var owner = new Owner { AutoFinish = true }; var creates = 0;
        var composition = NovaGuiNextPublishingComposition.Create(new Factory(() => { creates++; return owner; }));
        await composition.RequestStartAsync();
        var result = await composition.RequestFinishAsync(Limit);
        Assert.True(result.LocalCompletedProven); Assert.True(result.CleanupCompleted);
        owner.ReadSnapshot = () => throw new IOException("status lost");
        Assert.Equal(NovaObsPublishingAvailability.Unavailable, composition.Read().Availability);
        owner.ReadSnapshot = null;
        Assert.False(composition.ReadCommands().CanStart);
        await composition.RequestStartAsync();
        Assert.Equal(1, creates);
        Assert.Same(result, await composition.Lifecycle.Completion);
        await composition.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuditPostDisposeDifferentCompletionIsRetainedAndFaultObserved(bool alreadyFaulted)
    {
        var owner = new Owner { AutoFinish = true }; var reads = 0;
        var other = new TaskCompletionSource<ObsPublishChannelResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (alreadyFaulted) other.TrySetException(new IOException("other task fault"));
        owner.ReadCompletion = () => Interlocked.Increment(ref reads) == 1 ? owner.Completed.Task : other.Task;
        var controller = new NovaObsPublishingLifecycleController(owner); controller.RequestStart();
        var result = await controller.RequestFinishAsync(Limit).WaitAsync(Limit);
        Assert.Same(other.Task, controller.PostDisposeCompletion);
        Assert.False(result.CleanupCompleted); Assert.False(result.LocalCompletedProven);
        var snapshot = controller.Read();
        if (!alreadyFaulted) other.TrySetException(new IOException("late other task fault"));
        using var deadline = new CancellationTokenSource(Limit);
        while (controller.LateFaultCount == 0) await Task.Delay(5, deadline.Token);
        Assert.Same(result, await controller.Completion);
        Assert.Equal(snapshot, controller.Read());
        await DisposeFailedAsync(controller);
        Assert.Equal(1, owner.Disposals);
    }

    [Fact]
    public async Task AuditRuntimeWithoutStableGenerationCapabilityFailsClosed()
    {
        var owner = new Owner { AutoFinish = true };
        var controller = new NovaObsPublishingLifecycleController(new UnprovenRuntime(owner));
        controller.RequestStart();
        var result = await controller.RequestFinishAsync(Limit).WaitAsync(Limit);
        Assert.Equal(NovaObsPublishingLifecycleFailure.IdentityUnproven, result.Failure);
        Assert.False(result.CleanupCompleted); Assert.False(result.LocalCompletedProven);
        Assert.Equal(0, owner.Finishes);
        await DisposeFailedAsync(controller);
    }

    private sealed class UnprovenRuntime(Owner owner) : INovaObsPublishingFinishRuntime
    {
        public ObsPublishStatusSnapshot Snapshot => owner.Snapshot;
        public Task<ObsPublishChannelResult> Completion => owner.Completion;
        public void Start(CancellationToken cancellationToken) => owner.Start(cancellationToken);
        public Task<ObsPublishLocalTerminationResult> RequestLocalTerminationAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            owner.RequestLocalTerminationAsync(timeout, cancellationToken);
        public ValueTask DisposeAsync() => owner.DisposeAsync();
    }

    private sealed class Factory(Func<INovaObsPublishingRuntime> create) : INovaObsPublishingRuntimeFactory
    { public INovaObsPublishingRuntime Create() => create(); }

    private sealed class QueuedStart(INovaObsPublishingLifecycleController inner) : INovaObsPublishingLifecycleController
    {
        internal TaskCompletionSource Entered { get; } = Signal();
        internal TaskCompletionSource Release { get; } = Signal();
        public NovaObsPublishingReadOnlySnapshot Read() => inner.Read();
        public Task<NovaObsPublishingLifecycleResult> Completion => inner.Completion;
        public NovaObsPublishingStartResult RequestStart(CancellationToken cancellationToken = default)
        { Entered.TrySetResult(); Release.Task.GetAwaiter().GetResult(); return inner.RequestStart(cancellationToken); }
        public Task<NovaObsPublishingLifecycleResult> RequestStopAsync() => inner.RequestStopAsync();
        public Task<NovaObsPublishingLifecycleResult> RequestFinishAsync(TimeSpan timeout, CancellationToken cancellationToken = default) => inner.RequestFinishAsync(timeout, cancellationToken);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class ControlledClock : TimeProvider
    {
        private long _ticks;
        private Action? _fire;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public void AdvanceAndFire(TimeSpan elapsed)
        {
            Interlocked.Add(ref _ticks, elapsed.Ticks);
            (_fire ?? throw new InvalidOperationException("No admitted deadline."))();
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _fire = () => callback(state);
            return new ControlledTimer();
        }
        private sealed class ControlledTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class SilentClock : TimeProvider
    {
        private long _ticks;
        public int CreatedTimers { get; private set; } // Timer callbacks are deliberately never invoked.
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public void Advance(TimeSpan time) => Interlocked.Add(ref _ticks, time.Ticks);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) { CreatedTimers++; return new SilentTimer(); }
        private sealed class SilentTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
