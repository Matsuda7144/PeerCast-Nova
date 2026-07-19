namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackRecoveryAwarePumpOrchestratorTests
{
    private static readonly string[] ExpectedResetOrder =
        ["recovery-reset", "pump-reset"];

    [Fact]
    public async Task RunAsync_RecoveryThenPumpCompletionRestartsWithSameGenerations()
    {
        var pump = new RecordingPump(
            PlaybackLateAwareContinuousPumpResult.AudioRecoveryRequired,
            PlaybackLateAwareContinuousPumpResult.Completed);
        var recovery = new RecordingRecovery(PlaybackAudioRecoveryResult.Recovered);
        using var orchestrator = new PlaybackRecoveryAwarePumpOrchestrator(pump, recovery);

        var result = await orchestrator.RunAsync(7, 11, ValidOptions());

        Assert.Equal(PlaybackRecoveryAwarePumpResult.Completed, result);
        Assert.Equal(2, pump.RunCount);
        Assert.Equal((7L, 11L), pump.LastReset);
        Assert.Equal((7L, 11L), recovery.LastRecovery);
        Assert.Equal(1, orchestrator.Snapshot.RecoveryCount);
        Assert.Equal(PlaybackRecoveryAwarePumpState.Idle, orchestrator.Snapshot.State);
    }

    [Fact]
    public async Task RunAsync_RecoveryLimitLatchesResetRequired()
    {
        var pump = new RecordingPump(
            PlaybackLateAwareContinuousPumpResult.AudioRecoveryRequired,
            PlaybackLateAwareContinuousPumpResult.AudioRecoveryRequired);
        var recovery = new RecordingRecovery(PlaybackAudioRecoveryResult.Recovered);
        using var orchestrator = new PlaybackRecoveryAwarePumpOrchestrator(pump, recovery);

        var result = await orchestrator.RunAsync(1, 2, ValidOptions());

        Assert.Equal(PlaybackRecoveryAwarePumpResult.RecoveryLimitReached, result);
        Assert.Equal(1, recovery.RecoveryCount);
        Assert.Equal(PlaybackRecoveryAwarePumpState.ResetRequired,
            orchestrator.Snapshot.State);
    }

    [Fact]
    public async Task RunAsync_RecoveryResetRequiredPropagatesLatch()
    {
        var pump = new RecordingPump(
            PlaybackLateAwareContinuousPumpResult.AudioRecoveryRequired);
        var recovery = new RecordingRecovery(PlaybackAudioRecoveryResult.ResetRequired);
        using var orchestrator = new PlaybackRecoveryAwarePumpOrchestrator(pump, recovery);

        Assert.Equal(PlaybackRecoveryAwarePumpResult.ResetRequired,
            await orchestrator.RunAsync(3, 4, ValidOptions()));
        Assert.Equal(PlaybackRecoveryAwarePumpState.ResetRequired,
            orchestrator.Snapshot.State);
        Assert.Equal(0, pump.ResetCount);
    }

    [Fact]
    public async Task RunAsync_PumpFailureFaultsWithoutRecovery()
    {
        var pump = new RecordingPump(PlaybackLateAwareContinuousPumpResult.Failure);
        var recovery = new RecordingRecovery(PlaybackAudioRecoveryResult.Recovered);
        using var orchestrator = new PlaybackRecoveryAwarePumpOrchestrator(pump, recovery);

        Assert.Equal(PlaybackRecoveryAwarePumpResult.Failure,
            await orchestrator.RunAsync(0, 0, ValidOptions()));
        Assert.Equal(0, recovery.RecoveryCount);
        Assert.Equal(PlaybackRecoveryAwarePumpState.Faulted,
            orchestrator.Snapshot.State);
    }

    [Fact]
    public async Task StopAsync_CancelsActivePumpAndReturnsStopped()
    {
        var pump = new BlockingPump();
        var recovery = new RecordingRecovery(PlaybackAudioRecoveryResult.Recovered);
        using var orchestrator = new PlaybackRecoveryAwarePumpOrchestrator(pump, recovery);
        var run = orchestrator.RunAsync(1, 1, ValidOptions()).AsTask();
        await pump.Started.Task;

        Assert.True(await orchestrator.StopAsync());
        Assert.Equal(PlaybackRecoveryAwarePumpResult.Stopped, await run);
        Assert.Equal(PlaybackRecoveryAwarePumpState.Idle, orchestrator.Snapshot.State);
    }

    [Fact]
    public async Task ResetAsync_ResetsRecoveryBeforePumpAndClearsLatch()
    {
        var order = new List<string>();
        var pump = new RecordingPump(
            PlaybackLateAwareContinuousPumpResult.ResetRequired)
        {
            Order = order,
        };
        var recovery = new RecordingRecovery(PlaybackAudioRecoveryResult.Recovered)
        {
            Order = order,
        };
        using var orchestrator = new PlaybackRecoveryAwarePumpOrchestrator(pump, recovery);
        Assert.Equal(PlaybackRecoveryAwarePumpResult.ResetRequired,
            await orchestrator.RunAsync(1, 1, ValidOptions()));

        Assert.True(await orchestrator.ResetAsync(8, 9));
        Assert.Equal(ExpectedResetOrder, order);
        Assert.Equal(PlaybackRecoveryAwarePumpState.Idle, orchestrator.Snapshot.State);
        Assert.Equal((8L, 9L), pump.LastReset);
    }

    private static PlaybackRecoveryAwarePumpOptions ValidOptions() => new()
    {
        MaximumRecoveries = 1,
        PumpOptions = new()
        {
            MaximumPumpCount = 1,
            MaximumRunDuration = TimeSpan.FromSeconds(1),
            MaximumConsecutiveIdleCount = 1,
            IdleDelay = TimeSpan.FromMilliseconds(1),
        },
        RecoveryOptions = new(),
    };

    private sealed class RecordingPump(params PlaybackLateAwareContinuousPumpResult[] results) :
        IPlaybackRecoveryAwarePump
    {
        private readonly Queue<PlaybackLateAwareContinuousPumpResult> _results = new(results);
        public int RunCount { get; private set; }
        public int ResetCount { get; private set; }
        public (long Video, long Audio) LastReset { get; private set; }
        public List<string>? Order { get; init; }

        public ValueTask<PlaybackLateAwareContinuousPumpResult> RunAsync(
            PlaybackLateAwareContinuousPumpOptions options,
            CancellationToken cancellationToken)
        {
            RunCount++;
            return ValueTask.FromResult(_results.Dequeue());
        }

        public ValueTask<bool> ResetAsync(
            long videoGeneration,
            long audioGeneration,
            CancellationToken cancellationToken)
        {
            ResetCount++;
            LastReset = (videoGeneration, audioGeneration);
            Order?.Add("pump-reset");
            return ValueTask.FromResult(true);
        }
    }

    private sealed class RecordingRecovery(params PlaybackAudioRecoveryResult[] results) :
        IPlaybackRecoveryAwareAudioRecovery
    {
        private readonly Queue<PlaybackAudioRecoveryResult> _results = new(results);
        public int RecoveryCount { get; private set; }
        public (long Video, long Audio) LastRecovery { get; private set; }
        public List<string>? Order { get; init; }

        public ValueTask<PlaybackAudioRecoveryResult> RecoverAsync(
            long videoGeneration,
            long audioGeneration,
            PlaybackAudioRecoveryOptions options,
            CancellationToken cancellationToken)
        {
            RecoveryCount++;
            LastRecovery = (videoGeneration, audioGeneration);
            return ValueTask.FromResult(_results.Dequeue());
        }

        public ValueTask<bool> ResetAsync(CancellationToken cancellationToken)
        {
            Order?.Add("recovery-reset");
            return ValueTask.FromResult(true);
        }
    }

    private sealed class BlockingPump : IPlaybackRecoveryAwarePump
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<PlaybackLateAwareContinuousPumpResult> RunAsync(
            PlaybackLateAwareContinuousPumpOptions options,
            CancellationToken cancellationToken)
        {
            Started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return PlaybackLateAwareContinuousPumpResult.Completed;
        }

        public ValueTask<bool> ResetAsync(
            long videoGeneration,
            long audioGeneration,
            CancellationToken cancellationToken) => ValueTask.FromResult(true);
    }
}
