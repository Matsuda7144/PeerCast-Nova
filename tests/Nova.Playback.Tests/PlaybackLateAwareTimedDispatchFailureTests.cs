namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackLateAwareTimedDispatchFailureTests
{
    [Fact]
    public async Task DispatchOneAsync_OutputFailureRetainsAndLatchesFault()
    {
        using var policy = new PlaybackLateItemDispositionPolicyBoundary(
            new PlaybackLateItemDispositionOptions(), 1, 1);
        using var coordination = new PlaybackLateDispatchCoordinationBoundary(policy, 1, 1);
        using var disposition = new PlaybackLateReservationDispositionBoundary(coordination);
        var reservation = new Reservation();
        var source = new Source(reservation);
        using var boundary = new PlaybackLateAwareTimedDispatchBoundary(
            source,
            new Dispatcher(),
            new Timing(),
            new Delay(),
            disposition,
            1,
            1,
            static () => true);

        Assert.Equal(PlaybackLateAwareTimedDispatchResult.Failure,
            await boundary.DispatchOneAsync(Options()));
        Assert.False(reservation.Consumed);
        Assert.Equal(1, reservation.CompleteCalls);
        Assert.Equal(PlaybackLateAwareTimedDispatchState.Faulted, boundary.Snapshot.State);
        Assert.Equal(PlaybackLateAwareTimedDispatchResult.Failure,
            await boundary.DispatchOneAsync(Options()));
        Assert.Equal(1, source.Calls);
    }

    private static PlaybackTimedSchedulerDispatchOptions Options() => new()
    {
        MaximumDecisionCount = 1,
        MaximumTotalWait = TimeSpan.FromMilliseconds(20),
        MinimumDelay = TimeSpan.FromMilliseconds(1),
        MaximumSingleDelay = TimeSpan.FromMilliseconds(10),
    };

    private sealed class Source(Reservation reservation) : IPlaybackWindowsOutputSchedulerSource
    {
        public int Calls { get; private set; }

        public ValueTask<PlaybackWindowsOutputSchedulerTake> ReserveAsync(
            PlaybackSchedulerOutputKind preferredKind,
            CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(new PlaybackWindowsOutputSchedulerTake(
                PlaybackWindowsOutputSchedulerTakeResult.Reserved, reservation));
        }
    }

    private sealed class Timing : IPlaybackTimedSchedulerTimingDecider
    {
        public ValueTask<PlaybackAvTimingDecision> DecideAsync(
            PlaybackAvTimingItem item,
            CancellationToken cancellationToken) => ValueTask.FromResult(
                new PlaybackAvTimingDecision(
                    PlaybackAvTimingDecisionResult.ProcessNow, TimeSpan.Zero));

        public ValueTask<PlaybackAvTimingResetResult> ResetAsync(
            long videoGeneration,
            long audioGeneration,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackAvTimingResetResult.Reset);
    }

    private sealed class Delay : IPlaybackTimedSchedulerDelay
    {
        public ValueTask DelayAsync(TimeSpan duration, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class Dispatcher : IPlaybackWindowsOutputBufferDispatcher
    {
        public ValueTask<PlaybackWindowsOutputBufferProcessingResult> ProcessVideoAsync(
            PlaybackWindowsVideoFrameBuffer frame,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackWindowsOutputBufferProcessingResult.Failure);

        public ValueTask<PlaybackWindowsOutputBufferProcessingResult> ProcessAudioAsync(
            PlaybackWindowsAudioSampleBuffer samples,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackWindowsOutputBufferProcessingResult.Failure);

        public ValueTask<PlaybackWindowsOutputBufferResetResult> ResetVideoAsync(
            long generation,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackWindowsOutputBufferResetResult.Reset);

        public ValueTask<PlaybackWindowsOutputBufferResetResult> ResetAudioAsync(
            long generation,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackWindowsOutputBufferResetResult.Reset);
    }

    private sealed class Reservation : IPlaybackWindowsOutputSchedulerReservation
    {
        public PlaybackSchedulerOutputKind Kind => PlaybackSchedulerOutputKind.Video;
        public PlaybackDecoderFfmpegManagedFrame Frame { get; } = new(
            new PlaybackDecoderFfmpegFrameMetadata(
                PlaybackDecoderFfmpegFrameMediaKind.Video,
                PlaybackDecoderFfmpegFrameFormat.VideoBgra32,
                1,
                1,
                0,
                0,
                0,
                PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.Zero),
                PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.Zero),
                PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.FromMilliseconds(20))),
            [new(0, 4, 1, 4, new byte[4])]);
        public long Sequence => 1;
        public PlaybackDecodedFrameDiscontinuityKind Discontinuity =>
            PlaybackDecodedFrameDiscontinuityKind.None;
        public bool Consumed { get; private set; }
        public int CompleteCalls { get; private set; }

        public void Complete(bool consume)
        {
            CompleteCalls++;
            Consumed = consume;
        }

        public void Dispose() => Frame.Dispose();
    }
}
