namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackLateAwareTimedDispatchBoundaryTests
{
    [Fact]
    public async Task DispatchOneAsync_WaitsThenProcessesSameReservation()
    {
        using var disposition = Disposition();
        var reservation = new Reservation(PlaybackSchedulerOutputKind.Video);
        var timing = new Timing(
            new PlaybackAvTimingDecision(
                PlaybackAvTimingDecisionResult.Wait, TimeSpan.FromMilliseconds(5)),
            new PlaybackAvTimingDecision(
                PlaybackAvTimingDecisionResult.ProcessNow, TimeSpan.Zero));
        var dispatcher = new Dispatcher();
        var delay = new Delay();
        using var boundary = Create(new Source(reservation), dispatcher, timing, delay, disposition);

        Assert.Equal(PlaybackLateAwareTimedDispatchResult.VideoProcessed,
            await boundary.DispatchOneAsync(Options()));
        Assert.True(reservation.Consumed);
        Assert.Equal(1, reservation.CompleteCalls);
        Assert.Equal(1, dispatcher.VideoCalls);
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(5) }, delay.Durations);
    }

    [Fact]
    public async Task DispatchOneAsync_VideoLateDropConsumesWithoutOutputDispatch()
    {
        using var policy = new PlaybackLateItemDispositionPolicyBoundary(
            new PlaybackLateItemDispositionOptions
            {
                VideoLatePolicy = PlaybackVideoLatePolicy.MarkDropCandidate,
            }, 1, 1);
        using var coordination = new PlaybackLateDispatchCoordinationBoundary(policy, 1, 1);
        using var disposition = new PlaybackLateReservationDispositionBoundary(coordination);
        var reservation = new Reservation(PlaybackSchedulerOutputKind.Video);
        var dispatcher = new Dispatcher();
        using var boundary = Create(new Source(reservation), dispatcher,
            new Timing(new PlaybackAvTimingDecision(
                PlaybackAvTimingDecisionResult.VideoLate, TimeSpan.Zero)),
            new Delay(), disposition);

        Assert.Equal(PlaybackLateAwareTimedDispatchResult.VideoDropConsumed,
            await boundary.DispatchOneAsync(Options()));
        Assert.True(reservation.Consumed);
        Assert.Equal(1, reservation.CompleteCalls);
        Assert.Equal(0, dispatcher.VideoCalls);
        Assert.False(boundary.Snapshot.PreferVideoNext);
    }

    [Fact]
    public async Task DispatchOneAsync_WaitLimitRetainsReservation()
    {
        using var disposition = Disposition();
        var reservation = new Reservation(PlaybackSchedulerOutputKind.Audio);
        var options = Options() with { MaximumDecisionCount = 1 };
        using var boundary = Create(new Source(reservation), new Dispatcher(),
            new Timing(new PlaybackAvTimingDecision(
                PlaybackAvTimingDecisionResult.Wait, TimeSpan.FromMilliseconds(5))),
            new Delay(), disposition);

        Assert.Equal(PlaybackLateAwareTimedDispatchResult.WaitLimitReached,
            await boundary.DispatchOneAsync(options));
        Assert.False(reservation.Consumed);
        Assert.Equal(1, reservation.CompleteCalls);
    }

    [Fact]
    public async Task DispatchOneAsync_GenerationMismatchRetainsAndLatchesReset()
    {
        using var disposition = Disposition();
        var reservation = new Reservation(PlaybackSchedulerOutputKind.Video);
        var source = new Source(reservation);
        using var boundary = Create(source, new Dispatcher(),
            new Timing(new PlaybackAvTimingDecision(
                PlaybackAvTimingDecisionResult.GenerationMismatch, TimeSpan.Zero)),
            new Delay(), disposition);

        Assert.Equal(PlaybackLateAwareTimedDispatchResult.ResetRequired,
            await boundary.DispatchOneAsync(Options()));
        Assert.False(reservation.Consumed);
        Assert.Equal(PlaybackLateAwareTimedDispatchState.ResetRequired, boundary.Snapshot.State);
        Assert.Equal(PlaybackLateAwareTimedDispatchResult.ResetRequired,
            await boundary.DispatchOneAsync(Options()));
        Assert.Equal(1, source.Calls);
    }

    private static PlaybackLateAwareTimedDispatchBoundary Create(
        IPlaybackWindowsOutputSchedulerSource source,
        IPlaybackWindowsOutputBufferDispatcher dispatcher,
        IPlaybackTimedSchedulerTimingDecider timing,
        IPlaybackTimedSchedulerDelay delay,
        PlaybackLateReservationDispositionBoundary disposition) =>
        new(source, dispatcher, timing, delay, disposition, 1, 1, static () => true);

    private static PlaybackTimedSchedulerDispatchOptions Options() => new()
    {
        MaximumDecisionCount = 2,
        MaximumTotalWait = TimeSpan.FromMilliseconds(20),
        MinimumDelay = TimeSpan.FromMilliseconds(1),
        MaximumSingleDelay = TimeSpan.FromMilliseconds(10),
    };

    private static PlaybackLateReservationDispositionBoundary Disposition()
    {
        var policy = new PlaybackLateItemDispositionPolicyBoundary(
            new PlaybackLateItemDispositionOptions(), 1, 1);
        var coordination = new PlaybackLateDispatchCoordinationBoundary(policy, 1, 1);
        return new PlaybackLateReservationDispositionBoundary(coordination);
    }

    private sealed class Source(Reservation reservation) : IPlaybackWindowsOutputSchedulerSource
    {
        public int Calls { get; private set; }

        public ValueTask<PlaybackWindowsOutputSchedulerTake> ReserveAsync(
            PlaybackSchedulerOutputKind preferredKind,
            CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(reservation.Disposed
                ? new PlaybackWindowsOutputSchedulerTake(
                    PlaybackWindowsOutputSchedulerTakeResult.Empty, null)
                : new PlaybackWindowsOutputSchedulerTake(
                    PlaybackWindowsOutputSchedulerTakeResult.Reserved, reservation));
        }
    }

    private sealed class Timing(params PlaybackAvTimingDecision[] decisions) :
        IPlaybackTimedSchedulerTimingDecider
    {
        private readonly Queue<PlaybackAvTimingDecision> _decisions = new(decisions);

        public ValueTask<PlaybackAvTimingDecision> DecideAsync(
            PlaybackAvTimingItem item,
            CancellationToken cancellationToken) => ValueTask.FromResult(
                _decisions.TryDequeue(out var decision)
                    ? decision
                    : new PlaybackAvTimingDecision(
                        PlaybackAvTimingDecisionResult.ProcessNow, TimeSpan.Zero));

        public ValueTask<PlaybackAvTimingResetResult> ResetAsync(
            long videoGeneration,
            long audioGeneration,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackAvTimingResetResult.Reset);
    }

    private sealed class Delay : IPlaybackTimedSchedulerDelay
    {
        public List<TimeSpan> Durations { get; } = [];

        public ValueTask DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            Durations.Add(duration);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Dispatcher : IPlaybackWindowsOutputBufferDispatcher
    {
        public int VideoCalls { get; private set; }

        public ValueTask<PlaybackWindowsOutputBufferProcessingResult> ProcessVideoAsync(
            PlaybackWindowsVideoFrameBuffer frame,
            CancellationToken cancellationToken)
        {
            VideoCalls++;
            return ValueTask.FromResult(PlaybackWindowsOutputBufferProcessingResult.Processed);
        }

        public ValueTask<PlaybackWindowsOutputBufferProcessingResult> ProcessAudioAsync(
            PlaybackWindowsAudioSampleBuffer samples,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackWindowsOutputBufferProcessingResult.Processed);

        public ValueTask<PlaybackWindowsOutputBufferResetResult> ResetVideoAsync(
            long generation,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackWindowsOutputBufferResetResult.Reset);

        public ValueTask<PlaybackWindowsOutputBufferResetResult> ResetAudioAsync(
            long generation,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackWindowsOutputBufferResetResult.Reset);
    }

    private sealed class Reservation(PlaybackSchedulerOutputKind kind) :
        IPlaybackWindowsOutputSchedulerReservation
    {
        public PlaybackSchedulerOutputKind Kind { get; } = kind;
        public PlaybackDecoderFfmpegManagedFrame Frame { get; } = kind ==
            PlaybackSchedulerOutputKind.Video ? VideoFrame() : AudioFrame();
        public long Sequence => 1;
        public PlaybackDecodedFrameDiscontinuityKind Discontinuity =>
            PlaybackDecodedFrameDiscontinuityKind.None;
        public bool Consumed { get; private set; }
        public bool Disposed { get; private set; }
        public int CompleteCalls { get; private set; }

        public void Complete(bool consume)
        {
            CompleteCalls++;
            Consumed = consume;
        }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            Frame.Dispose();
        }
    }

    private static PlaybackDecoderFfmpegManagedFrame VideoFrame() => new(
        new PlaybackDecoderFfmpegFrameMetadata(
            PlaybackDecoderFfmpegFrameMediaKind.Video,
            PlaybackDecoderFfmpegFrameFormat.VideoBgra32,
            1, 1, 0, 0, 0,
            PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.Zero),
            PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.Zero),
            PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.FromMilliseconds(20))),
        [new(0, 4, 1, 4, new byte[4])]);

    private static PlaybackDecoderFfmpegManagedFrame AudioFrame() => new(
        new PlaybackDecoderFfmpegFrameMetadata(
            PlaybackDecoderFfmpegFrameMediaKind.Audio,
            PlaybackDecoderFfmpegFrameFormat.AudioSigned16,
            0, 0, 48_000, 2, 2,
            PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.Zero),
            PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.Zero),
            PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.FromMilliseconds(20))),
        [new(0, 8, 1, 8, new byte[8])]);
}
