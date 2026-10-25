using System.Collections.Concurrent;
using Owner = PeerCast.Nova.App.Tests.NovaObsPublishingFinishTests.Owner;

namespace PeerCast.Nova.App.Tests;

public sealed class PublishingRebindingCommandIdentityTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private sealed class Factory(params Owner[] owners) : INovaObsPublishingRuntimeFactory
    {
        private int _index;
        public INovaObsPublishingRuntime Create() => owners[Interlocked.Increment(ref _index) - 1];
    }

    private static GenesisGuiNextTabsViewModel Tabs() => new(new GenesisFavoriteChannelCollection()) { SelectedTab = GenesisGuiNextTab.Publishing };
    private static Task Command(GenesisGuiNextTabsViewModel tabs, int command) => command switch
    {
        0 => tabs.RequestPublishingStartAsync(),
        1 => tabs.RequestPublishingInterruptAsync(),
        _ => tabs.RequestPublishingFinishAsync(),
    };
    private static void Deliver(ConcurrentQueue<Action> queue)
    {
        while (queue.TryDequeue(out var action)) action();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SameGenerationDifferentBindingRejectsOldDisplayThenAdmitsNewDisplay(int command)
    {
        var old = new Owner(); var next = new Owner { AutoFinish = true };
        await using var a = NovaGuiNextPublishingComposition.Create(new Factory(old));
        await using var b = NovaGuiNextPublishingComposition.Create(new Factory(next));
        await a.RequestStartAsync();
        if (command != 0) await b.RequestStartAsync();
        await using var tabs = Tabs(); var queue = new ConcurrentQueue<Action>();
        tabs.SetPublishingDispatcher(queue.Enqueue); tabs.BindPublishingStatus(a, a);
        await tabs.RefreshPublishingStatusAsync().WaitAsync(Limit); Deliver(queue);
        Assert.True(tabs.PublishingCommands.CanFinish);
        var oldRow = Assert.Single(tabs.PublishingRows);
        Assert.Equal(1, a.CurrentGeneration); Assert.Equal(a.CurrentGeneration, b.CurrentGeneration);
        Assert.NotEqual(old.Current.SessionId, next.Current.SessionId);
        var state = b.Lifecycle.Read().LifecycleState;
        var starts = next.Starts;
        tabs.BindPublishingStatus(b, b);
        await tabs.RefreshPublishingStatusAsync().WaitAsync(Limit); // B publication is held, not guessed from scheduling.
        Assert.Same(oldRow, Assert.Single(tabs.PublishingRows));
        var admission = tabs.PublishingCommandAdmission;
        await Command(tabs, command).WaitAsync(Limit);
        Assert.Same(admission, tabs.PublishingCommandAdmission);
        Assert.Equal(starts, next.Starts); Assert.Equal(0, next.Finishes); Assert.Equal(0, next.Disposals);
        Assert.Equal(state, b.Lifecycle.Read().LifecycleState); Assert.False(b.Lifecycle.Completion.IsCompleted);
        Deliver(queue);
        Assert.Equal(next.Current.SessionId.Value, Assert.Single(tabs.PublishingRows).SessionId);
        Assert.Equal(1, tabs.PublishingCommands.Generation);
        await Command(tabs, command).WaitAsync(Limit);
        if (command == 0) { Assert.Equal(1, next.Starts); Assert.Equal(NovaObsPublishingLifecycleState.Running, b.Lifecycle.Read().LifecycleState); }
        else
        {
            var terminal = await b.Lifecycle.Completion;
            Assert.Equal(1, next.Disposals);
            Assert.Equal(command == 1 ? NovaObsPublishingCompletionKind.Cancelled : NovaObsPublishingCompletionKind.LocalCompleted, terminal.Kind);
            Assert.Equal(command == 2 ? 1 : 0, next.Finishes);
        }
        Assert.Equal(0, old.Finishes); Assert.Equal(0, old.Disposals);
    }

    [Fact]
    public async Task InitialUnpublishedBindingRejectsUnstartedFinishButAllowsExplicitStart()
    {
        var owner = new Owner(); await using var composition = NovaGuiNextPublishingComposition.Create(new Factory(owner));
        await using var tabs = Tabs(); var queue = new ConcurrentQueue<Action>();
        tabs.SetPublishingDispatcher(queue.Enqueue); tabs.BindPublishingStatus(composition, composition);
        await tabs.RefreshPublishingStatusAsync().WaitAsync(Limit);
        Assert.Empty(tabs.PublishingRows);
        await tabs.RequestPublishingFinishAsync().WaitAsync(Limit);
        Assert.Equal(0, owner.Finishes); Assert.Equal(0, owner.Starts);
        await tabs.RequestPublishingStartAsync().WaitAsync(Limit);
        Assert.Empty(tabs.PublishingRows); Assert.Equal(1, owner.Starts);
        Assert.Equal(NovaObsPublishingLifecycleState.Running, composition.Lifecycle.Read().LifecycleState);
        await tabs.RefreshPublishingStatusAsync().WaitAsync(Limit); Deliver(queue);
        Assert.Equal(owner.Current.SessionId.Value, Assert.Single(tabs.PublishingRows).SessionId);
        Assert.True(tabs.PublishingCommands.CanFinish);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task WorkerMustRevalidateBindingAfterQueueing(int command)
    {
        var old = new Owner(); var next = new Owner();
        await using var a = NovaGuiNextPublishingComposition.Create(new Factory(old));
        await using var b = NovaGuiNextPublishingComposition.Create(new Factory(next));
        if (command != 0) await a.RequestStartAsync();
        await using var tabs = Tabs(); tabs.SetPublishingDispatcher(action => action());
        tabs.BindPublishingStatus(a, a); await tabs.RefreshPublishingStatusAsync().WaitAsync(Limit);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tabs.PublishingCommandWorkerStarting = () => { entered.TrySetResult(); return release.Task; };
        var pending = Command(tabs, command);
        try
        {
            await entered.Task.WaitAsync(Limit);
            tabs.BindPublishingStatus(b, b); await tabs.RefreshPublishingStatusAsync().WaitAsync(Limit);
            Assert.Equal(next.Current.SessionId.Value, Assert.Single(tabs.PublishingRows).SessionId);
            Assert.False(pending.IsCompleted);
        }
        finally { release.TrySetResult(); await pending.WaitAsync(Limit); }
        Assert.Equal(command == 0 ? 0 : 1, old.Starts); Assert.Equal(0, old.Finishes); Assert.Equal(0, old.Disposals);
        Assert.Equal(0, next.Starts); Assert.Equal(0, next.Finishes); Assert.Equal(0, next.Disposals);
        Assert.False(a.Lifecycle.Completion.IsCompleted); Assert.False(b.Lifecycle.Completion.IsCompleted);
    }

    [Fact]
    public async Task ReturningToSameBoundaryDoesNotResurrectAnOldPublishedIdentity()
    {
        var old = new Owner { AutoFinish = true }; var next = new Owner();
        await using var a = NovaGuiNextPublishingComposition.Create(new Factory(old));
        await using var b = NovaGuiNextPublishingComposition.Create(new Factory(next));
        await a.RequestStartAsync();
        await using var tabs = Tabs(); var queue = new ConcurrentQueue<Action>();
        tabs.SetPublishingDispatcher(queue.Enqueue); tabs.BindPublishingStatus(a, a);
        await tabs.RefreshPublishingStatusAsync().WaitAsync(Limit); Deliver(queue);
        tabs.BindPublishingStatus(b, b); await tabs.RefreshPublishingStatusAsync().WaitAsync(Limit);
        tabs.BindPublishingStatus(a, a); await tabs.RefreshPublishingStatusAsync().WaitAsync(Limit);
        for (var command = 0; command < 3; command++) await Command(tabs, command).WaitAsync(Limit);
        Assert.Equal(0, old.Finishes); Assert.Equal(0, old.Disposals); Assert.Equal(0, next.Starts);
        Deliver(queue);
        Assert.Equal(old.Current.SessionId.Value, Assert.Single(tabs.PublishingRows).SessionId);
        await tabs.RequestPublishingFinishAsync().WaitAsync(Limit);
        Assert.Equal(1, old.Finishes); Assert.Equal(1, old.Disposals);
        Assert.Equal(0, next.Finishes); Assert.Equal(0, next.Disposals);
    }

    [Fact]
    public async Task DisposeDuringRebindingRejectsCommandsAndAllQueuedPublications()
    {
        var old = new Owner(); var next = new Owner();
        await using var a = NovaGuiNextPublishingComposition.Create(new Factory(old));
        await using var b = NovaGuiNextPublishingComposition.Create(new Factory(next));
        await a.RequestStartAsync(); await b.RequestStartAsync();
        var tabs = Tabs(); var queue = new ConcurrentQueue<Action>();
        tabs.SetPublishingDispatcher(queue.Enqueue); tabs.BindPublishingStatus(a, a);
        await tabs.RefreshPublishingStatusAsync().WaitAsync(Limit); Deliver(queue);
        var rows = tabs.PublishingRows;
        tabs.BindPublishingStatus(b, b); await tabs.RefreshPublishingStatusAsync().WaitAsync(Limit);
        await tabs.DisposeAsync(); var notifications = 0;
        tabs.PropertyChanged += (_, _) => notifications++;
        for (var command = 0; command < 3; command++) await Command(tabs, command).WaitAsync(Limit);
        Deliver(queue);
        Assert.Same(rows, tabs.PublishingRows); Assert.Equal(0, notifications);
        Assert.Equal(1, next.Starts); Assert.Equal(0, next.Finishes); Assert.Equal(0, next.Disposals);
        Assert.False(b.Lifecycle.Completion.IsCompleted);
    }

    [Fact]
    public async Task BlockedOldReadCannotAuthorizeReboundCommandsOrPublishOldIdentity()
    {
        var old = new Owner(); var next = new Owner();
        await using var a = NovaGuiNextPublishingComposition.Create(new Factory(old));
        await using var b = NovaGuiNextPublishingComposition.Create(new Factory(next));
        await a.RequestStartAsync(); await b.RequestStartAsync();
        await using var tabs = Tabs(); tabs.SetPublishingDispatcher(action => action());
        tabs.BindPublishingStatus(a, a); await tabs.RefreshPublishingStatusAsync().WaitAsync(Limit);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        old.ReadSnapshot = () => { entered.Set(); if (!release.Wait(Limit)) throw new TimeoutException(); return old.Current; };
        var read = tabs.RefreshPublishingStatusAsync();
        try
        {
            Assert.True(entered.Wait(Limit)); tabs.BindPublishingStatus(b, b);
            Assert.Same(read, tabs.RefreshPublishingStatusAsync());
            for (var command = 0; command < 3; command++) await Command(tabs, command).WaitAsync(Limit);
            Assert.Equal(old.Current.SessionId.Value, Assert.Single(tabs.PublishingRows).SessionId);
            Assert.Equal(1, next.Starts); Assert.Equal(0, next.Finishes); Assert.Equal(0, next.Disposals);
            Assert.False(release.IsSet); Assert.False(read.IsCompleted);
        }
        finally { old.ReadSnapshot = null; release.Set(); await read.WaitAsync(Limit); }
        Assert.Equal(next.Current.SessionId.Value, Assert.Single(tabs.PublishingRows).SessionId);
        Assert.Equal(1, tabs.PublishingCommands.Generation); Assert.True(tabs.PublishingCommands.CanFinish);
    }
}
