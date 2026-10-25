using System.Text.Json;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Network.Rtmp;
using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App.Tests;

public sealed class NovaObsPublishingLifecycleControllerTests
{
    [Fact]
    public async Task CompositionAndReadOnlyRefreshNeverStartAndApplicationShutdownDisposesOnce()
    {
        var factory = new Factory();
        var composition = NovaGuiNextPublishingComposition.Create(factory);
        Assert.Equal(0, factory.Owner.Reads);
        await using var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        tabs.BindPublishingStatus(composition.Status);
        tabs.SelectedTab = GenesisGuiNextTab.Publishing;
        tabs.RefreshPublishingStatus();
        tabs.RefreshPublishingStatus();
        Assert.Equal(0, factory.Owner.Starts);
        Assert.Equal(NovaObsPublishingLifecycleState.NotStarted, composition.Status.Read().LifecycleState);
        var identity = composition.Status.Read();
        await Task.WhenAll(composition.DisposeAsync().AsTask(), composition.DisposeAsync().AsTask());
        var terminal = await composition.Lifecycle.Completion;
        Assert.Equal(NovaObsPublishingCompletionKind.Cancelled, terminal.Kind);
        Assert.True(terminal.CleanupCompleted);
        Assert.False(terminal.NormalEosProven);
        Assert.Equal(1, factory.Creates);
        Assert.Equal(1, factory.Owner.Disposals);
        Assert.Equal(identity.SessionId, composition.Status.Read().SessionId);
        Assert.Equal(NovaObsPublishingStartResult.ClosedGeneration, composition.Lifecycle.RequestStart());
    }

    [Fact]
    public async Task ExplicitStartUsesExistingRuntimeAndDuplicateStartDoesNotReplaceGeneration()
    {
        var owner = new Owner();
        await using var controller = new NovaObsPublishingLifecycleController(owner);
        var identity = controller.Read();
        Assert.Equal(NovaObsPublishingStartResult.Accepted, controller.RequestStart());
        Assert.Equal(NovaObsPublishingStartResult.AlreadyStarted, controller.RequestStart());
        var running = controller.Read();
        Assert.Equal(NovaObsPublishingLifecycleState.Running, running.LifecycleState);
        Assert.Equal("OBS待機中", running.StateText);
        Assert.Equal(identity.ChannelId, running.ChannelId);
        Assert.Equal(identity.SessionId, running.SessionId);
        Assert.False(controller.Completion.IsCompleted);
        var stop = controller.RequestStopAsync();
        Assert.Same(stop, controller.RequestStopAsync());
        var terminal = await stop.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(NovaObsPublishingCompletionKind.Cancelled, terminal.Kind);
        Assert.Equal(ObsPublishChannelEndReason.HostCancelled, terminal.EndReason);
        Assert.False(terminal.NormalEosProven);
        Assert.Equal(1, owner.Starts);
        Assert.Equal(1, owner.Disposals);
        Assert.Equal(NovaObsPublishingStartResult.ClosedGeneration, controller.RequestStart());
    }

    [Fact]
    public async Task ExistingProductionCompletionBoundaryWorksWithoutAnObsConnection()
    {
        var runtime = new NovaObsPublishingRuntime(new() { Ingest = new() { Port = 0 } });
        await using var controller = new NovaObsPublishingLifecycleController(runtime);
        Assert.False(runtime.Snapshot.ListenerReady);
        Assert.Equal(NovaObsPublishingStartResult.Accepted, controller.RequestStart());
        Assert.True(runtime.Snapshot.ListenerReady); // Managed loopback listener only; no publisher.
        var terminal = await controller.RequestStopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        var foundation = await runtime.Completion;
        Assert.Equal(foundation.EndReason, terminal.EndReason);
        Assert.Equal(ObsPublishChannelEndReason.HostCancelled, terminal.EndReason);
        Assert.Equal(NovaObsPublishingCompletionKind.Cancelled, terminal.Kind);
        Assert.True(terminal.CleanupCompleted);
        Assert.False(terminal.NormalEosProven);
        Assert.False(runtime.Snapshot.ListenerReady);
        Assert.False(runtime.Snapshot.HeadAvailable);
    }

    [Fact]
    public async Task StopDuringStartWaitsForStartBeforeCleanupAndRejectsFurtherStarts()
    {
        var owner = new Owner { BlockStart = true };
        await using var controller = new NovaObsPublishingLifecycleController(owner);
        var start = Task.Run(() => controller.RequestStart());
        await owner.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(NovaObsPublishingLifecycleState.Starting, controller.Read().LifecycleState);
        Assert.Equal(NovaObsPublishingStartResult.AlreadyStarted, controller.RequestStart());
        var stop = controller.RequestStopAsync();
        Assert.Equal(NovaObsPublishingLifecycleState.Stopping, controller.Read().LifecycleState);
        Assert.Equal(0, owner.Disposals);
        Assert.False(stop.IsCompleted);
        Assert.Equal(NovaObsPublishingStartResult.ClosedGeneration, controller.RequestStart());
        owner.ReleaseStart.TrySetResult();
        Assert.Equal(NovaObsPublishingStartResult.Accepted, await start.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(NovaObsPublishingCompletionKind.Cancelled, (await stop.WaitAsync(TimeSpan.FromSeconds(5))).Kind);
        Assert.Equal(1, owner.Starts);
        Assert.Equal(1, owner.Disposals);
    }

    [Fact]
    public async Task ConcurrentStartsAndStopsHaveOneAdmissionAndOneCleanup()
    {
        var owner = new Owner();
        await using var controller = new NovaObsPublishingLifecycleController(owner);
        var starts = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => controller.RequestStart())));
        Assert.Single(starts, value => value == NovaObsPublishingStartResult.Accepted);
        Assert.All(starts.Where(value => value != NovaObsPublishingStartResult.Accepted),
            value => Assert.Equal(NovaObsPublishingStartResult.AlreadyStarted, value));
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => controller.RequestStopAsync()));
        Assert.Equal(1, owner.Starts);
        Assert.Equal(1, owner.Disposals);
    }

    [Fact]
    public async Task RequestCancellationControlsAdmissionAndDoesNotOwnAcceptedLifetime()
    {
        var owner = new Owner();
        await using var controller = new NovaObsPublishingLifecycleController(owner);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Equal(NovaObsPublishingStartResult.CancelledBeforeAdmission, controller.RequestStart(cancelled.Token));
        Assert.Equal(NovaObsPublishingLifecycleState.NotStarted, controller.Read().LifecycleState);
        Assert.Equal(0, owner.Starts);
        using var request = new CancellationTokenSource();
        Assert.Equal(NovaObsPublishingStartResult.Accepted, controller.RequestStart(request.Token));
        request.Cancel();
        Assert.False(owner.StartToken.IsCancellationRequested);
        Assert.Equal(0, owner.Disposals);
        Assert.Equal(NovaObsPublishingLifecycleState.Running, controller.Read().LifecycleState);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SynchronousStartOrCompletionAccessFailureIsSafeAndCleansOnce(bool startThrows)
    {
        var owner = new Owner { ThrowStart = startThrows, ThrowCompletionAccess = !startThrows };
        var controller = new NovaObsPublishingLifecycleController(owner);
        Assert.Equal(NovaObsPublishingStartResult.Failed, controller.RequestStart());
        var terminal = await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(NovaObsPublishingLifecycleFailure.StartFailed, terminal.Failure);
        Assert.Equal(NovaObsPublishingLifecycleState.Failed, terminal.State);
        Assert.Equal(startThrows, terminal.CleanupCompleted);
        if (startThrows) await controller.DisposeAsync();
        else await Assert.ThrowsAsync<InvalidOperationException>(() => controller.DisposeAsync().AsTask());
        Assert.Equal(1, owner.Disposals);
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(controller.Read()));
    }

    [Fact]
    public async Task CompletionExceptionIsObservedAndNeverShownAsRawException()
    {
        var owner = new Owner();
        var controller = new NovaObsPublishingLifecycleController(owner);
        controller.RequestStart();
        owner.FaultCompletion();
        var terminal = await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(NovaObsPublishingLifecycleFailure.CompletionFailed, terminal.Failure);
        Assert.False(terminal.NormalEosProven);
        Assert.False(terminal.CleanupCompleted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.DisposeAsync().AsTask());
        Assert.Equal(1, owner.Disposals);
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(controller.Read()));
    }

    [Fact]
    public async Task NullCompletionResultFailsClosed()
    {
        var owner = new Owner();
        var controller = new NovaObsPublishingLifecycleController(owner);
        controller.RequestStart();
        owner.Completed.TrySetResult(null!);
        var terminal = await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(NovaObsPublishingLifecycleFailure.CompletionFailed, terminal.Failure);
        Assert.False(terminal.NormalEosProven);
        Assert.False(terminal.CleanupCompleted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.DisposeAsync().AsTask());
    }

    [Theory]
    [InlineData(ObsPublishChannelEndReason.StartupFailed)]
    [InlineData(ObsPublishChannelEndReason.ProtocolRejected)]
    [InlineData(ObsPublishChannelEndReason.Disconnected)]
    public async Task FoundationFailureAndDisconnectAreNotNormalEos(ObsPublishChannelEndReason reason)
    {
        var owner = new Owner();
        await using var controller = new NovaObsPublishingLifecycleController(owner);
        controller.RequestStart();
        owner.End(Result(reason));
        var terminal = await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(NovaObsPublishingLifecycleState.Failed, terminal.State);
        Assert.Equal(NovaObsPublishingLifecycleFailure.SourceFailed, terminal.Failure);
        Assert.Equal(reason, terminal.EndReason);
        Assert.False(terminal.NormalEosProven);
        Assert.Equal("配信エラー", controller.Read().StateText);
    }

    [Fact]
    public async Task NormalObsEosRequiresCompletionEvidenceAndPreservesIdentity()
    {
        var owner = new Owner { BlockDispose = true };
        await using var controller = new NovaObsPublishingLifecycleController(owner);
        var identity = controller.Read();
        controller.RequestStart();
        owner.End(Result(ObsPublishChannelEndReason.PublisherStopped));
        await owner.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(NovaObsPublishingLifecycleState.Stopping, controller.Read().LifecycleState);
        Assert.False(controller.Completion.IsCompleted);
        // Stop racing an already-proven OBS result must not rewrite its cause.
        var stop = controller.RequestStopAsync();
        owner.ReleaseDispose.TrySetResult();
        var terminal = await stop.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(terminal.NormalEosProven);
        Assert.True(terminal.CleanupCompleted);
        Assert.Equal(NovaObsPublishingCompletionKind.ObsNormalEos, terminal.Kind);
        Assert.Equal(identity.SessionId, controller.Read().SessionId);
        Assert.Equal("停止（OBS正常EOS）", controller.Read().StateText);
        Assert.Equal(1, owner.Disposals);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public async Task PublisherStoppedWithoutEveryProofIsNotSuccessfulEos(int missing)
    {
        var owner = new Owner();
        var controller = new NovaObsPublishingLifecycleController(owner);
        controller.RequestStart();
        var result = Result(ObsPublishChannelEndReason.PublisherStopped);
        result = missing switch
        {
            0 => result with { ShutdownCompleted = false },
            1 => result with { HttpDrained = false },
            2 => result with { RelayDrain = PcpRelayDrainOutcome.TimedOut },
            3 => result with { Ingest = null },
            4 => result with { Ingest = new(ObsRtmpIngestEndReason.Disconnected, 1, 10) },
            5 => result with { HeadCount = 0 },
            6 => result with { AcceptedPacketCount = 0 },
            7 => result with { AcceptedByteCount = 0 },
            8 => result with { Ingest = new(ObsRtmpIngestEndReason.PublisherStopped, 2, 10) },
            _ => result with { Ingest = new(ObsRtmpIngestEndReason.PublisherStopped, 1, 11) },
        };
        owner.End(result);
        var terminal = await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(NovaObsPublishingLifecycleState.Failed, terminal.State);
        Assert.False(terminal.NormalEosProven);
        if (missing == 0)
        {
            Assert.False(terminal.CleanupCompleted);
            await Assert.ThrowsAsync<InvalidOperationException>(() => controller.DisposeAsync().AsTask());
        }
        else
        {
            Assert.Equal(NovaObsPublishingLifecycleFailure.NormalEosUnproven, terminal.Failure);
            Assert.Equal(NovaObsPublishingCompletionKind.IncompleteEos, terminal.Kind);
            await controller.DisposeAsync();
        }
    }

    [Fact]
    public async Task CleanupFailureReturnsFailureAndLateCompletionCannotRepairIt()
    {
        var owner = new Owner { ThrowDispose = true };
        var controller = new NovaObsPublishingLifecycleController(owner);
        controller.RequestStart();
        var terminal = await controller.RequestStopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(NovaObsPublishingLifecycleFailure.CleanupFailed, terminal.Failure);
        Assert.False(terminal.CleanupCompleted);
        owner.End(Result(ObsPublishChannelEndReason.PublisherStopped));
        Assert.Same(terminal, await controller.Completion);
        Assert.Equal(NovaObsPublishingLifecycleFailure.CleanupFailed, controller.Read().LifecycleFailure);
        Assert.Equal(NovaObsPublishingLifecycleState.Failed, controller.Read().LifecycleState);
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.DisposeAsync().AsTask());
        Assert.Equal(1, owner.Disposals);
    }

    [Fact]
    public async Task LateReceivingCannotOverwriteTerminalLabelOrTone()
    {
        var owner = new Owner();
        await using var controller = new NovaObsPublishingLifecycleController(owner);
        controller.RequestStart();
        await controller.RequestStopAsync();
        owner.Current = owner.Current with { Revision = 100, State = ChannelSessionState.Receiving, HeadAvailable = true, EndReason = null };
        var snapshot = controller.Read();
        Assert.Equal(NovaObsPublishingLifecycleState.Stopped, snapshot.LifecycleState);
        Assert.Equal(NovaObsPublishingAvailability.Stopped, snapshot.Availability);
        Assert.Equal("停止（キャンセル）", snapshot.StateText);
        Assert.Equal(NovaObsPublishingTone.Special, snapshot.Tone);
        Assert.False(controller.RequestStart() == NovaObsPublishingStartResult.Accepted);
    }

    [Fact]
    public async Task MissingOwnerAndMissingCompletionBoundaryFailClosed()
    {
        await using var missing = new NovaObsPublishingLifecycleController(null);
        Assert.Equal(NovaObsPublishingStartResult.Unavailable, missing.RequestStart());
        Assert.Equal(NovaObsPublishingAvailability.Unavailable, missing.Read().Availability);
        Assert.Equal(NovaObsPublishingLifecycleFailure.OwnerUnavailable, (await missing.RequestStopAsync()).Failure);
        var owner = new UnsupportedOwner();
        var unsupported = new NovaObsPublishingLifecycleController(owner);
        Assert.Equal(NovaObsPublishingStartResult.Unavailable, unsupported.RequestStart());
        Assert.Equal(NovaObsPublishingLifecycleFailure.LifecycleUnavailable, unsupported.Read().LifecycleFailure);
        Assert.False((await unsupported.RequestStopAsync()).CleanupCompleted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => unsupported.DisposeAsync().AsTask());
        Assert.Equal(0, owner.Starts);
        Assert.Equal(1, owner.Disposals);
        Assert.Equal(NovaObsPublishingStartResult.ClosedGeneration, unsupported.RequestStart());
    }

    [Fact]
    public async Task SnapshotAndTerminalContainNoEndpointsRoutesOrExceptions()
    {
        var owner = new Owner();
        await using var controller = new NovaObsPublishingLifecycleController(owner);
        controller.RequestStart();
        Assert.DoesNotContain("DO-NOT-DISPLAY", JsonSerializer.Serialize(controller.Read()));
        owner.End(Result(ObsPublishChannelEndReason.ProtocolRejected));
        var terminal = await controller.Completion;
        Assert.DoesNotContain("DO-NOT-DISPLAY", JsonSerializer.Serialize(terminal));
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(controller.Read()));
        Assert.Equal("SourceFailed / ProtocolRejected · 終了結果: Failed · 世代cleanup証拠あり（再開始許可とは別）", GenesisGuiNextPublishingPresentation.ErrorText(controller.Read()));
    }

    [Fact]
    public async Task DisposeWithoutSettledCompletionFailsClosedAndLateEosCannotRepairIt()
    {
        var owner = new Owner { LeaveCompletionPendingOnDispose = true };
        var controller = new NovaObsPublishingLifecycleController(owner);
        Assert.Equal(NovaObsPublishingStartResult.Accepted, controller.RequestStart());
        var stop = controller.RequestStopAsync();
        var terminal = await stop.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(NovaObsPublishingLifecycleFailure.CompletionFailed, terminal.Failure);
        Assert.False(terminal.CleanupCompleted);
        Assert.False(terminal.NormalEosProven);
        Assert.Equal(NovaObsPublishingLifecycleState.Failed, terminal.State);
        Assert.Equal(NovaObsPublishingStartResult.ClosedGeneration, controller.RequestStart());
        owner.End(Result(ObsPublishChannelEndReason.PublisherStopped));
        Assert.Same(terminal, await controller.Completion);
        Assert.Same(stop, controller.RequestStopAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.DisposeAsync().AsTask());
        Assert.Equal(1, owner.Disposals);
    }

    [Fact]
    public async Task PendingOwnedDisposalIsNotReportedAsCleanupSuccess()
    {
        var owner = new Owner { BlockDispose = true };
        var controller = new NovaObsPublishingLifecycleController(owner);
        controller.RequestStart();
        var stop = controller.RequestStopAsync();
        await owner.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(stop.IsCompleted);
        Assert.False(controller.Completion.IsCompleted);
        Assert.Equal(NovaObsPublishingLifecycleState.Stopping, controller.Read().LifecycleState);
        owner.ReleaseDispose.TrySetResult();
        Assert.True((await stop.WaitAsync(TimeSpan.FromSeconds(5))).CleanupCompleted);
        await controller.DisposeAsync();
        Assert.Equal(1, owner.Disposals);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnstartedShutdownUsesCompletionAfterDisposeAndSharesCleanup(bool shutdownCompleted)
    {
        var owner = new Owner
        {
            RequireDisposedForCompletion = true,
            ResultOnDispose = Result(ObsPublishChannelEndReason.HostCancelled) with { ShutdownCompleted = shutdownCompleted },
        };
        var controller = new NovaObsPublishingLifecycleController(owner);
        Assert.Equal(0, owner.CompletionReads);
        var stop = controller.RequestStopAsync();
        var disposal = controller.DisposeAsync().AsTask();
        Assert.Same(stop, controller.RequestStopAsync());
        var terminal = await stop.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(shutdownCompleted, terminal.CleanupCompleted);
        Assert.False(terminal.NormalEosProven);
        Assert.Equal(ObsPublishChannelEndReason.HostCancelled, terminal.EndReason);
        Assert.Equal(shutdownCompleted ? NovaObsPublishingLifecycleFailure.None : NovaObsPublishingLifecycleFailure.CleanupFailed,
            terminal.Failure);
        if (shutdownCompleted) await disposal;
        else await Assert.ThrowsAsync<InvalidOperationException>(() => disposal);
        Assert.Equal(0, owner.Starts);
        Assert.Equal(1, owner.Disposals);
        Assert.Equal(1, owner.CompletionReads);
    }

    [Theory]
    [InlineData(0)] // Pending
    [InlineData(1)] // Getter throws
    [InlineData(2)] // Faulted
    [InlineData(3)] // Null result
    [InlineData(4)] // Cancelled
    [InlineData(5)] // Null task
    public async Task UnstartedMissingCompletionEvidenceFailsClosedAndLateResultCannotRepair(int failure)
    {
        var owner = new Owner
        {
            RequireDisposedForCompletion = true,
            LeaveCompletionPendingOnDispose = failure == 0,
            ThrowCompletionAccess = failure == 1,
            ReturnNullCompletion = failure == 5,
        };
        if (failure == 2) owner.FaultCompletion();
        if (failure == 3) owner.Completed.TrySetResult(null!);
        if (failure == 4) owner.Completed.TrySetCanceled();
        var controller = new NovaObsPublishingLifecycleController(owner);
        var terminal = await controller.RequestStopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(terminal.CleanupCompleted);
        Assert.False(terminal.NormalEosProven);
        Assert.Equal(NovaObsPublishingLifecycleFailure.CompletionFailed, terminal.Failure);
        owner.Completed.TrySetResult(Result(ObsPublishChannelEndReason.PublisherStopped));
        Assert.Same(terminal, await controller.Completion);
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.DisposeAsync().AsTask());
        Assert.Equal(0, owner.Starts);
        Assert.Equal(1, owner.Disposals);
        Assert.Equal(1, owner.CompletionReads);
    }

    [Fact]
    public async Task UnstartedDisposeExceptionRetainsCleanupFailedWithoutReadingCompletion()
    {
        var owner = new Owner { ThrowDispose = true };
        var controller = new NovaObsPublishingLifecycleController(owner);
        var terminal = await controller.RequestStopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(NovaObsPublishingLifecycleFailure.CleanupFailed, terminal.Failure);
        Assert.False(terminal.CleanupCompleted);
        Assert.False(terminal.NormalEosProven);
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.DisposeAsync().AsTask());
        Assert.Equal(1, owner.Disposals);
        Assert.Equal(0, owner.CompletionReads);
    }

    private static ObsPublishChannelResult Result(ObsPublishChannelEndReason reason) =>
        new(reason, 1, 10, 1, 0, PcpRelayDrainOutcome.Completed, true, true,
            new(reason == ObsPublishChannelEndReason.PublisherStopped ? ObsRtmpIngestEndReason.PublisherStopped : ObsRtmpIngestEndReason.Cancelled, 1, 10), null);

    private sealed class Factory : INovaObsPublishingRuntimeFactory
    {
        public Owner Owner { get; } = new();
        public int Creates { get; private set; }
        public INovaObsPublishingRuntime Create() { Creates++; return Owner; }
    }

    private class UnsupportedOwner : INovaObsPublishingRuntime
    {
        public ObsPublishStatusSnapshot Current { get; set; } = new(
            new ChannelId(Guid.NewGuid()), new ChannelSessionId(Guid.NewGuid()), 1,
            ChannelSessionState.Created, false, false, false,
            "rtmp://DO-NOT-DISPLAY", "DO-NOT-DISPLAY", "http://DO-NOT-DISPLAY", null, null, null);
        public int Starts;
        public int Disposals;
        public int Reads;
        public ObsPublishStatusSnapshot Snapshot { get { Interlocked.Increment(ref Reads); return Current; } }
        public virtual void Start(CancellationToken token) { Interlocked.Increment(ref Starts); }
        public virtual ValueTask DisposeAsync() { Interlocked.Increment(ref Disposals); return ValueTask.CompletedTask; }
    }

    private sealed class Owner : UnsupportedOwner, INovaObsPublishingLifecycleRuntime
    {
        public TaskCompletionSource<ObsPublishChannelResult> Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StartEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseStart { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DisposeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDispose { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool BlockStart { get; init; }
        public bool BlockDispose { get; init; }
        public bool ThrowStart { get; init; }
        public bool ThrowCompletionAccess { get; init; }
        public bool ThrowDispose { get; init; }
        public bool LeaveCompletionPendingOnDispose { get; init; }
        public bool ReturnNullCompletion { get; init; }
        public bool RequireDisposedForCompletion { get; init; }
        public bool DisposeFinished { get; private set; }
        public int CompletionReads { get; private set; }
        public ObsPublishChannelResult? ResultOnDispose { get; init; }
        public CancellationToken StartToken { get; private set; }
        public Task<ObsPublishChannelResult> Completion
        {
            get
            {
                CompletionReads++;
                if (ThrowCompletionAccess || (RequireDisposedForCompletion && !DisposeFinished))
                    throw new InvalidOperationException("SECRET");
                return ReturnNullCompletion ? null! : Completed.Task;
            }
        }

        public override void Start(CancellationToken token)
        {
            base.Start(token);
            StartToken = token;
            StartEntered.TrySetResult();
            if (BlockStart) ReleaseStart.Task.GetAwaiter().GetResult();
            if (ThrowStart) throw new InvalidOperationException("SECRET endpoint");
            Current = Current with { Revision = 2, State = ChannelSessionState.Negotiating, ListenerReady = true };
        }

        public void End(ObsPublishChannelResult result)
        {
            Current = Current with { Revision = 3, State = ChannelSessionState.Stopped, EndReason = result.EndReason };
            Completed.TrySetResult(result);
        }

        public void FaultCompletion() => Completed.TrySetException(new InvalidOperationException("SECRET endpoint"));

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            DisposeEntered.TrySetResult();
            if (BlockDispose) await ReleaseDispose.Task;
            if (ThrowDispose) throw new InvalidOperationException("SECRET cleanup");
            if (!LeaveCompletionPendingOnDispose && !Completed.Task.IsCompleted)
                End(ResultOnDispose ?? Result(ObsPublishChannelEndReason.HostCancelled));
            DisposeFinished = true;
        }
    }
}
