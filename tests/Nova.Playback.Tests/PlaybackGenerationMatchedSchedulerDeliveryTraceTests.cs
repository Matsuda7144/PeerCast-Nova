using static PeerCast.Nova.Playback.Tests.PlaybackSourceBoundOneFrameDecodeTestSupport;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackGenerationMatchedSchedulerDeliveryTraceTests
{
    [Fact]
    public async Task Opt_in_trace_records_four_deliveries_with_relative_timestamps()
    {
        using var harness = new Harness(discontinuityGeneration: 0);
        var operations = new TraceOperations();
        await using var boundary = CreateBoundary(harness, operations, capture: true);

        for (var index = 0; index < 4; index++)
        {
            TransferFrame(
                boundary,
                harness,
                CreateAudioFrame(TimeSpan.FromMilliseconds(120 + (index * 20))));
            Assert.Equal(
                PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered,
                await boundary.DeliverOneAsync());
        }

        var trace = boundary.Snapshot.DeliveryTrace;
        Assert.Equal(4, trace.Count);
        Assert.False(trace.Overflowed);
        for (var index = 0; index < trace.Count; index++)
        {
            var observation = trace.GetObservation(index);
            Assert.Equal((long)index, observation.Sequence);
            Assert.Equal(PlaybackPolicyMediaKind.Audio, observation.MediaKind);
            Assert.True(observation.HasPresentationTimestampOffset);
            Assert.Equal(TimeSpan.FromMilliseconds(index * 20).Ticks,
                observation.PresentationTimestampOffsetTicks);
            Assert.True(observation.HasTimingDecision);
            Assert.Equal(PlaybackAvTimingDecisionResult.ProcessNow, observation.TimingResult);
            Assert.Equal(0L, observation.TimingWaitTicks);
            Assert.Equal(
                PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered,
                observation.Result);
            Assert.Equal(
                PlaybackGenerationMatchedSchedulerReservationCompletion.Delivered,
                observation.Completion);
        }
    }

    [Fact]
    public async Task Opt_in_audio_trace_ignores_video_deliveries_before_four_audio_deliveries()
    {
        using var harness = new Harness(discontinuityGeneration: 0);
        var operations = new TraceOperations();
        await using var boundary = CreateBoundary(harness, operations, capture: true);

        operations.DeliveryResults.Enqueue(
            PlaybackGenerationMatchedCanonicalDeliveryResult.VideoProcessed);
        TransferFrame(
            boundary,
            harness,
            CreateVideoFrame(TimeSpan.FromMilliseconds(100)),
            PlaybackBoundedDemuxMediaKind.Video);
        Assert.Equal(
            PlaybackGenerationMatchedSchedulerDeliveryResult.VideoDelivered,
            await boundary.DeliverOneAsync());

        for (var index = 0; index < 4; index++)
        {
            TransferFrame(
                boundary,
                harness,
                CreateAudioFrame(TimeSpan.FromMilliseconds(120 + (index * 20))),
                PlaybackBoundedDemuxMediaKind.Audio);
            Assert.Equal(
                PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered,
                await boundary.DeliverOneAsync());
        }

        var trace = boundary.Snapshot.DeliveryTrace;
        Assert.Equal(4, trace.Count);
        Assert.False(trace.Overflowed);
        for (var index = 0; index < trace.Count; index++)
        {
            var observation = trace.GetObservation(index);
            Assert.Equal(PlaybackPolicyMediaKind.Audio, observation.MediaKind);
            Assert.Equal((long)(index + 1), observation.Sequence);
            Assert.Equal(
                TimeSpan.FromMilliseconds(index * 20).Ticks,
                observation.PresentationTimestampOffsetTicks);
        }
    }

    [Fact]
    public async Task Retained_wait_and_later_delivery_coalesce_to_final_sequence_result()
    {
        using var harness = new Harness(discontinuityGeneration: 0);
        var operations = new TraceOperations();
        operations.TimingDecisions.Enqueue(new(
            PlaybackAvTimingDecisionResult.Wait,
            TimeSpan.FromMilliseconds(3)));
        operations.TimingDecisions.Enqueue(new(
            PlaybackAvTimingDecisionResult.ProcessNow,
            TimeSpan.Zero));
        await using var boundary = CreateBoundary(harness, operations, capture: true);
        TransferFrame(boundary, harness, CreateAudioFrame(TimeSpan.FromMilliseconds(120)));

        Assert.Equal(
            PlaybackGenerationMatchedSchedulerDeliveryResult.RetainedWait,
            await boundary.DeliverOneAsync());
        Assert.Equal(
            PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered,
            await boundary.DeliverOneAsync());

        var trace = boundary.Snapshot.DeliveryTrace;
        Assert.Equal(1, trace.Count);
        Assert.False(trace.Overflowed);
        var delivered = trace.GetObservation(0);
        Assert.Equal(0L, delivered.Sequence);
        Assert.Equal(PlaybackAvTimingDecisionResult.ProcessNow, delivered.TimingResult);
        Assert.Equal(0L, delivered.TimingWaitTicks);
        Assert.Equal(
            PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered,
            delivered.Result);
        Assert.Equal(
            PlaybackGenerationMatchedSchedulerReservationCompletion.Delivered,
            delivered.Completion);
    }

    [Fact]
    public async Task Fifth_observation_sets_overflow_without_replacing_first_four()
    {
        using var harness = new Harness(discontinuityGeneration: 0);
        var operations = new TraceOperations();
        await using var boundary = CreateBoundary(harness, operations, capture: true);

        for (var index = 0; index < 5; index++)
        {
            TransferFrame(
                boundary,
                harness,
                CreateAudioFrame(TimeSpan.FromMilliseconds(120 + (index * 20))));
            Assert.Equal(
                PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered,
                await boundary.DeliverOneAsync());
        }

        var first = boundary.Snapshot.DeliveryTrace;
        var second = boundary.Snapshot.DeliveryTrace;
        Assert.Equal(4, first.Count);
        Assert.True(first.Overflowed);
        Assert.Equal(first, second);
        Assert.Equal(0L, first.First.Sequence);
        Assert.Equal(3L, first.Fourth.Sequence);
        Assert.Throws<ArgumentOutOfRangeException>(() => first.GetObservation(4));
    }

    [Fact]
    public async Task Disabled_trace_retains_no_observations()
    {
        using var harness = new Harness(discontinuityGeneration: 0);
        var operations = new TraceOperations();
        await using var boundary = CreateBoundary(harness, operations, capture: false);
        TransferFrame(boundary, harness, CreateAudioFrame(TimeSpan.FromMilliseconds(120)));

        Assert.Equal(
            PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered,
            await boundary.DeliverOneAsync());

        Assert.Equal(
            default(PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot),
            boundary.Snapshot.DeliveryTrace);
    }

    private static PlaybackGenerationMatchedSchedulerDeliveryBoundary CreateBoundary(
        Harness harness,
        TraceOperations operations,
        bool capture)
    {
        var scheduler = new BoundedPlaybackScheduler(
            new PlaybackSchedulerOptions
            {
                Capacity = 8,
                MaximumOwnedBytes = 1024,
                FullPolicy = PlaybackSchedulerFullPolicy.Reject,
            },
            new PlaybackPolicyClockBoundary(new TraceClock(), new PlaybackPolicyOptions()));
        return new(
            harness.Source,
            harness.Boundary,
            harness.NativeSession,
            harness.DecoderGeneration,
            harness.DiscontinuityGeneration,
            scheduler,
            0,
            operations,
            capture);
    }

    private static void TransferFrame(
        PlaybackGenerationMatchedSchedulerDeliveryBoundary boundary,
        Harness harness,
        PlaybackDecoderFfmpegManagedFrame frame,
        PlaybackBoundedDemuxMediaKind mediaKind =
            PlaybackBoundedDemuxMediaKind.Audio)
    {
        harness.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            frame));
        var operation = harness.Boundary.ReceiveOne(
            harness.Source,
            harness.DecoderGeneration,
            mediaKind,
            StreamId(mediaKind),
            PlaybackSourceBoundOneFrameDecodeMode.Ordinary);
        Assert.Equal(PlaybackSourceBoundOneFrameDecodeResult.FrameAvailable, operation.Result);
        var completion = Assert.IsType<PlaybackSourceBoundDecodedFrameCompletion>(
            operation.Completion);
        Assert.Equal(
            PlaybackDecodedCompletionSchedulerIngressResult.Transferred,
            boundary.Transfer(completion));
    }

    private static PlaybackDecoderFfmpegManagedFrame CreateAudioFrame(TimeSpan timestamp)
    {
        var metadata = new PlaybackDecoderFfmpegFrameMetadata(
            PlaybackDecoderFfmpegFrameMediaKind.Audio,
            PlaybackDecoderFfmpegFrameFormat.AudioSigned16,
            0,
            0,
            48_000,
            2,
            1,
            PlaybackDecoderFfmpegFrameTime.Known(timestamp),
            PlaybackDecoderFfmpegFrameTime.Known(timestamp),
            PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.FromMilliseconds(20)));
        return new(
            metadata,
            [new PlaybackDecoderFfmpegManagedPlane(0, 4, 1, 4, new byte[] { 1, 2, 3, 4 })]);
    }

    private static PlaybackDecoderFfmpegManagedFrame CreateVideoFrame(TimeSpan timestamp)
    {
        var metadata = new PlaybackDecoderFfmpegFrameMetadata(
            PlaybackDecoderFfmpegFrameMediaKind.Video,
            PlaybackDecoderFfmpegFrameFormat.VideoBgra32,
            1,
            1,
            0,
            0,
            0,
            PlaybackDecoderFfmpegFrameTime.Known(timestamp),
            PlaybackDecoderFfmpegFrameTime.Known(timestamp),
            PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.FromMilliseconds(20)));
        return new(
            metadata,
            [new PlaybackDecoderFfmpegManagedPlane(0, 4, 1, 4, new byte[] { 1, 2, 3, 4 })]);
    }

    private sealed class TraceClock : IPlaybackMonotonicClock
    {
        public TimeSpan GetTimestamp() => TimeSpan.Zero;
    }

    private sealed class TraceOperations : IPlaybackGenerationMatchedSchedulerDeliveryOperations
    {
        public Queue<PlaybackAvTimingDecision> TimingDecisions { get; } = new();

        public Queue<PlaybackGenerationMatchedCanonicalDeliveryResult> DeliveryResults { get; } =
            new();

        public bool ValidateContextRole(
            PlaybackBoundedFlvNativeDecoderSession nativeSession,
            PlaybackBoundedDemuxMediaKind mediaKind) =>
            !nativeSession.IsDisposed && Enum.IsDefined(mediaKind);

        public ValueTask<PlaybackAvTimingDecision> DecideAsync(
            PlaybackAvTimingItem item,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(TimingDecisions.Count > 0
                ? TimingDecisions.Dequeue()
                : new PlaybackAvTimingDecision(
                    PlaybackAvTimingDecisionResult.ProcessNow,
                    TimeSpan.Zero));

        public ValueTask<PlaybackAvTimingResetResult> ResetTimingAsync(
            long generation,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackAvTimingResetResult.Reset);

        public ValueTask<PlaybackGenerationMatchedOutputResetResult> ResetOutputAsync(
            long generation,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackGenerationMatchedOutputResetResult.Reset);

        public ValueTask<PlaybackGenerationMatchedCanonicalDeliveryResult> DeliverAsync(
            PlaybackBoundedFlvNativeDecoderSession nativeSession,
            PlaybackDecoderFfmpegManagedFrame frame,
            long sequence,
            PlaybackDecodedFrameDiscontinuityKind discontinuity,
            long generation,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(DeliveryResults.Count > 0
                ? DeliveryResults.Dequeue()
                : PlaybackGenerationMatchedCanonicalDeliveryResult.AudioProcessed);

        public void Dispose()
        {
        }
    }
}
