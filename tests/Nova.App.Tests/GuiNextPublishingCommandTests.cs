using System.Text.Json;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Network.Rtmp;

namespace PeerCast.Nova.App.Tests;

public sealed class GuiNextPublishingCommandTests
{
    [Fact]
    public async Task BindingNavigationAndRefreshNeverStartOrStop()
    {
        var factory = new Factory();
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        await using var tabs = await Tabs(composition);
        tabs.SelectedTab = GenesisGuiNextTab.Channels;
        await tabs.RequestPublishingStartAsync();
        await tabs.RefreshPublishingStatusAsync();
        await tabs.RequestPublishingInterruptAsync();
        await tabs.RefreshPublishingStatusAsync();
        tabs.SelectedTab = GenesisGuiNextTab.Publishing;
        await tabs.RefreshPublishingStatusAsync();
        tabs.SelectedTab = GenesisGuiNextTab.Channels;
        tabs.SelectedTab = GenesisGuiNextTab.Publishing;
        await tabs.RefreshPublishingStatusAsync();
        Assert.True(tabs.PublishingCommands.CanStart);
        Assert.False(tabs.PublishingCommands.CanInterrupt);
        Assert.Equal(0, factory.Owner.Starts);
        Assert.Equal(0, factory.Owner.Disposals);
    }

    [Fact]
    public async Task ExplicitCommandsForwardOnceAndRejectReusedOwnerOnRestart()
    {
        var factory = new Factory();
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        await using var tabs = await Tabs(composition);
        var initial = Assert.Single(tabs.PublishingRows);
        await tabs.RequestPublishingStartAsync();
        await tabs.RefreshPublishingStatusAsync();
        Assert.False(tabs.PublishingCommands.CanStart);
        Assert.True(tabs.PublishingCommands.CanInterrupt);
        Assert.Equal("開始要求受付済み（待受未確認）", Assert.Single(tabs.PublishingRows).StateText);
        Assert.Equal(initial.SessionId, Assert.Single(tabs.PublishingRows).SessionId);
        Assert.Equal(initial.ChannelId, Assert.Single(tabs.PublishingRows).ChannelId);
        await tabs.RequestPublishingStartAsync();
        await tabs.RefreshPublishingStatusAsync();
        await tabs.RequestPublishingInterruptAsync();
        await tabs.RefreshPublishingStatusAsync();
        await tabs.RequestPublishingInterruptAsync();
        await tabs.RefreshPublishingStatusAsync();
        Assert.True(tabs.PublishingCommands.CanStart);
        await tabs.RequestPublishingStartAsync();
        await tabs.RefreshPublishingStatusAsync();
        Assert.Contains("既存owner", tabs.PublishingCommands.DiagnosticText);
        Assert.Equal(1, factory.Owner.Starts);
        Assert.Equal(1, factory.Owner.Disposals);
        Assert.False(tabs.PublishingCommands.CanStart);
        Assert.False(tabs.PublishingCommands.CanInterrupt);
        var terminal = Assert.Single(tabs.PublishingRows);
        Assert.Equal("停止（キャンセル）", terminal.StateText);
        Assert.Equal(NovaObsPublishingCompletionKind.Cancelled, terminal.CompletionKind);
        Assert.False((await composition.Lifecycle.Completion).NormalEosProven);
    }

    [Fact]
    public async Task BlockedStartIsOffCallerThreadClickSpamAndInterruptAreBounded()
    {
        var factory = new Factory();
        factory.Owner.BlockStart = true;
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        await using var tabs = await Tabs(composition);
        var changed = new List<string?>();
        tabs.PropertyChanged += (_, e) => { lock (changed) changed.Add(e.PropertyName); };
        var start = tabs.RequestPublishingStartAsync();
        await factory.Owner.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await tabs.RefreshPublishingStatusAsync();
        Assert.False(start.IsCompleted);
        Assert.False(tabs.PublishingCommands.CanStart);
        Assert.True(tabs.PublishingCommands.CanInterrupt);
        Assert.Contains("未確認", tabs.PublishingCommands.DiagnosticText);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => tabs.RequestPublishingStartAsync()));
        var interrupt = tabs.RequestPublishingInterruptAsync();
        await tabs.PublishingCommandAdmission;
        await tabs.RefreshPublishingStatusAsync();
        Assert.False(tabs.PublishingCommands.CanInterrupt);
        Assert.Contains("キャンセル", tabs.PublishingCommands.DiagnosticText);
        Assert.Equal(0, factory.Owner.Disposals);
        factory.Owner.ReleaseStart.TrySetResult();
        await Task.WhenAll(start, interrupt).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, factory.Owner.Starts);
        Assert.Equal(1, factory.Owner.Disposals);
        lock (changed)
        {
            Assert.Contains(nameof(GenesisGuiNextTabsViewModel.PublishingCommands), changed);
        }
    }

    [Fact]
    public async Task ShutdownClosesAdmissionDrainsStartAndRejectsLateViewUpdates()
    {
        var factory = new Factory();
        factory.Owner.BlockStart = true;
        var composition = NovaGuiNextPublishingComposition.Create(factory);
        var tabs = await Tabs(composition);
        var start = tabs.RequestPublishingStartAsync();
        await factory.Owner.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await tabs.RefreshPublishingStatusAsync();
        await tabs.DisposeAsync();
        var rows = tabs.PublishingRows;
        var lateUpdates = 0;
        tabs.PropertyChanged += (_, _) => Interlocked.Increment(ref lateUpdates);
        var shutdown = composition.DisposeAsync().AsTask();
        Assert.False(shutdown.IsCompleted);
        await composition.Commands.RequestStartAsync();
        await tabs.RequestPublishingInterruptAsync();
        await tabs.RefreshPublishingStatusAsync();
        Assert.False(composition.Commands.ReadCommands().CanStart);
        Assert.False(composition.Commands.ReadCommands().CanInterrupt);
        Assert.Equal(0, factory.Owner.Disposals);
        factory.Owner.ReleaseStart.TrySetResult();
        await Task.WhenAll(start, shutdown).WaitAsync(TimeSpan.FromSeconds(10));
        await composition.DisposeAsync();
        Assert.Same(rows, tabs.PublishingRows);
        Assert.Equal(0, lateUpdates);
        Assert.Equal(1, factory.Owner.Starts);
        Assert.Equal(1, factory.Owner.Disposals);
    }

    [Fact]
    public async Task ConcurrentApplicationCommandsAndShutdownDisposeExactlyOnce()
    {
        var factory = new Factory();
        var composition = NovaGuiNextPublishingComposition.Create(factory);
        var commands = composition.Commands;
        await Task.WhenAll(Enumerable.Range(0, 30).Select(i => Task.Run(async () =>
        {
            if (i % 3 == 0) await commands.RequestStartAsync();
            else if (i % 3 == 1) await commands.RequestInterruptAsync();
            else await composition.DisposeAsync();
        })));
        Assert.InRange(factory.Owner.Starts, 0, 1);
        Assert.Equal(1, factory.Owner.Disposals);
        Assert.False(commands.ReadCommands().CanStart);
        Assert.False(commands.ReadCommands().CanInterrupt);
    }

    [Fact]
    public async Task MissingCommandsAndMissingOwnerDisableBothButtons()
    {
        await using var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        tabs.SelectedTab = GenesisGuiNextTab.Publishing;
        Assert.False(tabs.PublishingCommands.CanStart);
        Assert.False(tabs.PublishingCommands.CanInterrupt);
        await tabs.RequestPublishingStartAsync();
        await tabs.RefreshPublishingStatusAsync();
        var factory = new Factory { Fails = true };
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        tabs.SetPublishingDispatcher(action => action());
        tabs.BindPublishingStatus(composition.Status, composition.Commands);
        await tabs.RefreshPublishingStatusAsync();
        Assert.False(tabs.PublishingCommands.CanStart);
        Assert.False(tabs.PublishingCommands.CanInterrupt);
        await tabs.RequestPublishingStartAsync();
        await tabs.RefreshPublishingStatusAsync();
        Assert.Equal(0, factory.Owner.Starts);
        Assert.Equal(NovaObsPublishingAvailability.Unavailable, Assert.Single(tabs.PublishingRows).Availability);
    }

    [Fact]
    public async Task StartFailureProjectsSafeErrorAndPreventsFurtherCommands()
    {
        var factory = new Factory();
        factory.Owner.FailStart = true;
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        await using var tabs = await Tabs(composition);
        await tabs.RequestPublishingStartAsync();
        await tabs.RefreshPublishingStatusAsync();
        await composition.Lifecycle.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        await tabs.RefreshPublishingStatusAsync();
        Assert.Equal(NovaObsPublishingLifecycleFailure.StartFailed, Assert.Single(tabs.PublishingRows).LifecycleFailure);
        Assert.False(tabs.PublishingCommands.CanStart);
        Assert.False(tabs.PublishingCommands.CanInterrupt);
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(tabs.PublishingRows));
    }

    [Fact]
    public async Task InterruptCleanupFailureIsFailureNotGracefulSuccess()
    {
        var factory = new Factory();
        factory.Owner.FailDispose = true;
        var composition = NovaGuiNextPublishingComposition.Create(factory);
        await using var tabs = await Tabs(composition);
        await tabs.RequestPublishingStartAsync();
        await tabs.RefreshPublishingStatusAsync();
        await tabs.RequestPublishingInterruptAsync();
        await tabs.RefreshPublishingStatusAsync();
        var terminal = await composition.Lifecycle.Completion;
        Assert.False(terminal.CleanupCompleted);
        Assert.False(terminal.NormalEosProven);
        Assert.Equal(NovaObsPublishingLifecycleFailure.CleanupFailed, terminal.Failure);
        Assert.False(tabs.PublishingCommands.CanStart);
        Assert.False(tabs.PublishingCommands.CanInterrupt);
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(tabs.PublishingRows));
        await Assert.ThrowsAsync<InvalidOperationException>(() => composition.DisposeAsync().AsTask());
        Assert.Equal(1, factory.Owner.Disposals);
    }

    [Fact]
    public async Task AcceptedListenerConnectedAndMediaStatesRemainDistinctAndPrivate()
    {
        var factory = new Factory();
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        await using var tabs = await Tabs(composition);
        await tabs.RequestPublishingStartAsync();
        await tabs.RefreshPublishingStatusAsync();
        Assert.Equal("開始要求受付済み（待受未確認）", Assert.Single(tabs.PublishingRows).StateText);
        factory.Owner.Current = factory.Owner.Current with { Revision = 2, State = ChannelSessionState.Negotiating, ListenerReady = true };
        await tabs.RefreshPublishingStatusAsync();
        Assert.Equal("OBS待機中", Assert.Single(tabs.PublishingRows).StateText);
        factory.Owner.Current = factory.Owner.Current with { Revision = 3, PublisherConnected = true };
        await tabs.RefreshPublishingStatusAsync();
        Assert.Equal("OBS接続中", Assert.Single(tabs.PublishingRows).StateText);
        factory.Owner.Current = factory.Owner.Current with { Revision = 4, State = ChannelSessionState.Receiving, HeadAvailable = true };
        await tabs.RefreshPublishingStatusAsync();
        Assert.Equal("OBSから配信を受信中", Assert.Single(tabs.PublishingRows).StateText);
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(tabs.PublishingRows));
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(tabs.PublishingCommands));
    }

    [Fact]
    public async Task NavigationAwayDoesNotInterruptAndLateTerminalCannotReenableStart()
    {
        var factory = new Factory();
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        await using var tabs = await Tabs(composition);
        await tabs.RequestPublishingStartAsync();
        await tabs.RefreshPublishingStatusAsync();
        tabs.SelectedTab = GenesisGuiNextTab.Channels;
        await tabs.RequestPublishingInterruptAsync();
        await tabs.RefreshPublishingStatusAsync();
        Assert.Equal(0, factory.Owner.Disposals);
        factory.Owner.End(ObsPublishChannelEndReason.Disconnected);
        await composition.Lifecycle.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        tabs.SelectedTab = GenesisGuiNextTab.Publishing;
        await tabs.RefreshPublishingStatusAsync();
        Assert.False(tabs.PublishingCommands.CanStart);
        Assert.False(tabs.PublishingCommands.CanInterrupt);
        factory.Owner.Current = factory.Owner.Current with { Revision = 9, State = ChannelSessionState.Receiving, HeadAvailable = true, EndReason = null };
        await tabs.RefreshPublishingStatusAsync();
        Assert.Equal(NovaObsPublishingLifecycleState.Failed, Assert.Single(tabs.PublishingRows).LifecycleState);
        await tabs.RequestPublishingStartAsync();
        await tabs.RefreshPublishingStatusAsync();
        Assert.Equal(1, factory.Owner.Starts);
    }

    [Fact]
    public async Task LostStatusObservationDoesNotPreventOwnedCancellationCleanup()
    {
        var factory = new Factory();
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        await using var tabs = await Tabs(composition);
        await tabs.RequestPublishingStartAsync();
        await tabs.RefreshPublishingStatusAsync();
        factory.Owner.FailRead = true;
        await tabs.RefreshPublishingStatusAsync();
        Assert.Equal(NovaObsPublishingAvailability.Unavailable, Assert.Single(tabs.PublishingRows).Availability);
        Assert.False(tabs.PublishingCommands.CanStart);
        Assert.True(tabs.PublishingCommands.CanInterrupt);
        await tabs.RequestPublishingInterruptAsync();
        await tabs.RefreshPublishingStatusAsync();
        Assert.Equal(1, factory.Owner.Disposals);
        Assert.False(tabs.PublishingCommands.CanInterrupt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnexpectedCommandBoundaryExceptionsAreSafeAndShutdownStillCleansUp(bool failStart)
    {
        var factory = new Factory();
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        await using var commands = new NovaGuiNextPublishingCommands(new FaultingBoundary(composition.Lifecycle, failStart));
        await commands.RequestStartAsync();
        if (!failStart) await commands.RequestInterruptAsync();
        var state = commands.ReadCommands();
        Assert.False(state.CanStart);
        Assert.False(state.CanInterrupt);
        Assert.Contains("失敗", state.DiagnosticText);
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(state));
        await commands.DisposeAsync();
        Assert.Equal(1, factory.Owner.Disposals);
    }

    [Fact]
    public void NativeCommandLabelsAndIdentitiesAreDedicatedAndScopeIsTruthful()
    {
        Assert.Equal("配信開始", GenesisGuiNextPublishingPresentation.StartLabel);
        Assert.Equal("配信中断", GenesisGuiNextPublishingPresentation.InterruptLabel);
        Assert.NotEqual(GenesisGuiNextAppShellBridge.PublishingStartControlId, GenesisGuiNextAppShellBridge.PublishingInterruptControlId);
        Assert.NotEqual(GenesisGuiNextAppShellBridge.PublishingStartControlId, GenesisGuiNextAppShellBridge.RecordingHistoryDetailsControlId);
        var otherControlIds = typeof(GenesisGuiNextAppShellBridge)
            .GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Where(field => field.IsLiteral && field.FieldType == typeof(int) && field.Name.EndsWith("ControlId", StringComparison.Ordinal) &&
                field.Name is not nameof(GenesisGuiNextAppShellBridge.PublishingStartControlId) and
                    not nameof(GenesisGuiNextAppShellBridge.PublishingInterruptControlId))
            .Select(field => (int)field.GetRawConstantValue()!);
        Assert.DoesNotContain(GenesisGuiNextAppShellBridge.PublishingStartControlId, otherControlIds);
        Assert.DoesNotContain(GenesisGuiNextAppShellBridge.PublishingInterruptControlId, otherControlIds);
        Assert.Contains("待受の開始要求", GenesisGuiNextPublishingPresentation.ScopeText);
        Assert.Contains("キャンセル", GenesisGuiNextPublishingPresentation.ScopeText);
        Assert.Contains("OBS正常EOSとは別", GenesisGuiNextPublishingPresentation.ScopeText);
    }

    [Fact]
    public async Task ShutdownDuringInterruptJoinsOnePendingCleanupWithoutPumpingUiContext()
    {
        var factory = new Factory();
        factory.Owner.BlockDispose = true;
        var composition = NovaGuiNextPublishingComposition.Create(factory);
        await using var tabs = await Tabs(composition);
        await tabs.RequestPublishingStartAsync();
        await tabs.RefreshPublishingStatusAsync();
        var interrupt = tabs.RequestPublishingInterruptAsync();
        await tabs.PublishingCommandAdmission;
        await tabs.RefreshPublishingStatusAsync();
        await factory.Owner.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var previous = SynchronizationContext.Current;
        var context = new NeverPumpedContext();
        Task shutdown;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            shutdown = composition.DisposeAsync().AsTask();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        Assert.False(interrupt.IsCompleted);
        Assert.False(shutdown.IsCompleted);
        Assert.Same(shutdown, composition.DisposeAsync().AsTask());
        Assert.False(composition.Commands.ReadCommands().CanStart);
        Assert.False(composition.Commands.ReadCommands().CanInterrupt);
        Assert.Equal(1, factory.Owner.Disposals);
        factory.Owner.ReleaseDispose.TrySetResult();
        await Task.WhenAll(interrupt, shutdown).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, factory.Owner.Starts);
        Assert.Equal(1, factory.Owner.Disposals);
        Assert.Equal(0, context.Posts);
    }

    [Fact]
    public async Task ClosingAdmissionRejectsCommandsBeforeOwnedShutdownRuns()
    {
        var factory = new Factory();
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        composition.CloseAdmission();
        await composition.Commands.RequestStartAsync();
        await composition.Commands.RequestInterruptAsync();
        Assert.False(composition.Commands.ReadCommands().CanStart);
        Assert.False(composition.Commands.ReadCommands().CanInterrupt);
        Assert.Equal(0, factory.Owner.Starts);
        Assert.Equal(0, factory.Owner.Disposals);
        await composition.DisposeAsync();
        Assert.Equal(1, factory.Owner.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnstartedShutdownRetainsLifecycleFailClosedEvidence(bool pendingCompletion)
    {
        var factory = new Factory();
        factory.Owner.LeaveCompletionPending = pendingCompletion;
        factory.Owner.ShutdownCompleted = false;
        var composition = NovaGuiNextPublishingComposition.Create(factory);
        await using var tabs = await Tabs(composition);
        await Assert.ThrowsAsync<InvalidOperationException>(() => composition.DisposeAsync().AsTask());
        var terminal = await composition.Lifecycle.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(terminal.CleanupCompleted);
        Assert.False(terminal.NormalEosProven);
        await tabs.RefreshPublishingStatusAsync();
        Assert.Equal(NovaObsPublishingLifecycleState.Failed, Assert.Single(tabs.PublishingRows).LifecycleState);
        Assert.False(Assert.Single(tabs.PublishingRows).CleanupCompleted);
        Assert.False(tabs.PublishingCommands.CanFinish);
        Assert.False(tabs.PublishingCommands.CanStart);
        Assert.False(tabs.PublishingCommands.CanInterrupt);
        Assert.Equal(0, factory.Owner.Starts);
        Assert.Equal(1, factory.Owner.Disposals);
    }

    private sealed class NeverPumpedContext : SynchronizationContext
    {
        internal int Posts;
        public override void Post(SendOrPostCallback d, object? state) => Interlocked.Increment(ref Posts);
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

    private sealed class Factory : INovaObsPublishingRuntimeFactory
    {
        internal Owner Owner { get; } = new();
        internal bool Fails { get; init; }
        public INovaObsPublishingRuntime Create() => Fails ? throw new InvalidOperationException("SECRET") : Owner;
    }

    private sealed class FaultingBoundary(INovaObsPublishingLifecycleController inner, bool failStart)
        : INovaObsPublishingLifecycleController
    {
        public NovaObsPublishingReadOnlySnapshot Read() => inner.Read();
        public Task<NovaObsPublishingLifecycleResult> Completion => inner.Completion;
        public NovaObsPublishingStartResult RequestStart(CancellationToken cancellationToken = default) =>
            failStart ? throw new InvalidOperationException("SECRET start") : inner.RequestStart(cancellationToken);
        public Task<NovaObsPublishingLifecycleResult> RequestStopAsync() => throw new InvalidOperationException("SECRET stop");
        public Task<NovaObsPublishingLifecycleResult> RequestFinishAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
            inner.RequestFinishAsync(timeout, cancellationToken);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class Owner : INovaObsPublishingLifecycleRuntime
    {
        internal ObsPublishStatusSnapshot Current = new(new ChannelId(Guid.NewGuid()), new ChannelSessionId(Guid.NewGuid()),
            1, ChannelSessionState.Created, false, false, false, "rtmp://SECRET", "SECRET-key", "http://SECRET", null, null, null);
        internal int Starts;
        internal int Disposals;
        internal bool BlockStart;
        internal bool FailStart;
        internal bool FailDispose;
        internal bool FailRead;
        internal bool BlockDispose;
        internal bool LeaveCompletionPending;
        internal bool ShutdownCompleted = true;
        internal TaskCompletionSource DisposeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseDispose { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource StartEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseStart { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<ObsPublishChannelResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ObsPublishChannelResult> Completion => _completion.Task;
        public ObsPublishStatusSnapshot Snapshot => FailRead ? throw new InvalidOperationException("SECRET status") : Current;
        public void Start(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Starts);
            StartEntered.TrySetResult();
            if (BlockStart) ReleaseStart.Task.GetAwaiter().GetResult();
            if (FailStart) throw new InvalidOperationException("SECRET endpoint");
        }
        internal void End(ObsPublishChannelEndReason reason)
        {
            Current = Current with { Revision = 5, State = ChannelSessionState.Stopped, EndReason = reason };
            _completion.TrySetResult(new(reason, 0, 0, 0, 0, PcpRelayDrainOutcome.Completed, true, ShutdownCompleted,
                new(ObsRtmpIngestEndReason.Cancelled, 0, 0), null));
        }
        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref Disposals);
            DisposeEntered.TrySetResult();
            if (BlockDispose) await ReleaseDispose.Task.ConfigureAwait(false);
            if (FailDispose) throw new InvalidOperationException("SECRET cleanup");
            if (!LeaveCompletionPending && !Completion.IsCompleted) End(ObsPublishChannelEndReason.HostCancelled);
        }
    }
}
