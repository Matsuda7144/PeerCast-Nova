namespace PeerCast.Nova.Playback.Tests;

public sealed class BoundedPlaybackSchedulerRestrictedOutputTests
{
    private static readonly PlaybackDecodedFrameDiscontinuityKind None =
        PlaybackDecodedFrameDiscontinuityKind.None;

    [Fact]
    public async Task RestrictedAudioSelectionReservesConsecutiveAudioAndRetainsVideo()
    {
        using var scheduler = CreateScheduler(restrictOutputToInitialKind: true);
        var video = VideoFrame(0);
        var firstAudio = AudioFrame(0);
        var secondAudio = AudioFrame(20);
        Assert.Equal(PlaybackSchedulerEnqueueResult.Enqueued, scheduler.Enqueue(video, 1, None));
        Assert.Equal(
            PlaybackSchedulerEnqueueResult.Enqueued,
            scheduler.Enqueue(firstAudio, 2, None));
        Assert.Equal(
            PlaybackSchedulerEnqueueResult.Enqueued,
            scheduler.Enqueue(secondAudio, 3, None));

        using (var first = await scheduler.ReserveOutputAsync(
                   PlaybackSchedulerOutputKind.Video,
                   CancellationToken.None))
        {
            Assert.NotNull(first);
            Assert.Equal(2, first.Sequence);
            Assert.Equal(
                PlaybackDecoderFfmpegFrameMediaKind.Audio,
                first.Frame.Metadata.MediaKind);
            first.Complete(PlaybackSchedulerOutputCompletion.Delivered);
        }

        using (var second = await scheduler.ReserveOutputAsync(
                   PlaybackSchedulerOutputKind.Video,
                   CancellationToken.None))
        {
            Assert.NotNull(second);
            Assert.Equal(3, second.Sequence);
            Assert.Equal(
                PlaybackDecoderFfmpegFrameMediaKind.Audio,
                second.Frame.Metadata.MediaKind);
            second.Complete(PlaybackSchedulerOutputCompletion.Delivered);
        }

        Assert.True(firstAudio.IsDisposed);
        Assert.True(secondAudio.IsDisposed);
        Assert.False(video.IsDisposed);
        Assert.Equal(new PlaybackSchedulerSnapshot(1, 0, 2, 4), scheduler.GetSnapshot());
    }

    [Fact]
    public async Task RestrictedAudioSelectionDoesNotFallbackToQueuedVideo()
    {
        using var scheduler = CreateScheduler(restrictOutputToInitialKind: true);
        var video = VideoFrame(0);
        Assert.Equal(PlaybackSchedulerEnqueueResult.Enqueued, scheduler.Enqueue(video, 1, None));

        var reservation = await scheduler.ReserveOutputAsync(
            PlaybackSchedulerOutputKind.Video,
            CancellationToken.None);

        Assert.Null(reservation);
        Assert.False(video.IsDisposed);
        Assert.Equal(new PlaybackSchedulerSnapshot(1, 0, 0, 4), scheduler.GetSnapshot());
    }

    [Fact]
    public async Task UnrestrictedSelectionKeepsCallerPreferenceAndFallback()
    {
        using var scheduler = CreateScheduler(restrictOutputToInitialKind: false);
        var firstAudio = AudioFrame(0);
        var video = VideoFrame(0);
        var secondAudio = AudioFrame(20);
        scheduler.Enqueue(firstAudio, 1, None);
        scheduler.Enqueue(video, 2, None);
        scheduler.Enqueue(secondAudio, 3, None);

        using (var first = await scheduler.ReserveOutputAsync(
                   PlaybackSchedulerOutputKind.Video,
                   CancellationToken.None))
        {
            Assert.NotNull(first);
            Assert.Equal(1, first.Sequence);
            first.Complete(PlaybackSchedulerOutputCompletion.Delivered);
        }

        using (var second = await scheduler.ReserveOutputAsync(
                   PlaybackSchedulerOutputKind.Video,
                   CancellationToken.None))
        {
            Assert.NotNull(second);
            Assert.Equal(2, second.Sequence);
            Assert.Equal(
                PlaybackDecoderFfmpegFrameMediaKind.Video,
                second.Frame.Metadata.MediaKind);
            second.Complete(PlaybackSchedulerOutputCompletion.Delivered);
        }

        using var fallback = await scheduler.ReserveOutputAsync(
            PlaybackSchedulerOutputKind.Video,
            CancellationToken.None);
        Assert.NotNull(fallback);
        Assert.Equal(3, fallback.Sequence);
        Assert.Equal(
            PlaybackDecoderFfmpegFrameMediaKind.Audio,
            fallback.Frame.Metadata.MediaKind);
    }

    private static BoundedPlaybackScheduler CreateScheduler(
        bool restrictOutputToInitialKind) => new(
            new PlaybackSchedulerOptions
            {
                Capacity = 8,
                MaximumOwnedBytes = 1_024,
                FullPolicy = PlaybackSchedulerFullPolicy.Reject,
                InitialOutputKind = PlaybackSchedulerInitialOutputKind.Audio,
                RestrictOutputToInitialKind = restrictOutputToInitialKind,
            },
            new PlaybackPolicyClockBoundary(
                new FixedClock(),
                new PlaybackPolicyOptions()));

    private static PlaybackDecoderFfmpegManagedFrame AudioFrame(int timestampMs) => new(
        new PlaybackDecoderFfmpegFrameMetadata(
            PlaybackDecoderFfmpegFrameMediaKind.Audio,
            PlaybackDecoderFfmpegFrameFormat.AudioSigned16,
            0,
            0,
            48_000,
            2,
            2,
            PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.FromMilliseconds(timestampMs)),
            PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.FromMilliseconds(timestampMs)),
            PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.FromMilliseconds(20))),
        [new(0, 8, 1, 8, new byte[8])]);

    private static PlaybackDecoderFfmpegManagedFrame VideoFrame(int timestampMs) => new(
        new PlaybackDecoderFfmpegFrameMetadata(
            PlaybackDecoderFfmpegFrameMediaKind.Video,
            PlaybackDecoderFfmpegFrameFormat.VideoBgra32,
            1,
            1,
            0,
            0,
            0,
            PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.FromMilliseconds(timestampMs)),
            PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.FromMilliseconds(timestampMs)),
            PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.FromMilliseconds(20))),
        [new(0, 4, 1, 4, new byte[4])]);

    private sealed class FixedClock : IPlaybackMonotonicClock
    {
        public TimeSpan GetTimestamp() => TimeSpan.Zero;
    }
}
