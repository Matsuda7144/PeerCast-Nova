namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackLateReservationDispositionBoundaryTests
{
    [Fact]
    public void Apply_DefaultVideoPolicy_RetainsReservation()
    {
        using var policy = new PlaybackLateItemDispositionPolicyBoundary(
            new PlaybackLateItemDispositionOptions(), 1, 1);
        using var coordination = new PlaybackLateDispatchCoordinationBoundary(policy, 1, 1);
        using var boundary = new PlaybackLateReservationDispositionBoundary(coordination);
        using var reservation = new Reservation(PlaybackSchedulerOutputKind.Video);

        Assert.Equal(PlaybackLateReservationDispositionResult.RetainedVideoLate,
            boundary.Apply(reservation, PlaybackTimedSchedulerDispatchResult.VideoLate, 1));
        Assert.False(reservation.Consumed);
        Assert.Equal(1, reservation.CompleteCalls);
    }

    [Fact]
    public void Apply_ConfiguredVideoDrop_ConsumesExactlyOnce()
    {
        using var policy = new PlaybackLateItemDispositionPolicyBoundary(
            new PlaybackLateItemDispositionOptions
            {
                VideoLatePolicy = PlaybackVideoLatePolicy.MarkDropCandidate,
            }, 1, 1);
        using var coordination = new PlaybackLateDispatchCoordinationBoundary(policy, 1, 1);
        using var boundary = new PlaybackLateReservationDispositionBoundary(coordination);
        using var reservation = new Reservation(PlaybackSchedulerOutputKind.Video);

        Assert.Equal(PlaybackLateReservationDispositionResult.VideoDropConsumed,
            boundary.Apply(reservation, PlaybackTimedSchedulerDispatchResult.VideoLate, 1));
        Assert.True(reservation.Consumed);
        Assert.Equal(1, reservation.CompleteCalls);
        Assert.Equal(1, boundary.Snapshot.VideoDropCount);
    }

    [Fact]
    public void Apply_AudioRecovery_RetainsReservation()
    {
        using var policy = new PlaybackLateItemDispositionPolicyBoundary(
            new PlaybackLateItemDispositionOptions
            {
                AudioLatePolicy = PlaybackAudioLatePolicy.RequireRecovery,
            }, 1, 1);
        using var coordination = new PlaybackLateDispatchCoordinationBoundary(policy, 1, 1);
        using var boundary = new PlaybackLateReservationDispositionBoundary(coordination);
        using var reservation = new Reservation(PlaybackSchedulerOutputKind.Audio);

        Assert.Equal(PlaybackLateReservationDispositionResult.AudioRecoveryRequired,
            boundary.Apply(reservation, PlaybackTimedSchedulerDispatchResult.AudioLate, 1));
        Assert.False(reservation.Consumed);
        Assert.Equal(1, reservation.CompleteCalls);
    }

    [Fact]
    public void Apply_GenerationMismatch_RetainsAndLatchesReset()
    {
        using var policy = new PlaybackLateItemDispositionPolicyBoundary(
            new PlaybackLateItemDispositionOptions(), 1, 1);
        using var coordination = new PlaybackLateDispatchCoordinationBoundary(policy, 1, 1);
        using var boundary = new PlaybackLateReservationDispositionBoundary(coordination);
        using var reservation = new Reservation(PlaybackSchedulerOutputKind.Video);

        Assert.Equal(PlaybackLateReservationDispositionResult.ResetRequired,
            boundary.Apply(reservation, PlaybackTimedSchedulerDispatchResult.VideoLate, 2));
        Assert.False(reservation.Consumed);
        Assert.Equal(1, reservation.CompleteCalls);
        Assert.Equal(PlaybackLateReservationDispositionState.ResetRequired, boundary.Snapshot.State);
    }

    [Fact]
    public void Apply_LatchedReset_RetainsNextReservationExactlyOnce()
    {
        using var policy = new PlaybackLateItemDispositionPolicyBoundary(
            new PlaybackLateItemDispositionOptions(), 1, 1);
        using var coordination = new PlaybackLateDispatchCoordinationBoundary(policy, 1, 1);
        using var boundary = new PlaybackLateReservationDispositionBoundary(coordination);
        using var first = new Reservation(PlaybackSchedulerOutputKind.Video);
        using var second = new Reservation(PlaybackSchedulerOutputKind.Audio);

        boundary.Apply(first, PlaybackTimedSchedulerDispatchResult.VideoLate, 2);

        Assert.Equal(PlaybackLateReservationDispositionResult.ResetRequired,
            boundary.Apply(second, PlaybackTimedSchedulerDispatchResult.AudioLate, 1));
        Assert.False(second.Consumed);
        Assert.Equal(1, second.CompleteCalls);
    }

    [Fact]
    public void Apply_InvalidPair_RetainsReservationExactlyOnce()
    {
        using var policy = new PlaybackLateItemDispositionPolicyBoundary(
            new PlaybackLateItemDispositionOptions(), 1, 1);
        using var coordination = new PlaybackLateDispatchCoordinationBoundary(policy, 1, 1);
        using var boundary = new PlaybackLateReservationDispositionBoundary(coordination);
        using var reservation = new Reservation(PlaybackSchedulerOutputKind.Video);

        Assert.Equal(PlaybackLateReservationDispositionResult.InvalidReservation,
            boundary.Apply(reservation, PlaybackTimedSchedulerDispatchResult.AudioLate, 1));
        Assert.False(reservation.Consumed);
        Assert.Equal(1, reservation.CompleteCalls);
    }

    [Fact]
    public void Apply_DisposedBoundary_RetainsReservationExactlyOnce()
    {
        using var policy = new PlaybackLateItemDispositionPolicyBoundary(
            new PlaybackLateItemDispositionOptions(), 1, 1);
        using var coordination = new PlaybackLateDispatchCoordinationBoundary(policy, 1, 1);
        var boundary = new PlaybackLateReservationDispositionBoundary(coordination);
        using var reservation = new Reservation(PlaybackSchedulerOutputKind.Video);
        boundary.Dispose();

        Assert.Equal(PlaybackLateReservationDispositionResult.Disposed,
            boundary.Apply(reservation, PlaybackTimedSchedulerDispatchResult.VideoLate, 1));
        Assert.False(reservation.Consumed);
        Assert.Equal(1, reservation.CompleteCalls);
    }

    [Fact]
    public void Reset_ClearsDropCountAndRestoresReady()
    {
        using var policy = new PlaybackLateItemDispositionPolicyBoundary(
            new PlaybackLateItemDispositionOptions
            {
                VideoLatePolicy = PlaybackVideoLatePolicy.MarkDropCandidate,
            }, 1, 1);
        using var coordination = new PlaybackLateDispatchCoordinationBoundary(policy, 1, 1);
        using var boundary = new PlaybackLateReservationDispositionBoundary(coordination);
        using var reservation = new Reservation(PlaybackSchedulerOutputKind.Video);
        boundary.Apply(reservation, PlaybackTimedSchedulerDispatchResult.VideoLate, 1);

        Assert.True(boundary.Reset(3, 4));
        Assert.Equal(new PlaybackLateReservationDispositionSnapshot(
            PlaybackLateReservationDispositionState.Ready, 0), boundary.Snapshot);
    }

    private sealed class Reservation(PlaybackSchedulerOutputKind kind) :
        IPlaybackWindowsOutputSchedulerReservation
    {
        public PlaybackSchedulerOutputKind Kind { get; } = kind;
        public PlaybackDecoderFfmpegManagedFrame Frame { get; } = kind == PlaybackSchedulerOutputKind.Video
            ? VideoFrame()
            : AudioFrame();
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
