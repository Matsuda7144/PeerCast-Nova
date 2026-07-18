namespace PeerCast.Nova.Playback.Tests;

public sealed class FiniteContinuousPlaybackPumpPolicyBoundaryTests
{
    [Fact]
    public async Task SuccessfulItemsReachFinitePumpLimit()
    {
        var adapter = new RecordingAdapter(PlaybackWindowsOutputSchedulerAdapterResult.VideoProcessed);
        using var policy = Create(adapter, new StepClock());
        Assert.Equal(FiniteContinuousPlaybackPumpResult.PumpLimitReached,
            await policy.RunAsync(Options(pumps: 3), default));
        Assert.Equal(3, adapter.PumpCalls);
        Assert.Equal(3, policy.Snapshot.PumpCount);
    }

    [Fact]
    public async Task DurationLimitStopsBeforeAnotherPump()
    {
        var adapter = new RecordingAdapter(PlaybackWindowsOutputSchedulerAdapterResult.VideoProcessed);
        using var policy = Create(adapter, new SequenceClock(
            TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMilliseconds(60)));
        Assert.Equal(FiniteContinuousPlaybackPumpResult.DurationLimitReached,
            await policy.RunAsync(Options(duration: TimeSpan.FromMilliseconds(50)), default));
        Assert.Equal(1, adapter.PumpCalls);
    }

    [Theory]
    [InlineData(PlaybackWindowsOutputSchedulerAdapterResult.Empty)]
    [InlineData(PlaybackWindowsOutputSchedulerAdapterResult.WouldBlock)]
    public async Task IdleAndBackpressureDelayWithoutBusySpin(
        PlaybackWindowsOutputSchedulerAdapterResult result)
    {
        var delay = new RecordingDelay();
        using var policy = Create(new RecordingAdapter(result), new StepClock(), delay);
        Assert.Equal(FiniteContinuousPlaybackPumpResult.IdleLimitReached,
            await policy.RunAsync(Options(idle: 2), default));
        Assert.Equal([TimeSpan.FromMilliseconds(5)], delay.Delays);
    }

    [Fact]
    public async Task StopInterruptsDelayAndIsIdempotent()
    {
        var delay = new BlockingDelay();
        using var policy = Create(
            new RecordingAdapter(PlaybackWindowsOutputSchedulerAdapterResult.Empty),
            new StepClock(), delay);
        var run = policy.RunAsync(Options(), default).AsTask();
        await delay.Entered.Task;
        Assert.Equal(FiniteContinuousPlaybackPumpStopResult.Stopped,
            await policy.StopAsync());
        Assert.Equal(FiniteContinuousPlaybackPumpResult.Stopped, await run);
        Assert.Equal(FiniteContinuousPlaybackPumpStopResult.NotRunning,
            await policy.StopAsync());
    }

    [Fact]
    public async Task CallerCancellationHasFixedResult()
    {
        var delay = new BlockingDelay();
        using var policy = Create(new RecordingAdapter(
            PlaybackWindowsOutputSchedulerAdapterResult.WouldBlock), new StepClock(), delay);
        using var cancellation = new CancellationTokenSource();
        var run = policy.RunAsync(Options(), cancellation.Token).AsTask();
        await delay.Entered.Task;
        cancellation.Cancel();
        Assert.Equal(FiniteContinuousPlaybackPumpResult.Cancelled, await run);
    }

    [Fact]
    public async Task ConcurrentRunIsRejected()
    {
        var delay = new BlockingDelay();
        using var policy = Create(new RecordingAdapter(
            PlaybackWindowsOutputSchedulerAdapterResult.Empty), new StepClock(), delay);
        var first = policy.RunAsync(Options(), default).AsTask();
        await delay.Entered.Task;
        Assert.Equal(FiniteContinuousPlaybackPumpResult.AlreadyRunning,
            await policy.RunAsync(Options(), default));
        await policy.StopAsync();
        await first;
    }

    [Theory]
    [InlineData(PlaybackWindowsOutputSchedulerAdapterResult.DeviceLost,
        FiniteContinuousPlaybackPumpResult.DeviceLost)]
    [InlineData(PlaybackWindowsOutputSchedulerAdapterResult.ResizeRequired,
        FiniteContinuousPlaybackPumpResult.ResizeRequired)]
    [InlineData(PlaybackWindowsOutputSchedulerAdapterResult.ResetRequired,
        FiniteContinuousPlaybackPumpResult.ResetRequired)]
    [InlineData(PlaybackWindowsOutputSchedulerAdapterResult.InitializationRequired,
        FiniteContinuousPlaybackPumpResult.ResetRequired)]
    [InlineData(PlaybackWindowsOutputSchedulerAdapterResult.BindingRequired,
        FiniteContinuousPlaybackPumpResult.ResetRequired)]
    public async Task RecoveryResultsStopRun(
        PlaybackWindowsOutputSchedulerAdapterResult lower,
        FiniteContinuousPlaybackPumpResult expected)
    {
        using var policy = Create(new RecordingAdapter(lower), new StepClock());
        Assert.Equal(expected, await policy.RunAsync(Options(), default));
    }

    [Theory]
    [InlineData(PlaybackWindowsOutputSchedulerAdapterResult.InvalidItem)]
    [InlineData(PlaybackWindowsOutputSchedulerAdapterResult.FormatUnsupported)]
    public async Task InvalidAndUndefinedPumpResultsFailClosed(
        PlaybackWindowsOutputSchedulerAdapterResult value)
    {
        using var policy = Create(new RecordingAdapter(
            value), new StepClock());
        Assert.Equal(FiniteContinuousPlaybackPumpResult.Failure,
            await policy.RunAsync(Options(), default));
        Assert.Equal(FiniteContinuousPlaybackPumpState.Faulted, policy.Snapshot.State);
    }

    [Fact]
    public async Task UndefinedPumpResultFailsClosed()
    {
        using var policy = Create(new RecordingAdapter(
            (PlaybackWindowsOutputSchedulerAdapterResult)99), new StepClock());
        Assert.Equal(FiniteContinuousPlaybackPumpResult.Failure,
            await policy.RunAsync(Options(), default));
    }

    [Fact]
    public async Task AdapterAndClockExceptionsFailClosed()
    {
        using var adapterFailure = Create(new RecordingAdapter(
            PlaybackWindowsOutputSchedulerAdapterResult.VideoProcessed)
        { ThrowPump = true },
            new StepClock());
        using var clockFailure = Create(new RecordingAdapter(
            PlaybackWindowsOutputSchedulerAdapterResult.VideoProcessed), new ThrowingClock());
        Assert.Equal(FiniteContinuousPlaybackPumpResult.Failure,
            await adapterFailure.RunAsync(Options(), default));
        Assert.Equal(FiniteContinuousPlaybackPumpResult.Failure,
            await clockFailure.RunAsync(Options(), default));
    }

    [Fact]
    public async Task RegressingAndInvalidClockFailClosed()
    {
        using var regressing = Create(new RecordingAdapter(
            PlaybackWindowsOutputSchedulerAdapterResult.VideoProcessed),
            new SequenceClock(TimeSpan.FromTicks(2), TimeSpan.FromTicks(1)));
        using var invalid = Create(new RecordingAdapter(
            PlaybackWindowsOutputSchedulerAdapterResult.VideoProcessed),
            new SequenceClock(TimeSpan.MinValue));
        Assert.Equal(FiniteContinuousPlaybackPumpResult.Failure,
            await regressing.RunAsync(Options(), default));
        Assert.Equal(FiniteContinuousPlaybackPumpResult.Failure,
            await invalid.RunAsync(Options(), default));
    }

    [Fact]
    public async Task ResetSuccessReturnsIdleAndPartialFaultRequiresReset()
    {
        var adapter = new RecordingAdapter(PlaybackWindowsOutputSchedulerAdapterResult.ResetRequired);
        adapter.ResetResults.Enqueue(PlaybackWindowsOutputSchedulerAdapterResetResult.PartialResetFault);
        adapter.ResetResults.Enqueue(PlaybackWindowsOutputSchedulerAdapterResetResult.Reset);
        using var policy = Create(adapter, new StepClock());
        Assert.Equal(FiniteContinuousPlaybackPumpResult.ResetRequired,
            await policy.RunAsync(Options(), default));
        Assert.Equal(FiniteContinuousPlaybackPumpResetResult.ResetRequired,
            await policy.ResetAsync(8, 12, default));
        Assert.Equal(FiniteContinuousPlaybackPumpState.ResetRequired, policy.Snapshot.State);
        Assert.Equal(FiniteContinuousPlaybackPumpResetResult.Reset,
            await policy.ResetAsync(9, 13, default));
        Assert.Equal(FiniteContinuousPlaybackPumpState.Idle, policy.Snapshot.State);
    }

    [Fact]
    public async Task ResetAndDisposeStopInflightRunWithoutOwningAdapter()
    {
        var adapter = new RecordingAdapter(PlaybackWindowsOutputSchedulerAdapterResult.Empty);
        var delay = new BlockingDelay();
        var policy = Create(adapter, new StepClock(), delay);
        var run = policy.RunAsync(Options(), default).AsTask();
        await delay.Entered.Task;
        Assert.Equal(FiniteContinuousPlaybackPumpResetResult.Reset,
            await policy.ResetAsync(8, 12, default));
        Assert.Equal(FiniteContinuousPlaybackPumpResult.Stopped, await run);
        policy.Dispose();
        policy.Dispose();
        Assert.False(adapter.Disposed);
        Assert.Equal(FiniteContinuousPlaybackPumpResult.Disposed,
            await policy.RunAsync(Options(), default));
    }

    [Fact]
    public async Task BlockingResetExcludesRunResetAndStopUntilSuccess()
    {
        var adapter = new RecordingAdapter(
            PlaybackWindowsOutputSchedulerAdapterResult.VideoProcessed)
        { BlockReset = true, IgnoreResetCancellation = true };
        using var policy = Create(adapter, new StepClock());
        var reset = policy.ResetAsync(8, 12, default).AsTask();
        await adapter.ResetEntered.Task;

        Assert.Equal(FiniteContinuousPlaybackPumpState.Resetting, policy.Snapshot.State);
        Assert.Equal(FiniteContinuousPlaybackPumpResult.ResetInProgress,
            await policy.RunAsync(Options(), default));
        Assert.Equal(FiniteContinuousPlaybackPumpResetResult.AlreadyResetting,
            await policy.ResetAsync(9, 13, default));
        Assert.Equal(FiniteContinuousPlaybackPumpStopResult.ResetInProgress,
            await policy.StopAsync());
        Assert.Equal(0, adapter.PumpCalls);

        adapter.ResetRelease.TrySetResult();
        Assert.Equal(FiniteContinuousPlaybackPumpResetResult.Reset, await reset);
        Assert.Equal(FiniteContinuousPlaybackPumpState.Idle, policy.Snapshot.State);
        Assert.Equal(FiniteContinuousPlaybackPumpResult.PumpLimitReached,
            await policy.RunAsync(Options(pumps: 1), default));
    }

    [Fact]
    public async Task DisposeCancelsAndWaitsForBlockingResetWithoutStateOverwrite()
    {
        var adapter = new RecordingAdapter(
            PlaybackWindowsOutputSchedulerAdapterResult.VideoProcessed)
        { BlockReset = true };
        var policy = Create(adapter, new StepClock());
        var reset = policy.ResetAsync(8, 12, default).AsTask();
        await adapter.ResetEntered.Task;
        var dispose = Task.Run(policy.Dispose);

        Assert.Equal(FiniteContinuousPlaybackPumpResetResult.Disposed, await reset);
        await dispose;
        Assert.Equal(FiniteContinuousPlaybackPumpState.Disposed, policy.Snapshot.State);
        Assert.False(adapter.Disposed);
    }

    [Fact]
    public async Task ResetCancellationRequiresRecoveryAndResetExceptionFaults()
    {
        var cancellingAdapter = new RecordingAdapter(
            PlaybackWindowsOutputSchedulerAdapterResult.VideoProcessed)
        { BlockReset = true };
        using var cancelling = Create(cancellingAdapter, new StepClock());
        using var cancellation = new CancellationTokenSource();
        var reset = cancelling.ResetAsync(8, 12, cancellation.Token).AsTask();
        await cancellingAdapter.ResetEntered.Task;
        cancellation.Cancel();
        Assert.Equal(FiniteContinuousPlaybackPumpResetResult.Cancelled, await reset);
        Assert.Equal(FiniteContinuousPlaybackPumpState.ResetRequired,
            cancelling.Snapshot.State);

        var throwingAdapter = new RecordingAdapter(
            PlaybackWindowsOutputSchedulerAdapterResult.VideoProcessed)
        { ThrowReset = true };
        using var throwing = Create(throwingAdapter, new StepClock());
        Assert.Equal(FiniteContinuousPlaybackPumpResetResult.Failure,
            await throwing.ResetAsync(8, 12, default));
        Assert.Equal(FiniteContinuousPlaybackPumpState.Faulted,
            throwing.Snapshot.State);
    }

    [Fact]
    public async Task NonWindowsAndInvalidOptionsAvoidLowerPump()
    {
        var adapter = new RecordingAdapter(PlaybackWindowsOutputSchedulerAdapterResult.VideoProcessed);
        using var policy = new FiniteContinuousPlaybackPumpPolicyBoundary(
            adapter, new StepClock(), new RecordingDelay(), static () => false);
        Assert.Equal(FiniteContinuousPlaybackPumpResult.InvalidEnvironment,
            await policy.RunAsync(Options(), default));
        Assert.Equal(FiniteContinuousPlaybackPumpResult.InvalidConfiguration,
            await policy.RunAsync(Options(pumps: 0), default));
        Assert.Equal(0, adapter.PumpCalls);
    }

    private static FiniteContinuousPlaybackPumpPolicyBoundary Create(
        IFiniteContinuousPlaybackPumpAdapter adapter,
        IFiniteContinuousPlaybackPumpClock clock,
        IFiniteContinuousPlaybackPumpDelay? delay = null) =>
        new(adapter, clock, delay ?? new RecordingDelay(), static () => true);

    private static FiniteContinuousPlaybackPumpOptions Options(
        int pumps = 10, int idle = 10, TimeSpan? duration = null) => new()
        {
            MaximumPumpCount = pumps,
            MaximumConsecutiveIdleCount = idle,
            MaximumRunDuration = duration ?? TimeSpan.FromSeconds(1),
            IdleDelay = TimeSpan.FromMilliseconds(5),
        };

    private sealed class RecordingAdapter(
        PlaybackWindowsOutputSchedulerAdapterResult result) :
        IFiniteContinuousPlaybackPumpAdapter
    {
        public int PumpCalls { get; private set; }
        public bool ThrowPump { get; init; }
        public bool ThrowReset { get; init; }
        public bool BlockReset { get; init; }
        public bool IgnoreResetCancellation { get; init; }
        public bool Disposed { get; private set; }
        public TaskCompletionSource ResetEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ResetRelease { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public Queue<PlaybackWindowsOutputSchedulerAdapterResetResult> ResetResults { get; } = [];
        public ValueTask<PlaybackWindowsOutputSchedulerAdapterResult> PumpOneAsync(
            CancellationToken cancellationToken)
        { PumpCalls++; if (ThrowPump) throw new InvalidOperationException(); return ValueTask.FromResult(result); }
        public async ValueTask<PlaybackWindowsOutputSchedulerAdapterResetResult> ResetAsync(
            long videoGeneration, long audioGeneration, CancellationToken cancellationToken)
        {
            if (ThrowReset) throw new InvalidOperationException();
            ResetEntered.TrySetResult();
            if (BlockReset)
            {
                if (IgnoreResetCancellation) await ResetRelease.Task;
                else await ResetRelease.Task.WaitAsync(cancellationToken);
            }
            return ResetResults.TryDequeue(out var reset)
                ? reset : PlaybackWindowsOutputSchedulerAdapterResetResult.Reset;
        }
    }

    private sealed class StepClock : IFiniteContinuousPlaybackPumpClock
    {
        private long _ticks;
        public TimeSpan GetTimestamp() => TimeSpan.FromTicks(_ticks++);
    }

    private sealed class SequenceClock(params TimeSpan[] values) :
        IFiniteContinuousPlaybackPumpClock
    {
        private int _index;
        public TimeSpan GetTimestamp() => values[Math.Min(_index++, values.Length - 1)];
    }

    private sealed class ThrowingClock : IFiniteContinuousPlaybackPumpClock
    {
        public TimeSpan GetTimestamp() => throw new InvalidOperationException();
    }

    private sealed class RecordingDelay : IFiniteContinuousPlaybackPumpDelay
    {
        public List<TimeSpan> Delays { get; } = [];
        public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        { Delays.Add(delay); return ValueTask.CompletedTask; }
    }

    private sealed class BlockingDelay : IFiniteContinuousPlaybackPumpDelay
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        { Entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
    }
}
