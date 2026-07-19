namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackLateAwareSchedulerDecisionBoundaryTests
{
    [Fact]
    public async Task DecideOneAsync_VideoDropConsumesSameReservedItemExactlyOnce()
    {
        using var policy = new PlaybackLateItemDispositionPolicyBoundary(
            new PlaybackLateItemDispositionOptions
            {
                VideoLatePolicy = PlaybackVideoLatePolicy.MarkDropCandidate,
            }, 1, 1);
        using var coordination = new PlaybackLateDispatchCoordinationBoundary(policy, 1, 1);
        using var disposition = new PlaybackLateReservationDispositionBoundary(coordination);
        var reservation = new Reservation(PlaybackSchedulerOutputKind.Video);
        using var boundary = Create(new Source(reservation),
            new Timing(PlaybackAvTimingDecisionResult.VideoLate), disposition);

        Assert.Equal(PlaybackLateAwareSchedulerDecisionResult.VideoDropConsumed,
            await boundary.DecideOneAsync());
        Assert.True(reservation.Consumed);
        Assert.Equal(1, reservation.CompleteCalls);
        Assert.True(reservation.Disposed);
        Assert.False(boundary.Snapshot.PreferVideoNext);
    }

    [Fact]
    public async Task DecideOneAsync_ProcessNowRetainsReservationForLaterDispatch()
    {
        using var disposition = Disposition();
        var reservation = new Reservation(PlaybackSchedulerOutputKind.Video);
        using var boundary = Create(new Source(reservation),
            new Timing(PlaybackAvTimingDecisionResult.ProcessNow), disposition);

        Assert.Equal(PlaybackLateAwareSchedulerDecisionResult.ProcessNowRequired,
            await boundary.DecideOneAsync());
        Assert.False(reservation.Consumed);
        Assert.Equal(1, reservation.CompleteCalls);
        Assert.True(reservation.Disposed);
        Assert.True(boundary.Snapshot.PreferVideoNext);
    }

    [Fact]
    public async Task DecideOneAsync_AudioRecoveryRetainsReservation()
    {
        using var policy = new PlaybackLateItemDispositionPolicyBoundary(
            new PlaybackLateItemDispositionOptions
            {
                AudioLatePolicy = PlaybackAudioLatePolicy.RequireRecovery,
            }, 1, 1);
        using var coordination = new PlaybackLateDispatchCoordinationBoundary(policy, 1, 1);
        using var disposition = new PlaybackLateReservationDispositionBoundary(coordination);
        var reservation = new Reservation(PlaybackSchedulerOutputKind.Audio);
        using var boundary = Create(new Source(reservation),
            new Timing(PlaybackAvTimingDecisionResult.AudioLate), disposition);

        Assert.Equal(PlaybackLateAwareSchedulerDecisionResult.AudioRecoveryRequired,
            await boundary.DecideOneAsync());
        Assert.False(reservation.Consumed);
        Assert.Equal(1, reservation.CompleteCalls);
    }

    [Fact]
    public async Task DecideOneAsync_GenerationMismatchRetainsAndLatchesReset()
    {
        using var disposition = Disposition();
        var source = new Source(new Reservation(PlaybackSchedulerOutputKind.Video));
        using var boundary = Create(source,
            new Timing(PlaybackAvTimingDecisionResult.GenerationMismatch), disposition);

        Assert.Equal(PlaybackLateAwareSchedulerDecisionResult.ResetRequired,
            await boundary.DecideOneAsync());
        Assert.Equal(PlaybackLateAwareSchedulerDecisionState.ResetRequired,
            boundary.Snapshot.State);
        Assert.Equal(PlaybackLateAwareSchedulerDecisionResult.ResetRequired,
            await boundary.DecideOneAsync());
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task ResetAsync_ResetsTimingDispositionAndPreference()
    {
        using var policy = new PlaybackLateItemDispositionPolicyBoundary(
            new PlaybackLateItemDispositionOptions
            {
                VideoLatePolicy = PlaybackVideoLatePolicy.MarkDropCandidate,
            }, 1, 1);
        using var coordination = new PlaybackLateDispatchCoordinationBoundary(policy, 1, 1);
        using var disposition = new PlaybackLateReservationDispositionBoundary(coordination);
        var timing = new Timing(PlaybackAvTimingDecisionResult.VideoLate);
        using var boundary = Create(
            new Source(new Reservation(PlaybackSchedulerOutputKind.Video)), timing, disposition);
        await boundary.DecideOneAsync();

        Assert.True(await boundary.ResetAsync(3, 4));
        Assert.Equal(new PlaybackLateAwareSchedulerDecisionSnapshot(
            3, 4, true, PlaybackLateAwareSchedulerDecisionState.Ready), boundary.Snapshot);
        Assert.Equal((3L, 4L), timing.LastReset);
        Assert.Equal(0, disposition.Snapshot.VideoDropCount);
    }

    [Fact]
    public async Task DecideOneAsync_NonWindowsDoesNotReserve()
    {
        using var disposition = Disposition();
        var source = new Source(new Reservation(PlaybackSchedulerOutputKind.Video));
        using var boundary = new PlaybackLateAwareSchedulerDecisionBoundary(
            source, new Timing(PlaybackAvTimingDecisionResult.ProcessNow), disposition,
            1, 1, static () => false);

        Assert.Equal(PlaybackLateAwareSchedulerDecisionResult.InvalidEnvironment,
            await boundary.DecideOneAsync());
        Assert.Equal(0, source.Calls);
    }

    private static PlaybackLateAwareSchedulerDecisionBoundary Create(
        IPlaybackWindowsOutputSchedulerSource source,
        IPlaybackTimedSchedulerTimingDecider timing,
        PlaybackLateReservationDispositionBoundary disposition) =>
        new(source, timing, disposition, 1, 1, static () => true);

    private static PlaybackLateReservationDispositionBoundary Disposition()
    {
        var policy = new PlaybackLateItemDispositionPolicyBoundary(
            new PlaybackLateItemDispositionOptions(), 1, 1);
        var coordination = new PlaybackLateDispatchCoordinationBoundary(policy, 1, 1);
        return new PlaybackLateReservationDispositionBoundary(coordination);
    }

    private sealed class Source(params Reservation[] reservations) :
        IPlaybackWindowsOutputSchedulerSource
    {
        public int Calls { get; private set; }

        public ValueTask<PlaybackWindowsOutputSchedulerTake> ReserveAsync(
            PlaybackSchedulerOutputKind preferredKind,
            CancellationToken cancellationToken)
        {
            Calls++;
            var reservation = reservations.FirstOrDefault(value => !value.Disposed &&
                value.Kind == preferredKind) ??
                reservations.FirstOrDefault(value => !value.Disposed);
            return ValueTask.FromResult(reservation is null
                ? new PlaybackWindowsOutputSchedulerTake(
                    PlaybackWindowsOutputSchedulerTakeResult.Empty, null)
                : new PlaybackWindowsOutputSchedulerTake(
                    PlaybackWindowsOutputSchedulerTakeResult.Reserved, reservation));
        }
    }

    private sealed class Timing(params PlaybackAvTimingDecisionResult[] results) :
        IPlaybackTimedSchedulerTimingDecider
    {
        private readonly Queue<PlaybackAvTimingDecisionResult> _results = new(results);
        public (long Video, long Audio)? LastReset { get; private set; }

        public ValueTask<PlaybackAvTimingDecision> DecideAsync(
            PlaybackAvTimingItem item,
            CancellationToken cancellationToken)
        {
            var result = _results.TryDequeue(out var value)
                ? value
                : PlaybackAvTimingDecisionResult.ProcessNow;
            return ValueTask.FromResult(new PlaybackAvTimingDecision(result, TimeSpan.Zero));
        }

        public ValueTask<PlaybackAvTimingResetResult> ResetAsync(
            long videoGeneration,
            long audioGeneration,
            CancellationToken cancellationToken)
        {
            LastReset = (videoGeneration, audioGeneration);
            return ValueTask.FromResult(PlaybackAvTimingResetResult.Reset);
        }
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
