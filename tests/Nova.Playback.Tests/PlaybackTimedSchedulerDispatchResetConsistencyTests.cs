namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackTimedSchedulerDispatchResetConsistencyTests
{
    [Fact]
    public async Task ResetAsync_ResetsOutputBeforeTimingAndCommitsGenerations()
    {
        var calls = new List<string>();
        var dispatcher = new RecordingDispatcher(calls);
        var timing = new RecordingTiming(calls);
        using var boundary = Create(dispatcher, timing);

        Assert.Equal(PlaybackTimedSchedulerResetResult.Reset,
            await boundary.ResetAsync(2, 3));
        Assert.Equal(["output-audio:3", "output-video:2", "timing:2:3"], calls);
        Assert.Equal(new PlaybackTimedSchedulerDispatchSnapshot(2, 3, true,
            PlaybackTimedSchedulerDispatchState.Ready), boundary.Snapshot);
    }

    [Fact]
    public async Task ResetAsync_TimingFailureRollsBackTimingAndOutputInReverseOrder()
    {
        var calls = new List<string>();
        var dispatcher = new RecordingDispatcher(calls);
        var timing = new RecordingTiming(calls);
        timing.Results.Enqueue(PlaybackAvTimingResetResult.Failure);
        timing.Results.Enqueue(PlaybackAvTimingResetResult.Reset);
        using var boundary = Create(dispatcher, timing);

        Assert.Equal(PlaybackTimedSchedulerResetResult.Failure,
            await boundary.ResetAsync(2, 3));
        Assert.Equal([
            "output-audio:3",
            "output-video:2",
            "timing:2:3",
            "timing:1:1",
            "output-video:1",
            "output-audio:1",
        ], calls);
        Assert.Equal(new PlaybackTimedSchedulerDispatchSnapshot(1, 1, true,
            PlaybackTimedSchedulerDispatchState.Faulted), boundary.Snapshot);
    }

    [Fact]
    public async Task ResetAsync_OutputRollbackFailureExposesPartialResetFault()
    {
        var calls = new List<string>();
        var dispatcher = new RecordingDispatcher(calls);
        dispatcher.VideoResults.Enqueue(PlaybackWindowsOutputBufferResetResult.Failure);
        dispatcher.AudioResults.Enqueue(PlaybackWindowsOutputBufferResetResult.Reset);
        dispatcher.AudioResults.Enqueue(PlaybackWindowsOutputBufferResetResult.Failure);
        using var boundary = Create(dispatcher, new RecordingTiming(calls));

        Assert.Equal(PlaybackTimedSchedulerResetResult.PartialResetFault,
            await boundary.ResetAsync(2, 3));
        Assert.Equal(new PlaybackTimedSchedulerDispatchSnapshot(1, 3, true,
            PlaybackTimedSchedulerDispatchState.Faulted), boundary.Snapshot);
    }

    private static PlaybackTimedSchedulerDispatchBoundary Create(
        IPlaybackWindowsOutputBufferDispatcher dispatcher,
        IPlaybackTimedSchedulerTimingDecider timing) =>
        new(new EmptySource(), dispatcher, timing, new NoDelay(), 1, 1, static () => true);

    private sealed class EmptySource : IPlaybackWindowsOutputSchedulerSource
    {
        public ValueTask<PlaybackWindowsOutputSchedulerTake> ReserveAsync(
            PlaybackSchedulerOutputKind preferredKind, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PlaybackWindowsOutputSchedulerTake(
                PlaybackWindowsOutputSchedulerTakeResult.Empty, null));
    }

    private sealed class RecordingDispatcher(List<string> calls) :
        IPlaybackWindowsOutputBufferDispatcher
    {
        public Queue<PlaybackWindowsOutputBufferResetResult> VideoResults { get; } = [];
        public Queue<PlaybackWindowsOutputBufferResetResult> AudioResults { get; } = [];

        public ValueTask<PlaybackWindowsOutputBufferProcessingResult> ProcessVideoAsync(
            PlaybackWindowsVideoFrameBuffer frame, CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackWindowsOutputBufferProcessingResult.Processed);

        public ValueTask<PlaybackWindowsOutputBufferProcessingResult> ProcessAudioAsync(
            PlaybackWindowsAudioSampleBuffer samples, CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackWindowsOutputBufferProcessingResult.Processed);

        public ValueTask<PlaybackWindowsOutputBufferResetResult> ResetVideoAsync(
            long generation, CancellationToken cancellationToken)
        {
            calls.Add($"output-video:{generation}");
            return ValueTask.FromResult(VideoResults.TryDequeue(out var result)
                ? result : PlaybackWindowsOutputBufferResetResult.Reset);
        }

        public ValueTask<PlaybackWindowsOutputBufferResetResult> ResetAudioAsync(
            long generation, CancellationToken cancellationToken)
        {
            calls.Add($"output-audio:{generation}");
            return ValueTask.FromResult(AudioResults.TryDequeue(out var result)
                ? result : PlaybackWindowsOutputBufferResetResult.Reset);
        }
    }

    private sealed class RecordingTiming(List<string> calls) : IPlaybackTimedSchedulerTimingDecider
    {
        public Queue<PlaybackAvTimingResetResult> Results { get; } = [];

        public ValueTask<PlaybackAvTimingDecision> DecideAsync(
            PlaybackAvTimingItem item, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PlaybackAvTimingDecision(
                PlaybackAvTimingDecisionResult.ProcessNow, TimeSpan.Zero));

        public ValueTask<PlaybackAvTimingResetResult> ResetAsync(
            long videoGeneration, long audioGeneration, CancellationToken cancellationToken)
        {
            calls.Add($"timing:{videoGeneration}:{audioGeneration}");
            return ValueTask.FromResult(Results.TryDequeue(out var result)
                ? result : PlaybackAvTimingResetResult.Reset);
        }
    }

    private sealed class NoDelay : IPlaybackTimedSchedulerDelay
    {
        public ValueTask DelayAsync(TimeSpan duration, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
