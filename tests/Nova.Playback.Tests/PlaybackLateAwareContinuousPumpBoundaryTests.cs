namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackLateAwareContinuousPumpBoundaryTests
{
    private static PlaybackLateAwareContinuousPumpOptions Options => new()
    {
        MaximumPumpCount = 4,
        MaximumRunDuration = TimeSpan.FromSeconds(1),
        MaximumConsecutiveIdleCount = 2,
        IdleDelay = TimeSpan.FromMilliseconds(1),
        DispatchOptions = new PlaybackTimedSchedulerDispatchOptions(),
    };

    [Fact]
    public async Task RunAsync_ProcessedItemsReachPumpLimit()
    {
        var dispatch = new RecordingDispatch(
            PlaybackLateAwareTimedDispatchResult.VideoProcessed,
            PlaybackLateAwareTimedDispatchResult.AudioProcessed,
            PlaybackLateAwareTimedDispatchResult.VideoProcessed,
            PlaybackLateAwareTimedDispatchResult.AudioProcessed);
        using var boundary = new PlaybackLateAwareContinuousPumpBoundary(
            dispatch, new IncrementingClock(), new RecordingDelay());

        Assert.Equal(PlaybackLateAwareContinuousPumpResult.PumpLimitReached,
            await boundary.RunAsync(Options));
        Assert.Equal(4, boundary.Snapshot.PumpCount);
        Assert.Equal(0, boundary.Snapshot.ConsecutiveIdleCount);
    }

    [Fact]
    public async Task RunAsync_VideoDropCountsAsProgress()
    {
        var dispatch = new RecordingDispatch(
            PlaybackLateAwareTimedDispatchResult.VideoDropConsumed,
            PlaybackLateAwareTimedDispatchResult.VideoProcessed,
            PlaybackLateAwareTimedDispatchResult.VideoProcessed,
            PlaybackLateAwareTimedDispatchResult.VideoProcessed);
        using var boundary = new PlaybackLateAwareContinuousPumpBoundary(
            dispatch, new IncrementingClock(), new RecordingDelay());

        Assert.Equal(PlaybackLateAwareContinuousPumpResult.PumpLimitReached,
            await boundary.RunAsync(Options));
        Assert.Equal(1, boundary.Snapshot.VideoDropCount);
    }

    [Fact]
    public async Task RunAsync_RetainedLateUsesIdleLimit()
    {
        var delay = new RecordingDelay();
        var dispatch = new RecordingDispatch(
            PlaybackLateAwareTimedDispatchResult.RetainedVideoLate,
            PlaybackLateAwareTimedDispatchResult.RetainedVideoLate);
        using var boundary = new PlaybackLateAwareContinuousPumpBoundary(
            dispatch, new IncrementingClock(), delay);

        Assert.Equal(PlaybackLateAwareContinuousPumpResult.IdleLimitReached,
            await boundary.RunAsync(Options));
        Assert.Single(delay.Durations);
        Assert.Equal(2, boundary.Snapshot.ConsecutiveIdleCount);
    }

    [Fact]
    public async Task RunAsync_AudioRecoveryStopsAndLatchesReset()
    {
        var dispatch = new RecordingDispatch(
            PlaybackLateAwareTimedDispatchResult.AudioRecoveryRequired);
        using var boundary = new PlaybackLateAwareContinuousPumpBoundary(
            dispatch, new IncrementingClock(), new RecordingDelay());

        Assert.Equal(PlaybackLateAwareContinuousPumpResult.AudioRecoveryRequired,
            await boundary.RunAsync(Options));
        Assert.Equal(PlaybackLateAwareContinuousPumpState.ResetRequired,
            boundary.Snapshot.State);
    }

    [Fact]
    public async Task ResetAsync_DelegatesAndClearsCounters()
    {
        var dispatch = new RecordingDispatch(
            PlaybackLateAwareTimedDispatchResult.VideoDropConsumed,
            PlaybackLateAwareTimedDispatchResult.VideoProcessed,
            PlaybackLateAwareTimedDispatchResult.VideoProcessed,
            PlaybackLateAwareTimedDispatchResult.VideoProcessed);
        using var boundary = new PlaybackLateAwareContinuousPumpBoundary(
            dispatch, new IncrementingClock(), new RecordingDelay());
        await boundary.RunAsync(Options);

        Assert.True(await boundary.ResetAsync(7, 11));
        Assert.Equal((7L, 11L), dispatch.LastReset);
        Assert.Equal(new PlaybackLateAwareContinuousPumpSnapshot(
            PlaybackLateAwareContinuousPumpState.Idle, 0, 0, 0, 1),
            boundary.Snapshot);
    }

    private sealed class RecordingDispatch(params PlaybackLateAwareTimedDispatchResult[] results) :
        IPlaybackLateAwareContinuousPumpDispatch
    {
        private readonly Queue<PlaybackLateAwareTimedDispatchResult> _results = new(results);
        public (long Video, long Audio) LastReset { get; private set; }

        public ValueTask<PlaybackLateAwareTimedDispatchResult> DispatchOneAsync(
            PlaybackTimedSchedulerDispatchOptions options,
            CancellationToken cancellationToken) => ValueTask.FromResult(
                _results.Count == 0
                    ? PlaybackLateAwareTimedDispatchResult.Empty
                    : _results.Dequeue());

        public ValueTask<bool> ResetAsync(
            long videoGeneration,
            long audioGeneration,
            CancellationToken cancellationToken)
        {
            LastReset = (videoGeneration, audioGeneration);
            return ValueTask.FromResult(true);
        }
    }

    private sealed class IncrementingClock : IPlaybackLateAwareContinuousPumpClock
    {
        private long _ticks;
        public TimeSpan GetTimestamp() => TimeSpan.FromTicks(_ticks++);
    }

    private sealed class RecordingDelay : IPlaybackLateAwareContinuousPumpDelay
    {
        public List<TimeSpan> Durations { get; } = [];
        public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Durations.Add(delay);
            return ValueTask.CompletedTask;
        }
    }
}
