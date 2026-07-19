namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackAudioRecoveryBoundaryTests
{
    [Fact]
    public async Task RecoverAsync_RecoveredEffectResetsGenerations()
    {
        var effect = new RecordingEffect(PlaybackAudioRecoveryEffectResult.Recovered);
        var reset = new RecordingReset(true);
        using var boundary = new PlaybackAudioRecoveryBoundary(
            effect, reset, new RecordingDelay());

        var result = await boundary.RecoverAsync(7, 11, new());

        Assert.Equal(PlaybackAudioRecoveryResult.Recovered, result);
        Assert.Equal((7L, 11L), reset.LastReset);
        Assert.Equal(PlaybackAudioRecoveryState.Ready, boundary.Snapshot.State);
        Assert.Equal(1, boundary.Snapshot.AttemptCount);
    }

    [Fact]
    public async Task RecoverAsync_RetryThenRecoveredUsesSameGeneration()
    {
        var effect = new RecordingEffect(
            PlaybackAudioRecoveryEffectResult.RetryRequired,
            PlaybackAudioRecoveryEffectResult.Recovered);
        var delay = new RecordingDelay();
        using var boundary = new PlaybackAudioRecoveryBoundary(
            effect, new RecordingReset(true), delay);

        var result = await boundary.RecoverAsync(2, 3, new()
        {
            MaximumAttempts = 2,
            RetryDelay = TimeSpan.FromMilliseconds(1),
        });

        Assert.Equal(PlaybackAudioRecoveryResult.Recovered, result);
        Assert.Equal(new long[] { 3, 3 }, effect.Generations);
        Assert.Single(delay.Durations);
        Assert.Equal(2, boundary.Snapshot.AttemptCount);
    }

    [Fact]
    public async Task RecoverAsync_AttemptLimitLatchesResetRequired()
    {
        var effect = new RecordingEffect(
            PlaybackAudioRecoveryEffectResult.RetryRequired,
            PlaybackAudioRecoveryEffectResult.RetryRequired);
        using var boundary = new PlaybackAudioRecoveryBoundary(
            effect, new RecordingReset(true), new RecordingDelay());

        var result = await boundary.RecoverAsync(0, 0, new()
        {
            MaximumAttempts = 2,
        });

        Assert.Equal(PlaybackAudioRecoveryResult.AttemptLimitReached, result);
        Assert.Equal(PlaybackAudioRecoveryState.ResetRequired, boundary.Snapshot.State);
    }

    [Fact]
    public async Task RecoverAsync_ResetFailureFaults()
    {
        using var boundary = new PlaybackAudioRecoveryBoundary(
            new RecordingEffect(PlaybackAudioRecoveryEffectResult.Recovered),
            new RecordingReset(false), new RecordingDelay());

        Assert.Equal(PlaybackAudioRecoveryResult.Failure,
            await boundary.RecoverAsync(1, 1, new()));
        Assert.Equal(PlaybackAudioRecoveryState.Faulted, boundary.Snapshot.State);
    }

    [Fact]
    public async Task ResetAsync_CancelsActiveRecoveryAndClearsLatch()
    {
        var effect = new BlockingEffect();
        using var boundary = new PlaybackAudioRecoveryBoundary(
            effect, new RecordingReset(true), new RecordingDelay());
        var recovery = boundary.RecoverAsync(1, 1, new()).AsTask();
        await effect.Started.Task;

        Assert.True(await boundary.ResetAsync());
        Assert.Equal(PlaybackAudioRecoveryResult.Cancelled, await recovery);
        Assert.Equal(PlaybackAudioRecoveryState.Ready, boundary.Snapshot.State);
        Assert.Equal(0, boundary.Snapshot.AttemptCount);
    }

    private sealed class RecordingEffect(params PlaybackAudioRecoveryEffectResult[] results) :
        IPlaybackAudioRecoveryEffect
    {
        private readonly Queue<PlaybackAudioRecoveryEffectResult> _results = new(results);
        public List<long> Generations { get; } = [];

        public ValueTask<PlaybackAudioRecoveryEffectResult> RecoverAsync(
            long audioGeneration,
            CancellationToken cancellationToken)
        {
            Generations.Add(audioGeneration);
            return ValueTask.FromResult(_results.Dequeue());
        }
    }

    private sealed class RecordingReset(bool result) : IPlaybackAudioRecoveryReset
    {
        public (long Video, long Audio) LastReset { get; private set; }

        public ValueTask<bool> ResetAsync(
            long videoGeneration,
            long audioGeneration,
            CancellationToken cancellationToken)
        {
            LastReset = (videoGeneration, audioGeneration);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RecordingDelay : IPlaybackAudioRecoveryDelay
    {
        public List<TimeSpan> Durations { get; } = [];

        public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Durations.Add(delay);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingEffect : IPlaybackAudioRecoveryEffect
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<PlaybackAudioRecoveryEffectResult> RecoverAsync(
            long audioGeneration,
            CancellationToken cancellationToken)
        {
            Started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return PlaybackAudioRecoveryEffectResult.Recovered;
        }
    }
}
