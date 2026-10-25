using System.Collections.Concurrent;
using System.Text.Json;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Network.Rtmp;

namespace PeerCast.Nova.App.Tests;

public sealed class GuiNextPublishingFinishCommandTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ReadsNavigationAndUnadmittedFinishHaveNoSideEffects()
    {
        var owner = new Owner();
        await using var composition = NovaGuiNextPublishingComposition.Create(new Factory(owner));
        await using var tabs = await Tabs(composition);
        Assert.False(tabs.PublishingCommands.CanFinish);
        await tabs.RequestPublishingFinishAsync();
        await tabs.RefreshPublishingStatusAsync();
        await tabs.RequestPublishingStartAsync();
        await tabs.RefreshPublishingStatusAsync();
        Assert.True(tabs.PublishingCommands.CanFinish);
        tabs.SelectedTab = GenesisGuiNextTab.Channels;
        await tabs.RequestPublishingFinishAsync();
        await tabs.RefreshPublishingStatusAsync();
        tabs.SelectedTab = GenesisGuiNextTab.Publishing;
        await tabs.RefreshPublishingStatusAsync(); // Same read-only path used by publishing refresh/F5.
        Assert.Equal(0, owner.Finishes);
        Assert.Equal(0, owner.Disposals);
    }

    [Fact]
    public async Task ExplicitFinishIsAsyncDeduplicatedAndAllowsProvenRestart()
    {
        var old = new Owner();
        var next = new Owner();
        var factory = new Factory(old, next);
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        await using var tabs = await Tabs(composition);
        await tabs.RequestPublishingStartAsync();
        await tabs.RefreshPublishingStatusAsync();
        var finish = tabs.RequestPublishingFinishAsync();
        await old.FinishEntered.Task.WaitAsync(Limit);
        await tabs.RefreshPublishingStatusAsync();
        Assert.False(finish.IsCompleted);
        Assert.False(tabs.PublishingCommands.CanFinish);
        Assert.False(tabs.PublishingCommands.CanStart);
        Assert.True(tabs.PublishingCommands.CanInterrupt);
        Assert.Contains("終了要求中", tabs.PublishingCommands.DiagnosticText);
        var duplicates = Enumerable.Range(0, 20).Select(_ => tabs.RequestPublishingFinishAsync()).ToArray();
        Assert.All(duplicates, task => Assert.False(task.IsCompleted));
        Assert.Equal(1, old.Finishes);
        Assert.InRange(old.Timeout.TotalSeconds, 0.001, 30);
        old.End();
        await Task.WhenAll(duplicates).WaitAsync(Limit);
        await finish.WaitAsync(Limit);
        await tabs.RefreshPublishingStatusAsync();
        var terminal = Assert.Single(tabs.PublishingRows);
        Assert.Equal("終了（Novaローカル）", terminal.StateText);
        Assert.Equal(NovaObsPublishingCompletionKind.LocalCompleted, terminal.CompletionKind);
        Assert.True(terminal.CleanupCompleted);
        Assert.Contains("NotObserved", GenesisGuiNextPublishingPresentation.ErrorText(terminal));
        Assert.False((await composition.Lifecycle.Completion).NormalEosProven);
        Assert.True(tabs.PublishingCommands.CanStart);
        await tabs.RequestPublishingStartAsync();
        await tabs.RefreshPublishingStatusAsync();
        Assert.Equal(2, factory.Creates);
        Assert.Equal(2, Assert.Single(tabs.PublishingRows).Generation);
        Assert.Equal(next.Current.SessionId.Value, Assert.Single(tabs.PublishingRows).SessionId);
        Assert.True(tabs.PublishingCommands.CanFinish);
        Assert.Equal(1, old.Disposals);
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(tabs.PublishingRows));
    }

    [Fact]
    public async Task InterruptDuringFinishUsesForcedCleanupOnce()
    {
        var owner = new Owner { BlockDispose = true };
        await using var composition = NovaGuiNextPublishingComposition.Create(new Factory(owner));
        await using var tabs = await Tabs(composition);
        await tabs.RequestPublishingStartAsync();
        await tabs.RefreshPublishingStatusAsync();
        var generation = composition.CurrentGeneration;
        var session = owner.Current.SessionId.Value;
        var lifecycle = Assert.IsType<NovaObsPublishingLifecycleController>(composition.Lifecycle);
        var finish = tabs.RequestPublishingFinishAsync();
        await owner.FinishEntered.Task.WaitAsync(Limit);
        await tabs.RefreshPublishingStatusAsync();
        Assert.True(tabs.PublishingCommands.CanInterrupt);
        Assert.True(composition.ReadCommands().CanInterrupt);
        var ui = new ConcurrentQueue<Action>();
        tabs.SetPublishingDispatcher(ui.Enqueue);
        try
        {
            var interrupt = tabs.RequestPublishingInterruptAsync();
            await tabs.PublishingCommandAdmission.WaitAsync(Limit);
            await owner.DisposeEntered.Task.WaitAsync(Limit);
            Assert.True(lifecycle.StopAdmissionObserved);
            Assert.Equal(NovaObsPublishingLifecycleState.Stopping, lifecycle.Read().LifecycleState);
            Assert.False(composition.ReadCommands().CanInterrupt);
            Assert.False(interrupt.IsCompleted); Assert.False(finish.IsCompleted);
            Assert.Equal(1, owner.Disposals);
            var duplicate = composition.RequestInterruptAsync();
            Assert.Same(duplicate, composition.RequestInterruptAsync());
            // Delivery is deliberately held: the old cache is not admission proof.
            Assert.True(tabs.PublishingCommands.CanInterrupt);
            await PublishForGeneration(tabs, ui, generation, session);
            Assert.False(tabs.PublishingCommands.CanInterrupt);
            owner.ReleaseDispose.TrySetResult();
            await Task.WhenAll(finish, interrupt, duplicate).WaitAsync(Limit);
            await PublishForGeneration(tabs, ui, generation, session);
            var result = await composition.Lifecycle.Completion;
            Assert.Equal(NovaObsPublishingCompletionKind.Cancelled, result.Kind);
            Assert.True(result.CleanupCompleted);
            Assert.False(result.LocalCompletedProven);
            Assert.False(result.NormalEosProven);
            Assert.Equal("停止（キャンセル）", Assert.Single(tabs.PublishingRows).StateText);
            Assert.True(tabs.PublishingCommands.CanStart);
            Assert.Equal(1, owner.Disposals);
            Assert.Same(result, await composition.Lifecycle.Completion);
        }
        finally { owner.ReleaseDispose.TrySetResult(); }
    }

    [Fact]
    public async Task ShutdownInterruptsFinishAndDisposedViewRejectsLateRefresh()
    {
        var owner = new Owner();
        var composition = NovaGuiNextPublishingComposition.Create(new Factory(owner));
        var tabs = await Tabs(composition);
        await tabs.RequestPublishingStartAsync();
        await tabs.RefreshPublishingStatusAsync();
        var finish = tabs.RequestPublishingFinishAsync();
        await owner.FinishEntered.Task.WaitAsync(Limit);
        await tabs.RefreshPublishingStatusAsync();
        await tabs.DisposeAsync();
        var rows = tabs.PublishingRows;
        await composition.DisposeAsync().AsTask().WaitAsync(Limit);
        await finish.WaitAsync(Limit);
        await tabs.RefreshPublishingStatusAsync();
        Assert.Same(rows, tabs.PublishingRows);
        Assert.Equal(1, owner.Disposals);
        Assert.False(composition.Commands.ReadCommands().CanFinish);
        Assert.False(composition.Commands.ReadCommands().CanStart);
    }

    [Theory]
    [InlineData(ObsPublishLocalTerminationOutcome.TimedOut)]
    [InlineData(ObsPublishLocalTerminationOutcome.DeliveryFailed)]
    [InlineData(ObsPublishLocalTerminationOutcome.CleanupIncomplete)]
    [InlineData(ObsPublishLocalTerminationOutcome.Failed)]
    public async Task NonSuccessIsVisibleAndCannotEnableRestart(ObsPublishLocalTerminationOutcome outcome)
    {
        var owner = new Owner();
        var composition = NovaGuiNextPublishingComposition.Create(new Factory(owner, new Owner()));
        await using var tabs = await Tabs(composition);
        await tabs.RequestPublishingStartAsync();
        await tabs.RefreshPublishingStatusAsync();
        var finish = tabs.RequestPublishingFinishAsync();
        await owner.FinishEntered.Task.WaitAsync(Limit);
        await tabs.RefreshPublishingStatusAsync();
        owner.End(outcome);
        await finish.WaitAsync(Limit);
        await tabs.RefreshPublishingStatusAsync();
        var row = Assert.Single(tabs.PublishingRows);
        Assert.Equal(NovaObsPublishingLifecycleState.Failed, row.LifecycleState);
        Assert.Contains(outcome.ToString(), GenesisGuiNextPublishingPresentation.ErrorText(row));
        Assert.False(tabs.PublishingCommands.CanStart);
        Assert.False(tabs.PublishingCommands.CanFinish);
        Assert.False((await composition.Lifecycle.Completion).LocalCompletedProven);
        if (outcome == ObsPublishLocalTerminationOutcome.CleanupIncomplete)
            await Assert.ThrowsAsync<InvalidOperationException>(() => composition.DisposeAsync().AsTask());
        else await composition.DisposeAsync();
    }

    [Fact]
    public async Task DeadlineCoversBlockedAdmittedStartAndShutdownDoesNotExtendIt()
    {
        var owner = new Owner { BlockStart = true };
        var clock = new GuiFinishAdmissionSafetyTests.Clock();
        var lifecycle = new NovaObsPublishingLifecycleController(owner) { FinishTimeProvider = clock };
        var commands = new NovaGuiNextPublishingCommands(lifecycle, TimeSpan.FromMilliseconds(100));
        var start = commands.RequestStartAsync();
        await owner.StartEntered.Task.WaitAsync(Limit);
        try
        {
            Assert.True(commands.ReadCommands().CanFinish);
            var finish = commands.RequestFinishAsync();
            clock.Advance(TimeSpan.FromSeconds(1));
            await finish.WaitAsync(Limit);
            Assert.Equal(NovaObsPublishingCompletionKind.TimedOut, (await lifecycle.Completion).Kind);
            Assert.False(commands.ReadCommands().CanStart);
            await Assert.ThrowsAsync<InvalidOperationException>(() => commands.DisposeAsync().AsTask().WaitAsync(Limit));
        }
        finally
        {
            owner.ReleaseStart.TrySetResult();
            await start.WaitAsync(Limit);
        }
        await owner.DisposeEntered.Task.WaitAsync(Limit);
        Assert.Equal(0, owner.Finishes);
    }

    [Fact]
    public async Task ReplacementInProgressRejectsFinishWithoutTouchingNewOwner()
    {
        var old = new Owner();
        var next = new Owner { BlockStart = true };
        await using var composition = NovaGuiNextPublishingComposition.Create(new Factory(old, next));
        await using var tabs = await Tabs(composition);
        await tabs.RequestPublishingStartAsync();
        await tabs.RefreshPublishingStatusAsync();
        await tabs.RequestPublishingInterruptAsync();
        await tabs.RefreshPublishingStatusAsync();
        Assert.Equal(1, composition.CurrentGeneration);
        var ui = new ConcurrentQueue<Action>(); tabs.SetPublishingDispatcher(ui.Enqueue);
        var replacement = tabs.RequestPublishingStartAsync();
        await next.StartEntered.Task.WaitAsync(Limit);
        var generation = composition.CurrentGeneration;
        var session = next.Current.SessionId.Value;
        Assert.Equal(2, generation);
        var lifecycle = Assert.IsType<NovaObsPublishingLifecycleController>(composition.Lifecycle);
        try
        {
            Assert.Equal(NovaObsPublishingLifecycleState.Starting, lifecycle.Read().LifecycleState);
            Assert.True(lifecycle.CanRequestFinish); // Start is admitted; composition replacement still fences GUI Finish.
            Assert.False(replacement.IsCompleted);
            Assert.False(composition.ReadCommands().CanFinish);
            var rejected = composition.RequestFinishForGenerationAsync(generation);
            Assert.True(rejected.IsCompleted); await rejected;
            await PublishForGeneration(tabs, ui, generation, session);
            Assert.False(tabs.PublishingCommands.CanFinish);
            await tabs.RequestPublishingFinishAsync();
            await tabs.PublishingCommandAdmission.WaitAsync(Limit);
            await PublishForGeneration(tabs, ui, generation, session);
            Assert.Equal(0, next.Finishes);
        }
        finally
        {
            next.ReleaseStart.TrySetResult();
            await replacement.WaitAsync(Limit);
        }
        Assert.Equal(NovaObsPublishingLifecycleState.Running, lifecycle.Read().LifecycleState);
        Assert.True(composition.ReadCommands().CanFinish);
        // The queued dispatcher still holds the previous replacing display.
        Assert.False(tabs.PublishingCommands.CanFinish);
        await PublishForGeneration(tabs, ui, generation, session);
        Assert.True(tabs.PublishingCommands.CanFinish);
        Assert.Equal(0, next.Finishes);
        Assert.NotEqual(old.Current.SessionId.Value, session);
    }

    private static async Task PublishForGeneration(GenesisGuiNextTabsViewModel tabs, ConcurrentQueue<Action> ui,
        long generation, Guid session)
    {
        await tabs.RefreshPublishingStatusAsync().WaitAsync(Limit);
        while (ui.TryDequeue(out var action)) action();
        var row = Assert.Single(tabs.PublishingRows);
        Assert.Equal(generation, row.Generation);
        Assert.Equal(session, row.SessionId);
        Assert.Equal(generation, tabs.PublishingCommands.Generation);
    }

    [Fact]
    public async Task OldCompletionAndCommandResultsCannotChangeNewGeneration()
    {
        var old = new Owner();
        var next = new Owner();
        await using var composition = NovaGuiNextPublishingComposition.Create(new Factory(old, next));
        await using var tabs = await Tabs(composition);
        await tabs.RequestPublishingStartAsync();
        await tabs.RefreshPublishingStatusAsync();
        var finish = tabs.RequestPublishingFinishAsync();
        await old.FinishEntered.Task.WaitAsync(Limit);
        await tabs.RefreshPublishingStatusAsync();
        old.End();
        await finish.WaitAsync(Limit);
        await tabs.RefreshPublishingStatusAsync();
        await tabs.RequestPublishingStartAsync();
        await tabs.RefreshPublishingStatusAsync();
        old.Current = old.Current with { SessionId = new(Guid.NewGuid()), State = ChannelSessionState.Faulted };
        old.End(ObsPublishLocalTerminationOutcome.Failed);
        await tabs.RefreshPublishingStatusAsync();
        Assert.Equal(2, Assert.Single(tabs.PublishingRows).Generation);
        Assert.Equal(next.Current.SessionId.Value, Assert.Single(tabs.PublishingRows).SessionId);
        Assert.Equal(NovaObsPublishingLifecycleState.Running, Assert.Single(tabs.PublishingRows).LifecycleState);
        Assert.True(tabs.PublishingCommands.CanFinish);
        Assert.Equal(0, next.Finishes);
    }

    private static async Task<GenesisGuiNextTabsViewModel> Tabs(NovaGuiNextPublishingComposition composition)
    {
        var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        tabs.SetPublishingDispatcher(action => action());
        tabs.BindPublishingStatus(composition.Status, composition.Commands);
        await tabs.RefreshPublishingStatusAsync();
        tabs.SelectedTab = GenesisGuiNextTab.Publishing;
        return tabs;
    }

    private sealed class Factory(params Owner[] owners) : INovaObsPublishingRuntimeFactory
    {
        internal int Creates;
        public INovaObsPublishingRuntime Create() => owners[Interlocked.Increment(ref Creates) - 1];
    }

    private sealed class Owner : INovaObsPublishingGenerationRuntime
    {
        private ObsPublishStatusSnapshot _current = new(new ChannelId(Guid.NewGuid()), new ChannelSessionId(Guid.NewGuid()),
            1, ChannelSessionState.Created, false, false, false, "rtmp://SECRET", "SECRET", "http://SECRET", null, null, null);
        public Owner() => GenerationProof = new(_current.ChannelId.Value, _current.SessionId.Value);
        public NovaObsPublishingGenerationProof GenerationProof { get; }
        internal ObsPublishStatusSnapshot Current
        {
            get { lock (GenerationProof.Gate) return _current; }
            set { lock (GenerationProof.Gate) { if (value.ChannelId != _current.ChannelId || value.SessionId != _current.SessionId) GenerationProof.Invalidate(); _current = value; } }
        }
        private readonly TaskCompletionSource<ObsPublishChannelResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<ObsPublishLocalTerminationResult> _finish = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource FinishEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource StartEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource DisposeEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Finishes, Disposals;
        internal bool BlockStart;
        internal bool BlockDispose;
        internal TaskCompletionSource ReleaseDispose = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TimeSpan Timeout;
        public ObsPublishStatusSnapshot Snapshot => Current;
        public Task<ObsPublishChannelResult> Completion => _completion.Task;
        public void Start(CancellationToken cancellationToken)
        {
            StartEntered.TrySetResult();
            if (BlockStart) ReleaseStart.Task.GetAwaiter().GetResult();
            Current = Current with { State = ChannelSessionState.Negotiating, ListenerReady = true, Revision = 2 };
        }
        public Task<ObsPublishLocalTerminationResult> RequestLocalTerminationAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            Timeout = timeout;
            Interlocked.Increment(ref Finishes);
            FinishEntered.TrySetResult();
            return _finish.Task;
        }
        internal void End(ObsPublishLocalTerminationOutcome outcome = ObsPublishLocalTerminationOutcome.LocalCompleted)
        {
            var local = new ObsPublishLocalTerminationResult(Current.ChannelId, Current.SessionId, outcome,
                ObsPublishLocalTerminationPhase.Finished, false, true, true, true,
                new(ObsRtmpQuiescenceOutcome.Completed, new(ObsRtmpIngestEndReason.Quiesced, 1, 10)),
                null, null, null, null, ObsPublishWriterEvidence.NotObserved,
                new(true, true, true, true, false, true, outcome != ObsPublishLocalTerminationOutcome.CleanupIncomplete));
            var reason = outcome == ObsPublishLocalTerminationOutcome.LocalCompleted ? ObsPublishChannelEndReason.LocalTerminated :
                outcome == ObsPublishLocalTerminationOutcome.Cancelled ? ObsPublishChannelEndReason.HostCancelled : ObsPublishChannelEndReason.InternalFailure;
            Current = Current with { State = ChannelSessionState.Stopped, EndReason = reason, Revision = Current.Revision + 1 };
            _finish.TrySetResult(local);
            _completion.TrySetResult(new(reason, 1, 10, 1, 0, PcpRelayDrainOutcome.Completed, true,
                outcome != ObsPublishLocalTerminationOutcome.CleanupIncomplete, local.Ingress?.FinalResult, null)
            { LocalTermination = local });
        }
        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref Disposals);
            DisposeEntered.TrySetResult();
            if (BlockDispose) await ReleaseDispose.Task;
            if (!_completion.Task.IsCompleted) End(ObsPublishLocalTerminationOutcome.Cancelled);
            DisposeEntered.TrySetResult();
        }
    }
}
