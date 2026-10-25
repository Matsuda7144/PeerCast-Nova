using System.Collections.Concurrent;
using Owner = PeerCast.Nova.App.Tests.NovaObsPublishingFinishTests.Owner;

namespace PeerCast.Nova.App.Tests;

public sealed class GuiFinishAdmissionSafetyTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private sealed class Factory(params Owner[] owners) : INovaObsPublishingRuntimeFactory
    { private int _index; public INovaObsPublishingRuntime Create() => owners[Interlocked.Increment(ref _index) - 1]; }
    private sealed class Status(Func<NovaObsPublishingReadOnlySnapshot> read) : INovaObsPublishingReadOnlyStatus
    { public NovaObsPublishingReadOnlySnapshot Read() => read(); }
    private sealed class Commands : INovaGuiNextPublishingCommands
    {
        internal long Generation;
        public NovaGuiNextPublishingCommandState ReadCommands() => new(true, true, "safe") { CanFinish = true, Generation = Generation };
        public Task RequestStartAsync() => Task.CompletedTask;
        public Task RequestInterruptAsync() => Task.CompletedTask;
        public Task RequestFinishAsync() => Task.CompletedTask;
    }
    private static NovaObsPublishingReadOnlySnapshot Snapshot(long generation) =>
        new(NovaObsPublishingAvailability.Observed, Guid.NewGuid(), Guid.NewGuid(), "safe", "safe", PeerCast.Nova.Windows.NovaObsPublishingTone.Normal, null, null, null)
        { Generation = generation };
    private static GenesisGuiNextTabsViewModel Tabs() => new(new GenesisFavoriteChannelCollection());

    [Fact]
    public async Task BlockedRefreshIsOffCallerGateCoalescesAndDoesNotBlockDispose()
    {
        var tabs = Tabs(); var reads = 0; var caller = Environment.CurrentManagedThreadId; var reader = caller;
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var status = new Status(() => { reader = Environment.CurrentManagedThreadId; Interlocked.Increment(ref reads); entered.Set(); if (!release.Wait(Limit)) throw new TimeoutException(); return Snapshot(1); });
        tabs.SetPublishingDispatcher(a => a());
        tabs.BindPublishingStatus(status);
        Assert.True(entered.Wait(Limit)); Assert.NotEqual(caller, reader);
        var worker = tabs.RefreshPublishingStatusAsync();
        for (var i = 0; i < 100; i++) Assert.Same(worker, tabs.RefreshPublishingStatusAsync());
        Assert.Equal(1, reads);
        var notifications = 0; tabs.PropertyChanged += (_, _) => notifications++;
        await tabs.DisposeAsync().AsTask().WaitAsync(Limit);
        Assert.False(release.IsSet); Assert.False(worker.IsCompleted);
        release.Set(); await worker.WaitAsync(Limit);
        Assert.Equal(1, reads); Assert.Equal(0, notifications); Assert.Empty(tabs.PublishingRows);
    }

    [Fact]
    public async Task RebindingUsesOneReaderAndDiscardsBlockedOldResult()
    {
        await using var tabs = Tabs();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var old = Snapshot(1); var next = Snapshot(2); var newReads = 0;
        tabs.SetPublishingDispatcher(a => a());
        tabs.BindPublishingStatus(new Status(() => { entered.Set(); if (!release.Wait(Limit)) throw new TimeoutException(); return old; }));
        Assert.True(entered.Wait(Limit)); var worker = tabs.RefreshPublishingStatusAsync();
        tabs.BindPublishingStatus(new Status(() => { Interlocked.Increment(ref newReads); return next; }));
        Assert.Same(worker, tabs.RefreshPublishingStatusAsync()); Assert.Equal(0, newReads);
        release.Set(); await worker.WaitAsync(Limit);
        Assert.Equal(2, Assert.Single(tabs.PublishingRows).Generation); Assert.Equal(next.SessionId, tabs.PublishingRows[0].SessionId);
        Assert.Equal(1, newReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicationIsDispatchedAndDisposeBeforeDeliverySuppressesIt(bool dispose)
    {
        var tabs = Tabs(); var posted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queue = new ConcurrentQueue<Action>(); var notifications = new List<int>();
        tabs.PropertyChanged += (_, _) => notifications.Add(Environment.CurrentManagedThreadId);
        tabs.SetPublishingDispatcher(a => { queue.Enqueue(a); posted.TrySetResult(); });
        tabs.BindPublishingStatus(new Status(() => Snapshot(1)));
        await posted.Task.WaitAsync(Limit); await tabs.RefreshPublishingStatusAsync().WaitAsync(Limit);
        Assert.Empty(tabs.PublishingRows); Assert.Empty(notifications);
        if (dispose) await tabs.DisposeAsync();
        var uiThread = Environment.CurrentManagedThreadId;
        while (queue.TryDequeue(out var action)) action();
        if (dispose) { Assert.Empty(tabs.PublishingRows); Assert.Empty(notifications); }
        else { Assert.Single(tabs.PublishingRows); Assert.NotEmpty(notifications); Assert.All(notifications, id => Assert.Equal(uiThread, id)); await tabs.DisposeAsync(); }
    }

    [Fact]
    public async Task MixedStatusAndCommandGenerationIsUnavailable()
    {
        await using var tabs = Tabs(); tabs.SetPublishingDispatcher(a => a());
        tabs.BindPublishingStatus(new Status(() => Snapshot(1)), new Commands { Generation = 2 });
        await tabs.RefreshPublishingStatusAsync().WaitAsync(Limit);
        Assert.Equal(NovaObsPublishingAvailability.Unavailable, Assert.Single(tabs.PublishingRows).Availability);
        Assert.False(tabs.PublishingCommands.CanStart); Assert.False(tabs.PublishingCommands.CanFinish); Assert.False(tabs.PublishingCommands.CanInterrupt);
    }

    [Fact]
    public async Task OldQueuedPublicationCannotOverwriteReplacementGeneration()
    {
        var first = new Owner { AutoFinish = true }; var second = new Owner();
        await using var composition = NovaGuiNextPublishingComposition.Create(new Factory(first, second));
        await composition.RequestStartAsync();
        await using var tabs = Tabs(); var queue = new ConcurrentQueue<Action>();
        tabs.SetPublishingDispatcher(queue.Enqueue);
        tabs.BindPublishingStatus(composition, composition); await tabs.RefreshPublishingStatusAsync().WaitAsync(Limit);
        await composition.RequestFinishAsync(Limit); await composition.RequestStartAsync();
        Assert.Equal(2, composition.CurrentGeneration);
        while (queue.TryDequeue(out var action)) action();
        Assert.Empty(tabs.PublishingRows);
        await tabs.RefreshPublishingStatusAsync().WaitAsync(Limit);
        while (queue.TryDequeue(out var action)) action();
        Assert.Equal(2, Assert.Single(tabs.PublishingRows).Generation);
        Assert.Equal(second.Current.SessionId.Value, tabs.PublishingRows[0].SessionId);
    }

    [Fact]
    public async Task FinishAndInterruptAreAdmittedWhileGuiSnapshotRemainsBlocked()
    {
        var owner = new Owner(); var composition = NovaGuiNextPublishingComposition.Create(new Factory(owner));
        await composition.RequestStartAsync();
        var tabs = Tabs(); tabs.SelectedTab = GenesisGuiNextTab.Publishing;
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        owner.ReadSnapshot = () => { entered.Set(); if (!release.Wait(Limit)) throw new TimeoutException(); return owner.Current; };
        tabs.SetPublishingDispatcher(a => a()); tabs.BindPublishingStatus(composition, composition);
        Assert.True(entered.Wait(Limit)); var read = tabs.RefreshPublishingStatusAsync();
        var finish = tabs.RequestPublishingFinishAsync(); await tabs.PublishingCommandAdmission.WaitAsync(Limit);
        var interrupt = tabs.RequestPublishingInterruptAsync(); await tabs.PublishingCommandAdmission.WaitAsync(Limit);
        await owner.DisposeEntered.Task.WaitAsync(Limit);
        await tabs.DisposeAsync().AsTask().WaitAsync(Limit); composition.CloseAdmission();
        Assert.False(release.IsSet); Assert.False(read.IsCompleted);
        release.Set(); owner.ReadSnapshot = null;
        await Task.WhenAll(read, finish, interrupt).WaitAsync(Limit);
        var terminal = await composition.Lifecycle.Completion;
        Assert.Equal(NovaObsPublishingCompletionKind.Cancelled, terminal.Kind); Assert.False(terminal.LocalCompletedProven);
        Assert.Equal(1, owner.Disposals);
        if (terminal.CleanupCompleted) await composition.DisposeAsync();
        else await Assert.ThrowsAsync<InvalidOperationException>(() => composition.DisposeAsync().AsTask());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateFinishSharesTaskAndFirstDeadlineAtBothBoundaries(bool compositionBoundary)
    {
        var owner = new Owner(); var clock = new Clock();
        var composition = NovaGuiNextPublishingComposition.Create(new Factory(owner), clock);
        var lifecycle = compositionBoundary ? (NovaObsPublishingLifecycleController)composition.Lifecycle : new NovaObsPublishingLifecycleController(owner) { FinishTimeProvider = clock };
        var commands = new NovaGuiNextPublishingCommands(lifecycle);
        if (compositionBoundary) await composition.RequestStartAsync(); else await commands.RequestStartAsync();
        var first = compositionBoundary ? composition.RequestFinishAsync() : commands.RequestFinishAsync();
        await owner.FinishEntered.Task.WaitAsync(Limit);
        clock.Advance(TimeSpan.FromSeconds(29));
        var duplicate = compositionBoundary ? composition.RequestFinishAsync() : commands.RequestFinishAsync();
        Assert.Same(first, duplicate); Assert.Equal(1, clock.Timers);
        clock.Advance(TimeSpan.FromSeconds(2)); await first.WaitAsync(Limit);
        var result = await lifecycle.Completion;
        Assert.Equal(NovaObsPublishingCompletionKind.TimedOut, result.Kind); Assert.True(result.FinishDeadlineExpired);
        Assert.False(result.LocalCompletedProven); Assert.Equal(1, owner.Finishes);
        if (compositionBoundary) await Assert.ThrowsAsync<InvalidOperationException>(() => composition.DisposeAsync().AsTask());
        else await Assert.ThrowsAsync<InvalidOperationException>(() => commands.DisposeAsync().AsTask());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlockedStartFinishForcedAdmissionPrecedesStartRelease(bool dispose)
    {
        var owner = new Owner { BlockStart = true }; var clock = new Clock();
        var lifecycle = new NovaObsPublishingLifecycleController(owner) { FinishTimeProvider = clock };
        var commands = new NovaGuiNextPublishingCommands(lifecycle);
        await commands.RequestFinishAsync(); Assert.Equal(0, clock.Timers);
        var start = commands.RequestStartAsync(); await owner.StartEntered.Task.WaitAsync(Limit);
        var finish = commands.RequestFinishAsync(); Assert.Equal(1, clock.Timers);
        var forced = dispose ? commands.DisposeAsync().AsTask() : commands.RequestInterruptAsync();
        // Forced admission must already be visible before the synchronous Start settles.
        Assert.True(lifecycle.StopAdmissionObserved); Assert.False(owner.ReleaseStart.Task.IsCompleted);
        owner.ReleaseStart.TrySetResult(); await start.WaitAsync(Limit);
        await finish.WaitAsync(Limit);
        var result = await lifecycle.Completion;
        if (dispose && !result.CleanupCompleted) await Assert.ThrowsAsync<InvalidOperationException>(() => forced.WaitAsync(Limit));
        else await forced.WaitAsync(Limit);
        Assert.Equal(NovaObsPublishingCompletionKind.Cancelled, result.Kind); Assert.False(result.FinishDeadlineExpired);
        Assert.False(result.LocalCompletedProven); Assert.Equal(1, owner.Disposals);
        if (!dispose)
        {
            if (result.CleanupCompleted) await commands.DisposeAsync();
            else await Assert.ThrowsAsync<InvalidOperationException>(() => commands.DisposeAsync().AsTask());
        }
    }

    [Fact]
    public void NativeIdsLayoutAndLocalMeaningAreExplicit()
    {
        Assert.Equal(1134, GenesisGuiNextAppShellBridge.PublishingStartControlId);
        Assert.Equal(1135, GenesisGuiNextAppShellBridge.PublishingInterruptControlId);
        Assert.Equal(1136, GenesisGuiNextAppShellBridge.PublishingFinishControlId);
        var ids = new[] { GenesisGuiNextAppShellBridge.PublishingStartControlId, GenesisGuiNextAppShellBridge.PublishingInterruptControlId,
            GenesisGuiNextAppShellBridge.PublishingFinishControlId, GenesisGuiNextAppShellBridge.RelayStartControlId,
            GenesisGuiNextAppShellBridge.RelayStopControlId, GenesisGuiNextAppShellBridge.RelayStatusControlId };
        Assert.Equal(ids.Length, ids.Distinct().Count());
        var all = typeof(GenesisGuiNextAppShellBridge).GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Where(f => f.IsLiteral && f.FieldType == typeof(int) && f.Name.EndsWith("ControlId", StringComparison.Ordinal))
            .Select(f => (int)f.GetRawConstantValue()!).ToArray();
        Assert.Equal(all.Length, all.Distinct().Count());
        Assert.Contains("OBS正常EOSとは別", GenesisGuiNextPublishingPresentation.ScopeText);
        Assert.Equal("配信終了", GenesisGuiNextPublishingPresentation.FinishLabel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnprovenCleanupOrIdentityCannotEnableGuiRestart(bool identity)
    {
        var owner = new Owner { AutoFinish = true, ThrowDispose = !identity };
        if (identity) owner.BeforeDispose = () => owner.Current = owner.Current with { SessionId = new(Guid.NewGuid()) };
        var composition = NovaGuiNextPublishingComposition.Create(new Factory(owner));
        await composition.RequestStartAsync(); await composition.RequestFinishAsync();
        var result = await composition.Lifecycle.Completion;
        Assert.False(result.LocalCompletedProven); Assert.False(result.CleanupCompleted);
        if (identity) Assert.Equal(NovaObsPublishingLifecycleFailure.IdentityUnproven, result.Failure);
        Assert.False(composition.ReadCommands().CanStart); Assert.False(composition.ReadCommands().CanFinish);
        await composition.RequestStartAsync(); Assert.Equal(1, composition.CurrentGeneration); Assert.Equal(1, owner.Starts);
        await Assert.ThrowsAsync<InvalidOperationException>(() => composition.DisposeAsync().AsTask());
        Assert.Equal(1, owner.Disposals);
    }

    [Fact]
    public async Task CloseRejectsNewFinishButRetainsAlreadyAcceptedTask()
    {
        var owner = new Owner(); var composition = NovaGuiNextPublishingComposition.Create(new Factory(owner));
        await composition.RequestFinishAsync(); Assert.Equal(0, owner.Finishes);
        await composition.RequestStartAsync(); var finish = composition.RequestFinishAsync();
        await owner.FinishEntered.Task.WaitAsync(Limit);
        composition.CloseAdmission(); Assert.Same(finish, composition.RequestFinishAsync());
        owner.End(owner.Local()); await finish.WaitAsync(Limit); await composition.DisposeAsync();
        var next = new Owner(); var closed = NovaGuiNextPublishingComposition.Create(new Factory(next));
        await closed.RequestStartAsync(); closed.CloseAdmission(); await closed.RequestFinishAsync();
        Assert.Equal(0, next.Finishes); await closed.DisposeAsync();
    }

    [Fact]
    public async Task ReadExceptionProjectsOnlySafeUnavailableText()
    {
        await using var tabs = Tabs(); tabs.SetPublishingDispatcher(a => a());
        tabs.BindPublishingStatus(new Status(() => throw new InvalidOperationException("SECRET url/path/key")));
        await tabs.RefreshPublishingStatusAsync().WaitAsync(Limit);
        Assert.Equal(NovaObsPublishingAvailability.Unavailable, Assert.Single(tabs.PublishingRows).Availability);
        Assert.DoesNotContain("SECRET", System.Text.Json.JsonSerializer.Serialize(tabs.PublishingRows));
        Assert.False(tabs.PublishingCommands.CanFinish);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosedTerminalFailureSurvivesRepeatedRefreshWithoutBorrowedRead(bool pendingOwnerCompletion)
    {
        var owner = new Owner { HoldCompletion = pendingOwnerCompletion, ThrowDispose = !pendingOwnerCompletion };
        var composition = NovaGuiNextPublishingComposition.Create(new Factory(owner));
        var initial = composition.Read();
        await Assert.ThrowsAsync<InvalidOperationException>(() => composition.DisposeAsync().AsTask());
        var terminal = await composition.Lifecycle.Completion;
        Assert.Equal(NovaObsPublishingLifecycleState.Failed, terminal.State); Assert.False(terminal.CleanupCompleted);
        var reads = 0;
        owner.ReadSnapshot = () => { Interlocked.Increment(ref reads); throw new InvalidOperationException("SECRET raw exception"); };
        await using var tabs = Tabs(); tabs.SetPublishingDispatcher(a => a()); tabs.BindPublishingStatus(composition, composition);
        for (var i = 0; i < 3; i++)
        {
            await tabs.RefreshPublishingStatusAsync().WaitAsync(Limit);
            var snapshot = Assert.Single(tabs.PublishingRows);
            Assert.Equal(terminal.State, snapshot.LifecycleState); Assert.Equal(terminal.Failure, snapshot.LifecycleFailure);
            Assert.Equal(terminal.Kind, snapshot.CompletionKind); Assert.False(snapshot.CleanupCompleted);
            Assert.Equal(initial.ChannelId, snapshot.ChannelId); Assert.Equal(initial.SessionId, snapshot.SessionId);
            Assert.False(snapshot.CanRequestFinish); Assert.False(tabs.PublishingCommands.CanStart);
            Assert.False(tabs.PublishingCommands.CanInterrupt); Assert.False(tabs.PublishingCommands.CanFinish);
            Assert.DoesNotContain("SECRET", System.Text.Json.JsonSerializer.Serialize(snapshot));
        }
        Assert.Equal(0, reads); Assert.Equal(0, owner.Starts); Assert.Equal(1, owner.Disposals);
        Assert.Same(terminal, await composition.Lifecycle.Completion);
    }

    [Fact]
    public async Task ClosedFailurePresentationAndShutdownBypassAnActuallyBlockedAdapter()
    {
        var owner = new Owner { ThrowDispose = true };
        var composition = NovaGuiNextPublishingComposition.Create(new Factory(owner));
        composition.Read();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        owner.ReadSnapshot = () => { entered.Set(); if (!release.Wait(Limit)) throw new TimeoutException(); return owner.Current; };
        var blocked = Task.Run(composition.Read);
        try
        {
            Assert.True(entered.Wait(Limit));
            composition.CloseAdmission();
            await Assert.ThrowsAsync<InvalidOperationException>(() => composition.DisposeAsync().AsTask().WaitAsync(Limit));
            var observed = await Task.Run(composition.ReadPublishingPresentation).WaitAsync(Limit);
            Assert.False(release.IsSet); Assert.False(blocked.IsCompleted);
            Assert.Equal(NovaObsPublishingLifecycleState.Failed, observed.Snapshot.LifecycleState);
            Assert.False(observed.Snapshot.CleanupCompleted);
            Assert.False(observed.Commands.CanStart); Assert.False(observed.Commands.CanFinish); Assert.False(observed.Commands.CanInterrupt);
            await using var tabs = Tabs(); tabs.SetPublishingDispatcher(a => a()); tabs.BindPublishingStatus(composition, composition);
            await tabs.RefreshPublishingStatusAsync().WaitAsync(Limit);
            Assert.Equal(NovaObsPublishingLifecycleState.Failed, Assert.Single(tabs.PublishingRows).LifecycleState);
            Assert.False(blocked.IsCompleted); Assert.Equal(1, owner.Disposals);
        }
        finally { owner.ReadSnapshot = null; release.Set(); await blocked.WaitAsync(Limit); }
    }

    [Fact]
    public async Task PendingClosedTerminalNeverInventsCleanupOrNormalFinish()
    {
        var owner = new Owner { BlockDispose = true }; var composition = NovaGuiNextPublishingComposition.Create(new Factory(owner));
        composition.Read();
        var disposal = composition.DisposeAsync().AsTask();
        await owner.DisposeEntered.Task.WaitAsync(Limit);
        try
        {
            Assert.False(composition.Lifecycle.Completion.IsCompleted);
            var observed = composition.ReadPublishingPresentation();
            Assert.Equal(NovaObsPublishingLifecycleState.Stopping, observed.Snapshot.LifecycleState);
            Assert.Equal(NovaObsPublishingCompletionKind.None, observed.Snapshot.CompletionKind);
            Assert.False(observed.Snapshot.CleanupCompleted); Assert.False(observed.Snapshot.CanRequestFinish);
            Assert.False(observed.Commands.CanStart); Assert.False(observed.Commands.CanInterrupt); Assert.False(observed.Commands.CanFinish);
            Assert.DoesNotContain("OBS正常EOS", observed.Snapshot.StateText);
            Assert.DoesNotContain("Novaローカル", observed.Snapshot.StateText);
        }
        finally { owner.ReleaseDispose.TrySetResult(); await disposal.WaitAsync(Limit); }
        var terminal = await composition.Lifecycle.Completion;
        Assert.False(terminal.LocalCompletedProven); Assert.False(terminal.NormalEosProven);
        Assert.Equal(0, owner.Starts); Assert.Equal(1, owner.Disposals);
    }

    [Fact]
    public async Task DelayedFailedTerminalCannotPublishIntoNewBindingGeneration()
    {
        var old = NovaGuiNextPublishingComposition.Create(new Factory(new Owner { ThrowDispose = true }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => old.DisposeAsync().AsTask());
        var failed = old.ReadPublishingPresentation().Snapshot;
        var next = new Owner(); await using var current = NovaGuiNextPublishingComposition.Create(new Factory(next));
        await current.RequestStartAsync();
        await using var tabs = Tabs(); tabs.SetPublishingDispatcher(a => a());
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        tabs.BindPublishingStatus(new Status(() => { entered.Set(); if (!release.Wait(Limit)) throw new TimeoutException(); return failed; }));
        Assert.True(entered.Wait(Limit));
        var blocked = tabs.RefreshPublishingStatusAsync();
        try { tabs.BindPublishingStatus(current, current); Assert.Same(blocked, tabs.RefreshPublishingStatusAsync()); }
        finally { release.Set(); await blocked.WaitAsync(Limit); }
        var row = Assert.Single(tabs.PublishingRows);
        Assert.Equal(next.Current.SessionId.Value, row.SessionId); Assert.Equal(current.CurrentGeneration, row.Generation);
        Assert.Equal(NovaObsPublishingLifecycleState.Running, row.LifecycleState);
        Assert.NotEqual(failed.SessionId, row.SessionId); Assert.True(tabs.PublishingCommands.CanFinish);
    }

    internal sealed class Clock : TimeProvider
    {
        private long _ticks; private readonly List<Action> _callbacks = [];
        internal int Timers => _callbacks.Count;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        internal void Advance(TimeSpan value) { Interlocked.Add(ref _ticks, value.Ticks); foreach (var fire in _callbacks.ToArray()) fire(); }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { var deadline = GetTimestamp() + dueTime.Ticks; var timer = new Timer(); _callbacks.Add(() => { if (!timer.Disposed && GetTimestamp() >= deadline) callback(state); }); return timer; }
        private sealed class Timer : ITimer
        {
            internal bool Disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
