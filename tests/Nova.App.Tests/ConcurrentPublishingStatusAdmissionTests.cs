using Owner = PeerCast.Nova.App.Tests.NovaObsPublishingFinishTests.Owner;

namespace PeerCast.Nova.App.Tests;

public sealed class ConcurrentPublishingStatusAdmissionTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class Factory(Func<INovaObsPublishingRuntime> create) : INovaObsPublishingRuntimeFactory
    { public INovaObsPublishingRuntime Create() => create(); }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinishReadContentionCannotOwnCompositionAdmission(bool commandsRead)
    {
        var owner = new Owner(); var clock = new ControlledClock();
        var composition = NovaGuiNextPublishingComposition.Create(new Factory(() => owner), clock);
        await composition.RequestStartAsync();
        var controller = Assert.IsType<NovaObsPublishingLifecycleController>(composition.Lifecycle);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        owner.ReadSnapshot = () => { entered.Set(); if (!release.Wait(Limit)) throw new TimeoutException(); return owner.Current; };
        Task<object>? read = null;
        try
        {
            var finish = composition.RequestFinishAsync(Limit);
            Assert.True(entered.Wait(Limit));
            var contended = Signal(); controller.StatusReadContentionObserver = () => contended.TrySetResult();
            read = Task.Run(() => commandsRead ? (object)composition.ReadCommands() : composition.Read());
            await contended.Task.WaitAsync(Limit); // Adapter TryEnter actually failed, not just scheduling of Read.
            Assert.False(read.IsCompleted);
            var duplicate = await Task.Factory.StartNew(() => composition.RequestFinishAsync(Limit),
                CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default).WaitAsync(Limit);
            Assert.Same(finish, duplicate);
            await Task.Run(composition.CloseAdmission).WaitAsync(Limit);
            var disposal = await Task.Factory.StartNew(() => composition.DisposeAsync().AsTask(),
                CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default).WaitAsync(Limit);
            await owner.DisposeEntered.Task.WaitAsync(Limit);
            Assert.False(release.IsSet); Assert.False(read.IsCompleted);
            var closedCommands = await Task.Run(composition.ReadCommands).WaitAsync(Limit);
            Assert.False(closedCommands.CanStart); Assert.False(closedCommands.CanInterrupt);
            clock.AdvanceAndFire(Limit + TimeSpan.FromTicks(1));
            var result = await finish.WaitAsync(Limit);
            Assert.Equal(NovaObsPublishingCompletionKind.TimedOut, result.Kind);
            Assert.True(result.FinishDeadlineExpired);
            Assert.False(result.CleanupCompleted); Assert.False(result.LocalCompletedProven);
            await Assert.ThrowsAsync<InvalidOperationException>(() => disposal.WaitAsync(Limit));
            release.Set();
            var observed = await read.WaitAsync(Limit);
            if (commandsRead)
            {
                var state = Assert.IsType<NovaGuiNextPublishingCommandState>(observed);
                Assert.False(state.CanStart); Assert.False(state.CanInterrupt);
            }
            Assert.Same(result, await controller.Completion);
            Assert.Equal(1, owner.Disposals); Assert.Equal(0, owner.Finishes);
        }
        finally
        {
            controller.StatusReadContentionObserver = null; owner.ReadSnapshot = null; release.Set();
            if (read is not null) await read.WaitAsync(Limit);
        }
    }

    [Fact]
    public async Task CommandReadCannotOwnCloseOrDisposeAdmission()
    {
        var owner = new Owner(); var lifecycle = new NovaObsPublishingLifecycleController(owner);
        var commands = new NovaGuiNextPublishingCommands(lifecycle);
        await commands.RequestStartAsync();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        owner.ReadSnapshot = () => { entered.Set(); if (!release.Wait(Limit)) throw new TimeoutException(); return owner.Current; };
        var read = Task.Run(commands.ReadCommands);
        try
        {
            Assert.True(entered.Wait(Limit));
            await Task.Run(commands.CloseAdmission).WaitAsync(Limit);
            var disposal = await Task.Factory.StartNew(() => commands.DisposeAsync().AsTask(),
                CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default).WaitAsync(Limit);
            await disposal.WaitAsync(Limit);
            Assert.False(read.IsCompleted); Assert.False(release.IsSet);
            release.Set();
            var state = await read.WaitAsync(Limit);
            Assert.False(state.CanStart); Assert.False(state.CanInterrupt);
            Assert.Equal(1, owner.Disposals);
        }
        finally { owner.ReadSnapshot = null; release.Set(); await read.WaitAsync(Limit); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CloseDuringStartOrInterruptReadRejectsStaleAdmission(bool compositionBoundary, bool interrupt)
    {
        var owner = new Owner();
        var composition = compositionBoundary ? NovaGuiNextPublishingComposition.Create(new Factory(() => owner)) : null;
        var lifecycle = composition is null ? new NovaObsPublishingLifecycleController(owner) :
            Assert.IsType<NovaObsPublishingLifecycleController>(composition.Lifecycle);
        var commands = composition is null ? new NovaGuiNextPublishingCommands(lifecycle) : null;
        if (interrupt)
        {
            if (composition is not null) await composition.RequestStartAsync();
            else await commands!.RequestStartAsync();
        }
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        owner.ReadSnapshot = () => { entered.Set(); if (!release.Wait(Limit)) throw new TimeoutException(); return owner.Current; };
        var operation = Task.Run(() => composition is not null
            ? interrupt ? composition.RequestInterruptAsync() : composition.RequestStartAsync()
            : interrupt ? commands!.RequestInterruptAsync() : commands!.RequestStartAsync());
        try
        {
            Assert.True(entered.Wait(Limit));
            await Task.Run(() => { if (composition is not null) composition.CloseAdmission(); else commands!.CloseAdmission(); }).WaitAsync(Limit);
            var disposal = await Task.Factory.StartNew(() => composition is not null ? composition.DisposeAsync().AsTask() : commands!.DisposeAsync().AsTask(),
                CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default).WaitAsync(Limit);
            await disposal.WaitAsync(Limit);
            Assert.False(release.IsSet); Assert.False(operation.IsCompleted);
            release.Set(); await operation.WaitAsync(Limit);
            Assert.Equal(interrupt ? 1 : 0, owner.Starts);
            Assert.Equal(1, owner.Disposals); Assert.Equal(0, owner.Finishes);
            var state = composition is not null ? composition.ReadCommands() : commands!.ReadCommands();
            Assert.False(state.CanStart); Assert.False(state.CanInterrupt);
        }
        finally { owner.ReadSnapshot = null; release.Set(); await operation.WaitAsync(Limit); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RetiredReadCannotPublishIntoReplacementOrReopenClosedCommands(bool commandsRead, bool close)
    {
        var first = new Owner { AutoFinish = true }; var second = new Owner(); var creates = 0;
        var composition = NovaGuiNextPublishingComposition.Create(new Factory(() => Interlocked.Increment(ref creates) == 1 ? first : second));
        await composition.RequestStartAsync(); await composition.RequestFinishAsync(Limit);
        using var prepared = new ManualResetEventSlim(); using var admit = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        composition.CommandObservationReady = () => { prepared.Set(); if (!admit.Wait(Limit)) throw new TimeoutException(); };
        var replacement = Task.Run(composition.RequestStartAsync);
        Task<object>? read = null;
        try
        {
            Assert.True(prepared.Wait(Limit)); // Valid terminal observation is prepared, not yet admitted.
            first.ReadSnapshot = () => { var captured = first.Current; entered.Set(); if (!release.Wait(Limit)) throw new TimeoutException(); return captured; };
            read = Task.Run(() => commandsRead ? (object)composition.ReadCommands() : composition.Read());
            Assert.True(entered.Wait(Limit));
            admit.Set(); await replacement.WaitAsync(Limit);
            var current = composition.Read();
            Assert.Equal(2, current.Generation); Assert.Equal(second.Current.SessionId.Value, current.SessionId);
            Assert.False(read.IsCompleted);
            if (close) await composition.DisposeAsync().AsTask().WaitAsync(Limit);
            release.Set();
            var stale = await read.WaitAsync(Limit);
            if (commandsRead)
            {
                var state = Assert.IsType<NovaGuiNextPublishingCommandState>(stale);
                Assert.False(state.CanStart); Assert.False(state.CanInterrupt);
            }
            else
            {
                var snapshot = Assert.IsType<NovaObsPublishingReadOnlySnapshot>(stale);
                Assert.Equal(2, snapshot.Generation); Assert.Equal(second.Current.SessionId.Value, snapshot.SessionId);
                Assert.Equal(NovaObsPublishingAvailability.Unavailable, snapshot.Availability);
            }
            Assert.Equal(current.SessionId, composition.Read().SessionId);
            Assert.Equal(2, creates); Assert.Equal(1, second.Starts);
            Assert.Equal(close ? 1 : 0, second.Disposals);
            Assert.False(composition.ReadCommands().CanStart);
            Assert.Equal(!close, composition.ReadCommands().CanInterrupt);
        }
        finally
        {
            composition.CommandObservationReady = null; first.ReadSnapshot = null; admit.Set(); release.Set();
            await replacement.WaitAsync(Limit);
            if (read is not null) await read.WaitAsync(Limit);
            await composition.DisposeAsync().AsTask().WaitAsync(Limit);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreparedOldStartOrInterruptCannotOperateOnNewGeneration(bool interrupt)
    {
        var first = new Owner { AutoFinish = true }; var second = new Owner(); var creates = 0;
        var composition = NovaGuiNextPublishingComposition.Create(new Factory(() => Interlocked.Increment(ref creates) == 1 ? first : second));
        await composition.RequestStartAsync();
        if (!interrupt) await composition.RequestFinishAsync(Limit);
        using var observed = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); var hooks = 0;
        composition.CommandObservationReady = () =>
        {
            if (Interlocked.Increment(ref hooks) == 1) { observed.Set(); if (!release.Wait(Limit)) throw new TimeoutException(); }
        };
        var stale = Task.Run(() => interrupt ? composition.RequestInterruptAsync() : composition.RequestStartAsync());
        try
        {
            Assert.True(observed.Wait(Limit));
            if (interrupt) await composition.RequestFinishAsync(Limit);
            await composition.RequestStartAsync().WaitAsync(Limit);
            Assert.Equal(2, composition.Read().Generation);
            release.Set(); await stale.WaitAsync(Limit);
            Assert.Equal(2, creates); Assert.Equal(1, second.Starts); Assert.Equal(0, second.Disposals);
            Assert.Equal(second.Current.SessionId.Value, composition.Read().SessionId);
        }
        finally
        {
            composition.CommandObservationReady = null; release.Set(); await stale.WaitAsync(Limit);
            await composition.DisposeAsync().AsTask().WaitAsync(Limit);
        }
    }

    private sealed class ControlledClock : TimeProvider
    {
        private long _ticks; private Action? _fire;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public void AdvanceAndFire(TimeSpan time) { Interlocked.Add(ref _ticks, time.Ticks); (_fire ?? throw new InvalidOperationException())(); }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { _fire = () => callback(state); return new Timer(); }
        private sealed class Timer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
