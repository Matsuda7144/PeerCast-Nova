using static PeerCast.Nova.Playback.Tests.PlaybackSourceBoundOneFrameDecodeTestSupport;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackSourceBoundOneFrameDecodeBackpressureTests
{
    [Fact]
    public void Pending_packet_retries_only_after_decoded_completion_is_released()
    {
        using var harness = new Harness();
        harness.PacketOperations.SubmitResults.Enqueue(
            PlaybackMetadataAwareNativePacketResult.NeedFrameDrain);
        harness.PacketOperations.RetryResults.Enqueue(
            PlaybackMetadataAwareNativePacketResult.Submitted);
        var sourceCompletion = CreatePacketCompletion(
            harness.Source,
            PlaybackBoundedDemuxMediaKind.Video);
        Assert.Equal(
            PlaybackFiniteOneEventPacketSubmissionResult.ReceiveRequired,
            harness.Boundary.BeginSubmission(sourceCompletion));
        harness.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            CreateFrame(PlaybackBoundedDemuxMediaKind.Video)));

        var relief = ReceiveRelief(harness, PlaybackBoundedDemuxMediaKind.Video);
        var blocked = harness.Boundary.ReceiveOne(
            harness.Source,
            harness.DecoderGeneration,
            PlaybackBoundedDemuxMediaKind.Video,
            0,
            PlaybackSourceBoundOneFrameDecodeMode.PendingSubmissionRelief);

        Assert.Equal(PlaybackSourceBoundOneFrameDecodeResult.ActiveCompletion, blocked.Result);
        Assert.Equal(0, harness.PacketOperations.RetryCount);
        Assert.False(sourceCompletion.IsDisposed);
        relief.Dispose();

        var retry = harness.Boundary.ReceiveOne(
            harness.Source,
            harness.DecoderGeneration,
            PlaybackBoundedDemuxMediaKind.Video,
            0,
            PlaybackSourceBoundOneFrameDecodeMode.PendingSubmissionRelief);

        Assert.Equal(PlaybackSourceBoundOneFrameDecodeResult.SubmissionAccepted, retry.Result);
        Assert.Equal(
            PlaybackFiniteOneEventPacketSubmissionResult.FrameProgressAndAccepted,
            retry.SubmissionResult);
        Assert.Equal(1, harness.ReceiveOperations.ReceiveCount);
        Assert.Equal(1, harness.PacketOperations.RetryCount);
        Assert.Equal(0, harness.PacketOperations.ReceiveCount);
        Assert.True(sourceCompletion.IsDisposed);
        Assert.False(harness.Boundary.Snapshot.HasActiveSubmission);
    }

    [Fact]
    public void Pending_flush_uses_the_same_frame_before_retry_ordering()
    {
        using var harness = new Harness();
        harness.PacketOperations.SubmitResults.Enqueue(
            PlaybackMetadataAwareNativePacketResult.NeedFrameDrain);
        harness.PacketOperations.RetryResults.Enqueue(
            PlaybackMetadataAwareNativePacketResult.Submitted);
        var sourceCompletion = CreateEndOfStreamCompletion(
            harness.Source,
            PlaybackBoundedDemuxMediaKind.Audio);
        Assert.Equal(
            PlaybackFiniteOneEventPacketSubmissionResult.ReceiveRequired,
            harness.Boundary.BeginSubmission(sourceCompletion));
        harness.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            CreateFrame(PlaybackBoundedDemuxMediaKind.Audio)));

        var relief = ReceiveRelief(harness, PlaybackBoundedDemuxMediaKind.Audio);
        Assert.Equal(0, harness.PacketOperations.RetryCount);
        relief.Dispose();
        var retry = harness.Boundary.ReceiveOne(
            harness.Source,
            harness.DecoderGeneration,
            PlaybackBoundedDemuxMediaKind.Audio,
            1,
            PlaybackSourceBoundOneFrameDecodeMode.PendingSubmissionRelief);

        Assert.Equal(PlaybackSourceBoundOneFrameDecodeResult.SubmissionAccepted, retry.Result);
        Assert.True(harness.PacketOperations.LastRetryWasFlush);
        Assert.True(harness.Boundary.Snapshot.AudioFlushAccepted);
        Assert.False(harness.Boundary.Snapshot.AudioEndOfStream);
        Assert.True(sourceCompletion.IsDisposed);
    }

    [Fact]
    public void Repeated_packet_backpressure_requires_one_frame_and_one_retry_per_step()
    {
        using var harness = new Harness();
        harness.PacketOperations.SubmitResults.Enqueue(
            PlaybackMetadataAwareNativePacketResult.NeedFrameDrain);
        harness.PacketOperations.RetryResults.Enqueue(
            PlaybackMetadataAwareNativePacketResult.NeedFrameDrain);
        harness.PacketOperations.RetryResults.Enqueue(
            PlaybackMetadataAwareNativePacketResult.Submitted);
        var sourceCompletion = CreatePacketCompletion(
            harness.Source,
            PlaybackBoundedDemuxMediaKind.Video);
        harness.Boundary.BeginSubmission(sourceCompletion);
        harness.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            CreateFrame(PlaybackBoundedDemuxMediaKind.Video, value: 1)));
        harness.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            CreateFrame(PlaybackBoundedDemuxMediaKind.Video, value: 2)));

        var firstFrame = ReceiveRelief(harness, PlaybackBoundedDemuxMediaKind.Video);
        firstFrame.Dispose();
        var firstRetry = harness.Boundary.ReceiveOne(
            harness.Source,
            harness.DecoderGeneration,
            PlaybackBoundedDemuxMediaKind.Video,
            0,
            PlaybackSourceBoundOneFrameDecodeMode.PendingSubmissionRelief);
        var secondFrame = ReceiveRelief(harness, PlaybackBoundedDemuxMediaKind.Video);
        secondFrame.Dispose();
        var secondRetry = harness.Boundary.ReceiveOne(
            harness.Source,
            harness.DecoderGeneration,
            PlaybackBoundedDemuxMediaKind.Video,
            0,
            PlaybackSourceBoundOneFrameDecodeMode.PendingSubmissionRelief);

        Assert.Equal(
            PlaybackSourceBoundOneFrameDecodeResult.SubmissionStillPending,
            firstRetry.Result);
        Assert.Equal(
            PlaybackSourceBoundOneFrameDecodeResult.SubmissionAccepted,
            secondRetry.Result);
        Assert.Equal(2, harness.ReceiveOperations.ReceiveCount);
        Assert.Equal(2, harness.PacketOperations.RetryCount);
        Assert.True(sourceCompletion.IsDisposed);
    }

    [Fact]
    public void Relief_need_more_input_is_terminal_no_progress()
    {
        using var harness = new Harness();
        harness.PacketOperations.SubmitResults.Enqueue(
            PlaybackMetadataAwareNativePacketResult.NeedFrameDrain);
        var sourceCompletion = CreatePacketCompletion(
            harness.Source,
            PlaybackBoundedDemuxMediaKind.Audio);
        harness.Boundary.BeginSubmission(sourceCompletion);
        harness.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.NeedMoreInput,
            null));

        var operation = harness.Boundary.ReceiveOne(
            harness.Source,
            harness.DecoderGeneration,
            PlaybackBoundedDemuxMediaKind.Audio,
            1,
            PlaybackSourceBoundOneFrameDecodeMode.PendingSubmissionRelief);

        Assert.Equal(PlaybackSourceBoundOneFrameDecodeResult.DecoderNoProgress, operation.Result);
        Assert.Equal(PlaybackSourceBoundOneFrameDecodeState.Faulted, harness.Boundary.Snapshot.State);
        Assert.True(sourceCompletion.IsDisposed);
        Assert.True(harness.NativeSession.IsDisposed);
        Assert.Equal(0, harness.PacketOperations.RetryCount);
    }

    [Fact]
    public void Relief_decoder_eof_completes_only_the_pending_stream()
    {
        using var harness = new Harness();
        harness.PacketOperations.SubmitResults.Enqueue(
            PlaybackMetadataAwareNativePacketResult.NeedFrameDrain);
        var sourceCompletion = CreatePacketCompletion(
            harness.Source,
            PlaybackBoundedDemuxMediaKind.Video);
        harness.Boundary.BeginSubmission(sourceCompletion);
        harness.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.EndOfStream,
            null));

        var operation = harness.Boundary.ReceiveOne(
            harness.Source,
            harness.DecoderGeneration,
            PlaybackBoundedDemuxMediaKind.Video,
            0,
            PlaybackSourceBoundOneFrameDecodeMode.PendingSubmissionRelief);

        Assert.Equal(PlaybackSourceBoundOneFrameDecodeResult.EndOfStream, operation.Result);
        Assert.True(harness.Boundary.Snapshot.VideoEndOfStream);
        Assert.False(harness.Boundary.Snapshot.AudioEndOfStream);
        Assert.True(sourceCompletion.IsDisposed);
        Assert.Equal(0, harness.PacketOperations.RetryCount);
    }

    [Fact]
    public void Accepted_flush_drains_video_and_audio_independently_one_frame_per_call()
    {
        using var harness = new Harness();
        Assert.Equal(
            PlaybackFiniteOneEventPacketSubmissionResult.FlushAccepted,
            harness.Boundary.BeginSubmission(CreateEndOfStreamCompletion(
                harness.Source,
                PlaybackBoundedDemuxMediaKind.Video)));
        Assert.Equal(
            PlaybackFiniteOneEventPacketSubmissionResult.FlushAccepted,
            harness.Boundary.BeginSubmission(CreateEndOfStreamCompletion(
                harness.Source,
                PlaybackBoundedDemuxMediaKind.Audio)));
        harness.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            CreateFrame(PlaybackBoundedDemuxMediaKind.Video)));
        harness.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.EndOfStream,
            null));
        harness.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            CreateFrame(PlaybackBoundedDemuxMediaKind.Audio)));
        harness.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.EndOfStream,
            null));

        var ordinaryAfterFlush = harness.Boundary.ReceiveOne(
            harness.Source,
            harness.DecoderGeneration,
            PlaybackBoundedDemuxMediaKind.Video,
            0,
            PlaybackSourceBoundOneFrameDecodeMode.Ordinary);
        var videoFrame = ReceiveDrain(harness, PlaybackBoundedDemuxMediaKind.Video);
        videoFrame.Dispose();
        var videoEof = harness.Boundary.ReceiveOne(
            harness.Source,
            harness.DecoderGeneration,
            PlaybackBoundedDemuxMediaKind.Video,
            0,
            PlaybackSourceBoundOneFrameDecodeMode.FlushDrain);
        var audioFrame = ReceiveDrain(harness, PlaybackBoundedDemuxMediaKind.Audio);
        audioFrame.Dispose();
        var audioEof = harness.Boundary.ReceiveOne(
            harness.Source,
            harness.DecoderGeneration,
            PlaybackBoundedDemuxMediaKind.Audio,
            1,
            PlaybackSourceBoundOneFrameDecodeMode.FlushDrain);

        Assert.Equal(
            PlaybackSourceBoundOneFrameDecodeResult.StreamRequiresDrain,
            ordinaryAfterFlush.Result);
        Assert.Equal(PlaybackSourceBoundOneFrameDecodeResult.EndOfStream, videoEof.Result);
        Assert.True(harness.Boundary.Snapshot.VideoEndOfStream);
        Assert.Equal(PlaybackSourceBoundOneFrameDecodeResult.EndOfStream, audioEof.Result);
        Assert.True(harness.Boundary.Snapshot.AudioEndOfStream);
        Assert.Equal(4, harness.ReceiveOperations.ReceiveCount);
    }

    [Fact]
    public void Flush_drain_need_more_input_fails_closed()
    {
        using var harness = new Harness();
        harness.Boundary.BeginSubmission(CreateEndOfStreamCompletion(
            harness.Source,
            PlaybackBoundedDemuxMediaKind.Video));
        harness.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.NeedMoreInput,
            null));

        var operation = harness.Boundary.ReceiveOne(
            harness.Source,
            harness.DecoderGeneration,
            PlaybackBoundedDemuxMediaKind.Video,
            0,
            PlaybackSourceBoundOneFrameDecodeMode.FlushDrain);

        Assert.Equal(PlaybackSourceBoundOneFrameDecodeResult.DecoderNoProgress, operation.Result);
        Assert.Equal(PlaybackSourceBoundOneFrameDecodeState.Faulted, harness.Boundary.Snapshot.State);
    }

    [Fact]
    public void Managed_byte_budget_failure_clears_copied_frame_without_sequence_commit()
    {
        using var harness = new Harness();
        var budget = new PlaybackSourceBoundOneFrameDecodeBudget(2, 3);
        var frame = CreateFrame(PlaybackBoundedDemuxMediaKind.Audio, 4);
        harness.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            frame));

        var operation = harness.Boundary.ReceiveOne(
            harness.Source,
            harness.DecoderGeneration,
            PlaybackBoundedDemuxMediaKind.Audio,
            1,
            PlaybackSourceBoundOneFrameDecodeMode.Ordinary,
            budget: budget);

        Assert.Equal(PlaybackSourceBoundOneFrameDecodeResult.WorkLimitExceeded, operation.Result);
        Assert.True(frame.IsDisposed);
        Assert.Equal(0, harness.Boundary.Snapshot.NextAudioSequence);
        Assert.Equal(2, budget.RemainingFrameCount);
        Assert.Equal(3, budget.RemainingManagedBytes);
    }

    [Fact]
    public void Dispose_clears_active_frame_before_pending_submission_and_native_contexts()
    {
        using var harness = new Harness();
        harness.PacketOperations.SubmitResults.Enqueue(
            PlaybackMetadataAwareNativePacketResult.NeedFrameDrain);
        var sourceCompletion = CreatePacketCompletion(
            harness.Source,
            PlaybackBoundedDemuxMediaKind.Video);
        harness.Boundary.BeginSubmission(sourceCompletion);
        var frame = CreateFrame(PlaybackBoundedDemuxMediaKind.Video);
        harness.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            frame));
        var completion = ReceiveRelief(harness, PlaybackBoundedDemuxMediaKind.Video);
        harness.Log.Clear();

        harness.Boundary.Dispose();
        harness.Boundary.Dispose();

        Assert.True(completion.IsDisposed);
        Assert.True(frame.IsDisposed);
        Assert.All(frame.Planes[0].Buffer.ToArray(), value => Assert.Equal(0, value));
        Assert.True(sourceCompletion.IsDisposed);
        Assert.Equal(["packet", "submission", "audio", "video"], harness.Log);
        Assert.True(harness.NativeSession.IsDisposed);
        Assert.True(harness.Boundary.IsDisposed);
    }

    private static PlaybackSourceBoundDecodedFrameCompletion ReceiveRelief(
        Harness harness,
        PlaybackBoundedDemuxMediaKind mediaKind)
    {
        var operation = harness.Boundary.ReceiveOne(
            harness.Source,
            harness.DecoderGeneration,
            mediaKind,
            StreamId(mediaKind),
            PlaybackSourceBoundOneFrameDecodeMode.PendingSubmissionRelief);
        Assert.Equal(PlaybackSourceBoundOneFrameDecodeResult.FrameAvailable, operation.Result);
        return Assert.IsType<PlaybackSourceBoundDecodedFrameCompletion>(operation.Completion);
    }

    private static PlaybackSourceBoundDecodedFrameCompletion ReceiveDrain(
        Harness harness,
        PlaybackBoundedDemuxMediaKind mediaKind)
    {
        var operation = harness.Boundary.ReceiveOne(
            harness.Source,
            harness.DecoderGeneration,
            mediaKind,
            StreamId(mediaKind),
            PlaybackSourceBoundOneFrameDecodeMode.FlushDrain);
        Assert.Equal(PlaybackSourceBoundOneFrameDecodeResult.FrameAvailable, operation.Result);
        return Assert.IsType<PlaybackSourceBoundDecodedFrameCompletion>(operation.Completion);
    }
}
