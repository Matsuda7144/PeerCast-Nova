using static PeerCast.Nova.Playback.Tests.PlaybackSourceBoundOneFrameDecodeTestSupport;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackGenerationMatchedSchedulerDeliveryBoundaryTests
{
    [Fact]
    public async Task DeliverOneAsync_delivers_video_and_commits_scheduler_accounting()
    {
        using var harness = new Harness(discontinuityGeneration: 0);
        var frame = CreateFrame(PlaybackBoundedDemuxMediaKind.Video);
        var operations = new FakeOperations();
        await using var boundary = CreateBoundary(harness, operations);
        TransferFrame(boundary, harness, PlaybackBoundedDemuxMediaKind.Video, frame);

        var result = await boundary.DeliverOneAsync();

        Assert.Equal(PlaybackGenerationMatchedSchedulerDeliveryResult.VideoDelivered, result);
        Assert.True(frame.IsDisposed);
        Assert.Equal(1, operations.DeliveryCount);
        Assert.Equal(PlaybackBoundedDemuxMediaKind.Video, Assert.Single(operations.MediaKinds));
        Assert.Equal(PlaybackDecodedFrameDiscontinuityKind.None,
            Assert.Single(operations.Discontinuities));
        var scheduler = boundary.Snapshot.Ingress.Scheduler;
        Assert.Equal(0, scheduler.QueueLength);
        Assert.Equal(1, scheduler.DeliveryCount);
        Assert.Equal(0, scheduler.DropCount);
        Assert.False(boundary.Snapshot.PreferVideoNext);
    }

    [Fact]
    public async Task VideoLate_drops_reservation_without_counting_delivery()
    {
        using var harness = new Harness(discontinuityGeneration: 0);
        var frame = CreateFrame(PlaybackBoundedDemuxMediaKind.Video);
        var operations = new FakeOperations
        {
            TimingDecision = new(PlaybackAvTimingDecisionResult.VideoLate, TimeSpan.Zero),
        };
        await using var boundary = CreateBoundary(harness, operations);
        TransferFrame(boundary, harness, PlaybackBoundedDemuxMediaKind.Video, frame);

        var result = await boundary.DeliverOneAsync();

        Assert.Equal(PlaybackGenerationMatchedSchedulerDeliveryResult.VideoDropped, result);
        Assert.True(frame.IsDisposed);
        Assert.Equal(0, operations.DeliveryCount);
        var scheduler = boundary.Snapshot.Ingress.Scheduler;
        Assert.Equal(0, scheduler.QueueLength);
        Assert.Equal(0, scheduler.DeliveryCount);
        Assert.Equal(1, scheduler.DropCount);
        Assert.False(boundary.Snapshot.PreferVideoNext);
    }

    [Fact]
    public async Task WouldBlock_retains_exact_reservation_for_later_delivery()
    {
        using var harness = new Harness(discontinuityGeneration: 0);
        var frame = CreateFrame(PlaybackBoundedDemuxMediaKind.Audio);
        var operations = new FakeOperations
        {
            DeliveryResult = PlaybackGenerationMatchedCanonicalDeliveryResult.WouldBlock,
        };
        await using var boundary = CreateBoundary(harness, operations);
        TransferFrame(boundary, harness, PlaybackBoundedDemuxMediaKind.Audio, frame);

        var retained = await boundary.DeliverOneAsync();

        Assert.Equal(
            PlaybackGenerationMatchedSchedulerDeliveryResult.RetainedWouldBlock,
            retained);
        Assert.False(frame.IsDisposed);
        Assert.Equal(1, boundary.Snapshot.Ingress.Scheduler.QueueLength);
        Assert.True(boundary.Snapshot.PreferVideoNext);

        operations.DeliveryResult =
            PlaybackGenerationMatchedCanonicalDeliveryResult.AudioProcessed;
        var delivered = await boundary.DeliverOneAsync();

        Assert.Equal(PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered, delivered);
        Assert.True(frame.IsDisposed);
        Assert.Equal(0, boundary.Snapshot.Ingress.Scheduler.QueueLength);
        Assert.True(boundary.Snapshot.PreferVideoNext);
    }

    [Fact]
    public async Task First_DecoderReset_is_acknowledged_once_then_delivered_as_ordinary_frame()
    {
        using var harness = new Harness(discontinuityGeneration: 4);
        var video = CreateFrame(PlaybackBoundedDemuxMediaKind.Video);
        var audio = CreateFrame(PlaybackBoundedDemuxMediaKind.Audio);
        var operations = new FakeOperations();
        await using var boundary = CreateBoundary(harness, operations);
        TransferFrame(boundary, harness, PlaybackBoundedDemuxMediaKind.Video, video);

        var first = await boundary.DeliverOneAsync();

        Assert.Equal(PlaybackGenerationMatchedSchedulerDeliveryResult.VideoDelivered, first);
        Assert.Equal(1, operations.TimingResetCount);
        Assert.Equal(1, operations.OutputResetCount);
        Assert.False(boundary.Snapshot.DecoderResetPending);
        Assert.Equal(PlaybackDecodedFrameDiscontinuityKind.None,
            Assert.Single(operations.Discontinuities));

        TransferFrame(boundary, harness, PlaybackBoundedDemuxMediaKind.Audio, audio);
        var second = await boundary.DeliverOneAsync();

        Assert.Equal(PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered, second);
        Assert.Equal(1, operations.TimingResetCount);
        Assert.Equal(1, operations.OutputResetCount);
    }

    [Fact]
    public async Task Cancelled_reset_retains_marker_and_same_frame_until_successful_admission()
    {
        using var harness = new Harness(discontinuityGeneration: 2);
        var frame = CreateFrame(PlaybackBoundedDemuxMediaKind.Video);
        var operations = new FakeOperations
        {
            TimingResetResult = PlaybackAvTimingResetResult.Cancelled,
        };
        await using var boundary = CreateBoundary(harness, operations);
        TransferFrame(boundary, harness, PlaybackBoundedDemuxMediaKind.Video, frame);

        var cancelled = await boundary.DeliverOneAsync();

        Assert.Equal(PlaybackGenerationMatchedSchedulerDeliveryResult.Cancelled, cancelled);
        Assert.True(boundary.Snapshot.DecoderResetPending);
        Assert.False(frame.IsDisposed);
        Assert.Equal(1, boundary.Snapshot.Ingress.Scheduler.QueueLength);
        Assert.Equal(0, operations.DeliveryCount);

        operations.TimingResetResult = PlaybackAvTimingResetResult.Reset;
        var delivered = await boundary.DeliverOneAsync();

        Assert.Equal(PlaybackGenerationMatchedSchedulerDeliveryResult.VideoDelivered, delivered);
        Assert.False(boundary.Snapshot.DecoderResetPending);
        Assert.True(frame.IsDisposed);
    }

    [Fact]
    public async Task AudioLate_retains_frame_and_reports_recovery_required()
    {
        using var harness = new Harness(discontinuityGeneration: 0);
        var frame = CreateFrame(PlaybackBoundedDemuxMediaKind.Audio);
        var operations = new FakeOperations
        {
            TimingDecision = new(PlaybackAvTimingDecisionResult.AudioLate, TimeSpan.Zero),
        };
        await using var boundary = CreateBoundary(harness, operations);
        TransferFrame(boundary, harness, PlaybackBoundedDemuxMediaKind.Audio, frame);

        var result = await boundary.DeliverOneAsync();

        Assert.Equal(
            PlaybackGenerationMatchedSchedulerDeliveryResult.RetainedAudioRecoveryRequired,
            result);
        Assert.False(frame.IsDisposed);
        Assert.Equal(1, boundary.Snapshot.Ingress.Scheduler.QueueLength);
        Assert.Equal(0, operations.DeliveryCount);
    }

    [Fact]
    public async Task DeviceLost_retains_frame_until_explicit_generation_reset()
    {
        using var harness = new Harness(discontinuityGeneration: 0);
        var frame = CreateFrame(PlaybackBoundedDemuxMediaKind.Video);
        var operations = new FakeOperations
        {
            DeliveryResult = PlaybackGenerationMatchedCanonicalDeliveryResult.DeviceLost,
        };
        await using var boundary = CreateBoundary(harness, operations);
        TransferFrame(boundary, harness, PlaybackBoundedDemuxMediaKind.Video, frame);

        var lost = await boundary.DeliverOneAsync();

        Assert.Equal(PlaybackGenerationMatchedSchedulerDeliveryResult.ResetRequired, lost);
        Assert.Equal(
            PlaybackGenerationMatchedSchedulerDeliveryState.ResetRequired,
            boundary.Snapshot.State);
        Assert.False(frame.IsDisposed);

        Assert.Equal(
            PlaybackGenerationMatchedSchedulerResetResult.Reset,
            await boundary.ResetAsync());
        operations.DeliveryResult =
            PlaybackGenerationMatchedCanonicalDeliveryResult.VideoProcessed;

        var delivered = await boundary.DeliverOneAsync();

        Assert.Equal(PlaybackGenerationMatchedSchedulerDeliveryResult.VideoDelivered, delivered);
        Assert.True(frame.IsDisposed);
    }

    [Fact]
    public async Task Transfer_during_active_delivery_returns_cancelled_without_detaching_completion()
    {
        using var harness = new Harness(discontinuityGeneration: 0);
        var first = CreateFrame(PlaybackBoundedDemuxMediaKind.Video);
        var second = CreateFrame(PlaybackBoundedDemuxMediaKind.Audio);
        var operations = new FakeOperations { BlockDelivery = true };
        await using var boundary = CreateBoundary(harness, operations);
        TransferFrame(boundary, harness, PlaybackBoundedDemuxMediaKind.Video, first);

        var delivery = boundary.DeliverOneAsync().AsTask();
        await operations.DeliveryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var completion = ReceiveFrame(harness, PlaybackBoundedDemuxMediaKind.Audio, second);

        var transfer = boundary.Transfer(completion);

        Assert.Equal(PlaybackDecodedCompletionSchedulerIngressResult.Cancelled, transfer);
        Assert.False(completion.IsDisposed);
        Assert.False(second.IsDisposed);
        Assert.True(harness.Boundary.Snapshot.HasActiveDecodedCompletion);

        operations.ReleaseDelivery.TrySetResult();
        Assert.Equal(
            PlaybackGenerationMatchedSchedulerDeliveryResult.VideoDelivered,
            await delivery);
        Assert.Equal(
            PlaybackDecodedCompletionSchedulerIngressResult.Transferred,
            boundary.Transfer(completion));
    }

    [Fact]
    public async Task StopAsync_cancels_active_delivery_then_disposes_scheduler_frames_before_operations()
    {
        using var harness = new Harness(discontinuityGeneration: 0);
        var frame = CreateFrame(PlaybackBoundedDemuxMediaKind.Video);
        var operations = new FakeOperations { WaitForCancellation = true };
        var boundary = CreateBoundary(harness, operations);
        TransferFrame(boundary, harness, PlaybackBoundedDemuxMediaKind.Video, frame);

        var delivery = boundary.DeliverOneAsync().AsTask();
        await operations.DeliveryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await boundary.StopAsync();

        Assert.Equal(
            PlaybackGenerationMatchedSchedulerDeliveryResult.Cancelled,
            await delivery);
        Assert.True(frame.IsDisposed);
        Assert.True(operations.IsDisposed);
        Assert.True(operations.FrameWasDisposedAtDispose);
        Assert.Equal(
            PlaybackGenerationMatchedSchedulerDeliveryState.Disposed,
            boundary.Snapshot.State);
    }

    [Fact]
    public async Task Undefined_delivery_result_faults_generation_and_retains_reservation_until_cleanup()
    {
        using var harness = new Harness(discontinuityGeneration: 0);
        var frame = CreateFrame(PlaybackBoundedDemuxMediaKind.Video);
        var operations = new FakeOperations
        {
            DeliveryResult = (PlaybackGenerationMatchedCanonicalDeliveryResult)999,
        };
        await using var boundary = CreateBoundary(harness, operations);
        TransferFrame(boundary, harness, PlaybackBoundedDemuxMediaKind.Video, frame);

        var result = await boundary.DeliverOneAsync();

        Assert.Equal(PlaybackGenerationMatchedSchedulerDeliveryResult.Faulted, result);
        Assert.Equal(
            PlaybackGenerationMatchedSchedulerDeliveryState.Faulted,
            boundary.Snapshot.State);
        Assert.False(frame.IsDisposed);
        Assert.Equal(1, boundary.Snapshot.Ingress.Scheduler.QueueLength);
    }

    private static PlaybackGenerationMatchedSchedulerDeliveryBoundary CreateBoundary(
        Harness harness,
        FakeOperations operations)
    {
        operations.OwnedFramesDisposed = () =>
            operations.OwnedFrames.All(static frame => frame.IsDisposed);
        var scheduler = new BoundedPlaybackScheduler(
            new PlaybackSchedulerOptions
            {
                Capacity = 8,
                MaximumOwnedBytes = 1024,
                FullPolicy = PlaybackSchedulerFullPolicy.Reject,
            },
            new PlaybackPolicyClockBoundary(new FakeClock(), new PlaybackPolicyOptions()));
        return new(
            harness.Source,
            harness.Boundary,
            harness.NativeSession,
            harness.DecoderGeneration,
            harness.DiscontinuityGeneration,
            scheduler,
            0,
            operations);
    }

    private static void TransferFrame(
        PlaybackGenerationMatchedSchedulerDeliveryBoundary boundary,
        Harness harness,
        PlaybackBoundedDemuxMediaKind mediaKind,
        PlaybackDecoderFfmpegManagedFrame frame)
    {
        var completion = ReceiveFrame(harness, mediaKind, frame);
        Assert.Equal(
            PlaybackDecodedCompletionSchedulerIngressResult.Transferred,
            boundary.Transfer(completion));
    }

    private static PlaybackSourceBoundDecodedFrameCompletion ReceiveFrame(
        Harness harness,
        PlaybackBoundedDemuxMediaKind mediaKind,
        PlaybackDecoderFfmpegManagedFrame frame)
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
        return Assert.IsType<PlaybackSourceBoundDecodedFrameCompletion>(operation.Completion);
    }

    private sealed class FakeClock : IPlaybackMonotonicClock
    {
        public TimeSpan GetTimestamp() => TimeSpan.Zero;
    }

    private sealed class FakeOperations : IPlaybackGenerationMatchedSchedulerDeliveryOperations
    {
        public PlaybackAvTimingDecision TimingDecision { get; set; } =
            new(PlaybackAvTimingDecisionResult.ProcessNow, TimeSpan.Zero);

        public PlaybackAvTimingResetResult TimingResetResult { get; set; } =
            PlaybackAvTimingResetResult.Reset;

        public PlaybackGenerationMatchedOutputResetResult OutputResetResult { get; set; } =
            PlaybackGenerationMatchedOutputResetResult.Reset;

        public PlaybackGenerationMatchedCanonicalDeliveryResult DeliveryResult { get; set; } =
            PlaybackGenerationMatchedCanonicalDeliveryResult.VideoProcessed;

        public bool BlockDelivery { get; init; }

        public bool WaitForCancellation { get; init; }

        public TaskCompletionSource DeliveryEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseDelivery { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<PlaybackBoundedDemuxMediaKind> MediaKinds { get; } = [];

        public List<PlaybackDecodedFrameDiscontinuityKind> Discontinuities { get; } = [];

        public List<PlaybackDecoderFfmpegManagedFrame> OwnedFrames { get; } = [];

        public Func<bool> OwnedFramesDisposed { get; set; } = static () => true;

        public int DeliveryCount { get; private set; }

        public int TimingResetCount { get; private set; }

        public int OutputResetCount { get; private set; }

        public bool IsDisposed { get; private set; }

        public bool FrameWasDisposedAtDispose { get; private set; }

        public bool ValidateContextRole(
            PlaybackBoundedFlvNativeDecoderSession nativeSession,
            PlaybackBoundedDemuxMediaKind mediaKind) =>
            !nativeSession.IsDisposed && Enum.IsDefined(mediaKind);

        public ValueTask<PlaybackAvTimingDecision> DecideAsync(
            PlaybackAvTimingItem item,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(TimingDecision);

        public ValueTask<PlaybackAvTimingResetResult> ResetTimingAsync(
            long generation,
            CancellationToken cancellationToken)
        {
            TimingResetCount++;
            return ValueTask.FromResult(TimingResetResult);
        }

        public ValueTask<PlaybackGenerationMatchedOutputResetResult> ResetOutputAsync(
            long generation,
            CancellationToken cancellationToken)
        {
            OutputResetCount++;
            return ValueTask.FromResult(OutputResetResult);
        }

        public async ValueTask<PlaybackGenerationMatchedCanonicalDeliveryResult> DeliverAsync(
            PlaybackBoundedFlvNativeDecoderSession nativeSession,
            PlaybackDecoderFfmpegManagedFrame frame,
            long sequence,
            PlaybackDecodedFrameDiscontinuityKind discontinuity,
            long generation,
            CancellationToken cancellationToken)
        {
            DeliveryCount++;
            MediaKinds.Add(frame.Metadata.MediaKind == PlaybackDecoderFfmpegFrameMediaKind.Video
                ? PlaybackBoundedDemuxMediaKind.Video
                : PlaybackBoundedDemuxMediaKind.Audio);
            Discontinuities.Add(discontinuity);
            OwnedFrames.Add(frame);
            DeliveryEntered.TrySetResult();
            if (WaitForCancellation)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return PlaybackGenerationMatchedCanonicalDeliveryResult.Cancelled;
                }
            }
            else if (BlockDelivery)
            {
                await ReleaseDelivery.Task.WaitAsync(cancellationToken);
            }

            if (DeliveryResult == PlaybackGenerationMatchedCanonicalDeliveryResult.VideoProcessed &&
                frame.Metadata.MediaKind == PlaybackDecoderFfmpegFrameMediaKind.Audio)
            {
                return PlaybackGenerationMatchedCanonicalDeliveryResult.AudioProcessed;
            }

            return DeliveryResult;
        }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            FrameWasDisposedAtDispose = OwnedFramesDisposed();
            IsDisposed = true;
        }
    }
}
