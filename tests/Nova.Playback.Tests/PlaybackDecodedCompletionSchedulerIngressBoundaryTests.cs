using static PeerCast.Nova.Playback.Tests.PlaybackSourceBoundOneFrameDecodeTestSupport;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDecodedCompletionSchedulerIngressBoundaryTests
{
    [Fact]
    public void Completion_detaches_exact_active_frame_once_without_disposing_transferred_ownership()
    {
        using var harness = new Harness(discontinuityGeneration: 0);
        var frame = CreateFrame(PlaybackBoundedDemuxMediaKind.Video);
        var completion = ReceiveFrame(harness, PlaybackBoundedDemuxMediaKind.Video, frame);

        Assert.True(completion.TryDetachFrame(out var detached));
        Assert.Same(frame, detached);
        Assert.False(completion.TryDetachFrame(out _));
        Assert.Throws<ObjectDisposedException>(() => _ = completion.Frame);

        completion.Dispose();

        Assert.True(completion.IsDisposed);
        Assert.False(frame.IsDisposed);
        Assert.False(harness.Boundary.Snapshot.HasActiveDecodedCompletion);
        detached!.Dispose();
        Assert.True(frame.IsDisposed);
    }

    [Fact]
    public void Transfer_detaches_frame_releases_decode_backpressure_and_enqueues_once()
    {
        using var harness = new Harness(discontinuityGeneration: 0);
        var frame = CreateFrame(PlaybackBoundedDemuxMediaKind.Video);
        var completion = ReceiveFrame(harness, PlaybackBoundedDemuxMediaKind.Video, frame);
        using var context = new FakeContextLifetime(() => frame.IsDisposed);
        using var ingress = CreateIngress(harness, context);

        var result = ingress.Transfer(completion);

        Assert.Equal(PlaybackDecodedCompletionSchedulerIngressResult.Transferred, result);
        Assert.True(completion.IsDisposed);
        Assert.False(frame.IsDisposed);
        Assert.False(harness.Boundary.Snapshot.HasActiveDecodedCompletion);
        Assert.Equal(1, ingress.Snapshot.Scheduler.QueueLength);
        Assert.Equal(1, ingress.Snapshot.NextVideoSourceSequence);
        Assert.Equal(0, ingress.Snapshot.NextAudioSourceSequence);
        Assert.Equal(1, ingress.Snapshot.NextSchedulerSequence);
        Assert.Equal(1, ingress.Snapshot.AcceptedTransferCount);
    }

    [Fact]
    public void Transfer_uses_generation_wide_sequence_for_stream_local_video_and_audio_zeroes()
    {
        using var harness = new Harness(discontinuityGeneration: 0);
        var video = CreateFrame(PlaybackBoundedDemuxMediaKind.Video);
        var audio = CreateFrame(PlaybackBoundedDemuxMediaKind.Audio);
        harness.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            video));
        harness.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            audio));
        using var context = new FakeContextLifetime(() => video.IsDisposed && audio.IsDisposed);
        var operations = new FakeIngressOperations();
        using var ingress = CreateIngress(harness, context, operations: operations);

        var videoCompletion = ReceiveFrame(harness, PlaybackBoundedDemuxMediaKind.Video);
        Assert.Equal(0, videoCompletion.Sequence);
        Assert.Equal(
            PlaybackDecodedCompletionSchedulerIngressResult.Transferred,
            ingress.Transfer(videoCompletion));
        var audioCompletion = ReceiveFrame(harness, PlaybackBoundedDemuxMediaKind.Audio);
        Assert.Equal(0, audioCompletion.Sequence);
        Assert.Equal(
            PlaybackDecodedCompletionSchedulerIngressResult.Transferred,
            ingress.Transfer(audioCompletion));

        Assert.Equal(2, operations.EnqueueCount);
        Assert.Collection(
            operations.Sequences,
            sequence => Assert.Equal(0L, sequence),
            sequence => Assert.Equal(1L, sequence));
        Assert.Equal(2, ingress.Snapshot.Scheduler.QueueLength);
        Assert.Equal(1, ingress.Snapshot.NextVideoSourceSequence);
        Assert.Equal(1, ingress.Snapshot.NextAudioSourceSequence);
        Assert.Equal(2, ingress.Snapshot.NextSchedulerSequence);
        Assert.Equal(2, ingress.Snapshot.AcceptedTransferCount);
    }

    [Fact]
    public void Transfer_cancellation_before_detach_preserves_completion_and_reset_until_acceptance()
    {
        using var harness = new Harness(discontinuityGeneration: 4);
        var frame = CreateFrame(PlaybackBoundedDemuxMediaKind.Video);
        var completion = ReceiveFrame(harness, PlaybackBoundedDemuxMediaKind.Video, frame);
        using var context = new FakeContextLifetime(() => frame.IsDisposed);
        var operations = new FakeIngressOperations();
        using var ingress = CreateIngress(harness, context, operations: operations);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var cancelled = ingress.Transfer(completion, cancellation.Token);

        Assert.Equal(PlaybackDecodedCompletionSchedulerIngressResult.Cancelled, cancelled);
        Assert.False(completion.IsDisposed);
        Assert.False(frame.IsDisposed);
        Assert.True(harness.Boundary.Snapshot.HasActiveDecodedCompletion);
        Assert.Equal(0, operations.EnqueueCount);
        Assert.True(ingress.Snapshot.DecoderResetPending);
        Assert.Equal(0, ingress.Snapshot.NextSchedulerSequence);
        Assert.Equal(PlaybackDecodedCompletionSchedulerIngressState.Accepting, ingress.Snapshot.State);

        Assert.Equal(
            PlaybackDecodedCompletionSchedulerIngressResult.Transferred,
            ingress.Transfer(completion));
        Assert.Equal(1, operations.EnqueueCount);
        Assert.Equal(
            PlaybackDecodedFrameDiscontinuityKind.DecoderReset,
            Assert.Single(operations.Discontinuities));
        Assert.False(ingress.Snapshot.DecoderResetPending);
    }

    [Fact]
    public void Transfer_rejected_enqueue_is_terminal_without_sequence_commit_or_context_leak()
    {
        using var harness = new Harness(discontinuityGeneration: 0);
        var firstFrame = CreateFrame(PlaybackBoundedDemuxMediaKind.Video);
        var secondFrame = CreateFrame(PlaybackBoundedDemuxMediaKind.Video);
        harness.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            firstFrame));
        harness.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            secondFrame));
        using var context = new FakeContextLifetime(
            () => firstFrame.IsDisposed && secondFrame.IsDisposed);
        using var ingress = CreateIngress(
            harness,
            context,
            capacity: 1,
            fullPolicy: PlaybackSchedulerFullPolicy.Reject);

        Assert.Equal(
            PlaybackDecodedCompletionSchedulerIngressResult.Transferred,
            ingress.Transfer(ReceiveFrame(harness, PlaybackBoundedDemuxMediaKind.Video)));
        var rejected = ingress.Transfer(
            ReceiveFrame(harness, PlaybackBoundedDemuxMediaKind.Video));

        Assert.Equal(PlaybackDecodedCompletionSchedulerIngressResult.EnqueueRejected, rejected);
        Assert.True(firstFrame.IsDisposed);
        Assert.True(secondFrame.IsDisposed);
        Assert.True(context.IsDisposed);
        Assert.True(context.OwnedFrameDisposedAtDispose);
        Assert.Equal(PlaybackDecodedCompletionSchedulerIngressState.Faulted, ingress.Snapshot.State);
        Assert.Equal(1, ingress.Snapshot.NextVideoSourceSequence);
        Assert.Equal(1, ingress.Snapshot.NextSchedulerSequence);
        Assert.Equal(1, ingress.Snapshot.AcceptedTransferCount);
    }

    [Fact]
    public void Transfer_rejects_generation_mismatch_and_disposes_completion_terminally()
    {
        using var sourceHarness = new Harness(decoderGeneration: 7, discontinuityGeneration: 0);
        using var ingressHarness = new Harness(decoderGeneration: 8, discontinuityGeneration: 0);
        var frame = CreateFrame(PlaybackBoundedDemuxMediaKind.Audio);
        var completion = ReceiveFrame(sourceHarness, PlaybackBoundedDemuxMediaKind.Audio, frame);
        using var context = new FakeContextLifetime(() => true);
        using var ingress = CreateIngress(ingressHarness, context);

        var result = ingress.Transfer(completion);

        Assert.Equal(PlaybackDecodedCompletionSchedulerIngressResult.InvalidCompletion, result);
        Assert.True(completion.IsDisposed);
        Assert.True(frame.IsDisposed);
        Assert.True(context.IsDisposed);
        Assert.Equal(PlaybackDecodedCompletionSchedulerIngressState.Faulted, ingress.Snapshot.State);
        Assert.Equal(0, ingress.Snapshot.NextSchedulerSequence);
    }

    [Fact]
    public void Transfer_same_completion_twice_fails_closed_without_second_enqueue()
    {
        using var harness = new Harness(discontinuityGeneration: 0);
        var frame = CreateFrame(PlaybackBoundedDemuxMediaKind.Video);
        var completion = ReceiveFrame(harness, PlaybackBoundedDemuxMediaKind.Video, frame);
        using var context = new FakeContextLifetime(() => frame.IsDisposed);
        var operations = new FakeIngressOperations();
        using var ingress = CreateIngress(harness, context, operations: operations);

        Assert.Equal(
            PlaybackDecodedCompletionSchedulerIngressResult.Transferred,
            ingress.Transfer(completion));
        var second = ingress.Transfer(completion);

        Assert.Equal(PlaybackDecodedCompletionSchedulerIngressResult.InvalidCompletion, second);
        Assert.Equal(1, operations.EnqueueCount);
        Assert.True(frame.IsDisposed);
        Assert.True(context.IsDisposed);
        Assert.Equal(PlaybackDecodedCompletionSchedulerIngressState.Faulted, ingress.Snapshot.State);
        Assert.Equal(1, ingress.Snapshot.NextSchedulerSequence);
    }

    [Fact]
    public void Transfer_sequence_exhaustion_is_terminal_before_detach()
    {
        using var harness = new Harness(discontinuityGeneration: 0);
        var frame = CreateFrame(PlaybackBoundedDemuxMediaKind.Audio);
        var completion = ReceiveFrame(harness, PlaybackBoundedDemuxMediaKind.Audio, frame);
        using var context = new FakeContextLifetime(() => frame.IsDisposed);
        using var ingress = CreateIngress(
            harness,
            context,
            initialSchedulerSequence: long.MaxValue);

        var result = ingress.Transfer(completion);

        Assert.Equal(PlaybackDecodedCompletionSchedulerIngressResult.SequenceExhausted, result);
        Assert.True(completion.IsDisposed);
        Assert.True(frame.IsDisposed);
        Assert.True(context.IsDisposed);
        Assert.Equal(long.MaxValue, ingress.Snapshot.NextSchedulerSequence);
        Assert.Equal(0, ingress.Snapshot.AcceptedTransferCount);
    }

    [Fact]
    public void Transfer_undefined_enqueue_result_is_terminal_and_never_commits_sequence()
    {
        using var harness = new Harness(discontinuityGeneration: 0);
        var frame = CreateFrame(PlaybackBoundedDemuxMediaKind.Video);
        var completion = ReceiveFrame(harness, PlaybackBoundedDemuxMediaKind.Video, frame);
        using var context = new FakeContextLifetime(() => frame.IsDisposed);
        var operations = new FakeIngressOperations
        {
            ForcedResult = (PlaybackSchedulerEnqueueResult)999,
        };
        using var ingress = CreateIngress(harness, context, operations: operations);

        var result = ingress.Transfer(completion);

        Assert.Equal(PlaybackDecodedCompletionSchedulerIngressResult.EnqueueRejected, result);
        Assert.Equal(1, operations.EnqueueCount);
        Assert.True(frame.IsDisposed);
        Assert.True(context.IsDisposed);
        Assert.Equal(0, ingress.Snapshot.NextSchedulerSequence);
        Assert.Equal(0, ingress.Snapshot.AcceptedTransferCount);
    }

    [Fact]
    public void Transfer_enqueue_exception_disposes_detached_frame_before_context()
    {
        using var harness = new Harness(discontinuityGeneration: 0);
        var frame = CreateFrame(PlaybackBoundedDemuxMediaKind.Audio);
        var completion = ReceiveFrame(harness, PlaybackBoundedDemuxMediaKind.Audio, frame);
        using var context = new FakeContextLifetime(() => frame.IsDisposed);
        var operations = new FakeIngressOperations
        {
            ThrowBeforeConsume = true,
        };
        using var ingress = CreateIngress(harness, context, operations: operations);

        var result = ingress.Transfer(completion);

        Assert.Equal(PlaybackDecodedCompletionSchedulerIngressResult.Faulted, result);
        Assert.Equal(1, operations.EnqueueCount);
        Assert.True(frame.IsDisposed);
        Assert.True(context.IsDisposed);
        Assert.True(context.OwnedFrameDisposedAtDispose);
        Assert.Equal(0, ingress.Snapshot.NextSchedulerSequence);
    }

    [Fact]
    public void Dispose_releases_scheduler_frames_before_decoder_context_lifetime()
    {
        using var harness = new Harness(discontinuityGeneration: 0);
        var frame = CreateFrame(PlaybackBoundedDemuxMediaKind.Video);
        var completion = ReceiveFrame(harness, PlaybackBoundedDemuxMediaKind.Video, frame);
        var context = new FakeContextLifetime(() => frame.IsDisposed);
        var ingress = CreateIngress(harness, context);
        Assert.Equal(
            PlaybackDecodedCompletionSchedulerIngressResult.Transferred,
            ingress.Transfer(completion));

        ingress.Dispose();
        ingress.Dispose();

        Assert.True(frame.IsDisposed);
        Assert.True(context.IsDisposed);
        Assert.True(context.OwnedFrameDisposedAtDispose);
        Assert.Equal(PlaybackDecodedCompletionSchedulerIngressState.Disposed, ingress.Snapshot.State);
    }

    [Fact]
    public void Transfer_DropOldestKeepsBoundedCapacityAndOwnershipAcrossSeventeenFrames()
    {
        using var harness = new Harness(discontinuityGeneration: 0);
        var frames = new List<PlaybackDecoderFfmpegManagedFrame>();
        var context = new FakeContextLifetime(() => frames.All(frame => frame.IsDisposed));
        var operations = new FakeIngressOperations();
        var ingress = CreateIngress(
            harness,
            context,
            capacity: PlaybackSchedulerOptions.DefaultCapacity,
            fullPolicy: PlaybackSchedulerFullPolicy.DropOldest,
            maximumOwnedBytes: PlaybackSchedulerOptions.DefaultMaximumOwnedBytes,
            operations: operations);

        for (var index = 0; index <= PlaybackSchedulerOptions.DefaultCapacity; index++)
        {
            var frame = CreateFrame(PlaybackBoundedDemuxMediaKind.Video);
            frames.Add(frame);

            Assert.Equal(
                PlaybackDecodedCompletionSchedulerIngressResult.Transferred,
                ingress.Transfer(ReceiveFrame(
                    harness,
                    PlaybackBoundedDemuxMediaKind.Video,
                    frame)));
        }

        Assert.All(
            operations.Results.Take(PlaybackSchedulerOptions.DefaultCapacity),
            result => Assert.Equal(PlaybackSchedulerEnqueueResult.Enqueued, result));
        Assert.Equal(
            PlaybackSchedulerEnqueueResult.EnqueuedAfterDroppingOldest,
            operations.Results[PlaybackSchedulerOptions.DefaultCapacity]);
        Assert.Equal(PlaybackSchedulerOptions.DefaultCapacity, ingress.Snapshot.Scheduler.QueueLength);
        Assert.Equal(1, ingress.Snapshot.Scheduler.DropCount);
        Assert.Equal(PlaybackDecodedCompletionSchedulerIngressState.Accepting, ingress.Snapshot.State);
        Assert.Equal(PlaybackDecodedCompletionSchedulerIngressResult.Transferred, ingress.Snapshot.Result);
        Assert.Equal(17, ingress.Snapshot.AcceptedTransferCount);
        Assert.True(frames[0].IsDisposed);
        Assert.All(frames.Skip(1), frame => Assert.False(frame.IsDisposed));

        ingress.Dispose();

        Assert.All(frames, frame => Assert.True(frame.IsDisposed));
        Assert.True(context.IsDisposed);
        Assert.True(context.OwnedFrameDisposedAtDispose);
    }

    private static PlaybackDecodedCompletionSchedulerIngressBoundary CreateIngress(
        Harness harness,
        FakeContextLifetime context,
        int capacity = 4,
        PlaybackSchedulerFullPolicy fullPolicy = PlaybackSchedulerFullPolicy.Reject,
        long maximumOwnedBytes = 1024,
        long initialSchedulerSequence = 0,
        FakeIngressOperations? operations = null)
    {
        var scheduler = new BoundedPlaybackScheduler(
            new PlaybackSchedulerOptions
            {
                Capacity = capacity,
                MaximumOwnedBytes = maximumOwnedBytes,
                FullPolicy = fullPolicy,
            },
            new PlaybackPolicyClockBoundary(
                new FakeClock(),
                new PlaybackPolicyOptions()));
        return operations is null
            ? new(
                harness.Source,
                harness.DecoderGeneration,
                harness.DiscontinuityGeneration,
                scheduler,
                context,
                initialSchedulerSequence)
            : new(
                harness.Source,
                harness.DecoderGeneration,
                harness.DiscontinuityGeneration,
                scheduler,
                context,
                initialSchedulerSequence,
                operations);
    }

    private static PlaybackSourceBoundDecodedFrameCompletion ReceiveFrame(
        Harness harness,
        PlaybackBoundedDemuxMediaKind mediaKind,
        PlaybackDecoderFfmpegManagedFrame? frame = null)
    {
        if (frame is not null)
        {
            harness.ReceiveOperations.Results.Enqueue(new(
                PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
                frame));
        }

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

    private sealed class FakeIngressOperations :
        IPlaybackDecodedCompletionSchedulerIngressOperations
    {
        public int EnqueueCount { get; private set; }

        public List<long> Sequences { get; } = [];

        public List<PlaybackDecodedFrameDiscontinuityKind> Discontinuities { get; } = [];

        public List<PlaybackSchedulerEnqueueResult> Results { get; } = [];

        public PlaybackSchedulerEnqueueResult? ForcedResult { get; init; }

        public bool ThrowBeforeConsume { get; init; }

        public PlaybackSchedulerEnqueueResult Enqueue(
            BoundedPlaybackScheduler scheduler,
            PlaybackDecoderFfmpegManagedFrame frame,
            long sequence,
            PlaybackDecodedFrameDiscontinuityKind discontinuity)
        {
            EnqueueCount++;
            Sequences.Add(sequence);
            Discontinuities.Add(discontinuity);
            if (ThrowBeforeConsume)
            {
                throw new InvalidOperationException("Injected enqueue failure.");
            }

            if (ForcedResult is { } forced)
            {
                frame.Dispose();
                return forced;
            }

            var result = scheduler.Enqueue(frame, sequence, discontinuity);
            Results.Add(result);
            return result;
        }
    }

    private sealed class FakeContextLifetime(Func<bool> ownedFrameDisposed) : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public bool OwnedFrameDisposedAtDispose { get; private set; }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            OwnedFrameDisposedAtDispose = ownedFrameDisposed();
            IsDisposed = true;
        }
    }
}
