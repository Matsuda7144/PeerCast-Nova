using static PeerCast.Nova.Playback.Tests.PlaybackSourceBoundOneFrameDecodeTestSupport;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackSourceBoundOneFrameDecodeBoundaryTests
{
    [Fact]
    public void ReceiveOne_validates_exact_source_generation_stream_and_context_before_native()
    {
        using var harness = new Harness();
        using var wrongSource = CreateSource();

        var wrongSourceResult = harness.Boundary.ReceiveOne(
            wrongSource,
            harness.DecoderGeneration,
            PlaybackBoundedDemuxMediaKind.Video,
            0,
            PlaybackSourceBoundOneFrameDecodeMode.Ordinary);
        var wrongGenerationResult = harness.Boundary.ReceiveOne(
            harness.Source,
            harness.DecoderGeneration + 1,
            PlaybackBoundedDemuxMediaKind.Video,
            0,
            PlaybackSourceBoundOneFrameDecodeMode.Ordinary);
        var wrongStreamResult = harness.Boundary.ReceiveOne(
            harness.Source,
            harness.DecoderGeneration,
            PlaybackBoundedDemuxMediaKind.Video,
            1,
            PlaybackSourceBoundOneFrameDecodeMode.Ordinary);
        harness.ReceiveOperations.ContextRolesValid = false;
        var wrongRoleResult = harness.Boundary.ReceiveOne(
            harness.Source,
            harness.DecoderGeneration,
            PlaybackBoundedDemuxMediaKind.Video,
            0,
            PlaybackSourceBoundOneFrameDecodeMode.Ordinary);

        Assert.Equal(
            PlaybackSourceBoundOneFrameDecodeResult.SourceMismatch,
            wrongSourceResult.Result);
        Assert.Equal(
            PlaybackSourceBoundOneFrameDecodeResult.GenerationMismatch,
            wrongGenerationResult.Result);
        Assert.Equal(
            PlaybackSourceBoundOneFrameDecodeResult.StreamRejected,
            wrongStreamResult.Result);
        Assert.Equal(
            PlaybackSourceBoundOneFrameDecodeResult.StreamRejected,
            wrongRoleResult.Result);
        Assert.Equal(0, harness.ReceiveOperations.ReceiveCount);
    }

    [Fact]
    public void ReceiveOne_copies_exactly_one_frame_into_source_bound_completion()
    {
        using var harness = new Harness();
        var frame = CreateFrame(PlaybackBoundedDemuxMediaKind.Video);
        harness.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            frame));

        var operation = harness.Boundary.ReceiveOne(
            harness.Source,
            harness.DecoderGeneration,
            PlaybackBoundedDemuxMediaKind.Video,
            0,
            PlaybackSourceBoundOneFrameDecodeMode.Ordinary);
        var blocked = harness.Boundary.ReceiveOne(
            harness.Source,
            harness.DecoderGeneration,
            PlaybackBoundedDemuxMediaKind.Video,
            0,
            PlaybackSourceBoundOneFrameDecodeMode.Ordinary);
        var completion = Assert.IsType<PlaybackSourceBoundDecodedFrameCompletion>(
            operation.Completion);

        Assert.Equal(PlaybackSourceBoundOneFrameDecodeResult.FrameAvailable, operation.Result);
        Assert.Equal(PlaybackSourceBoundOneFrameDecodeResult.ActiveCompletion, blocked.Result);
        Assert.Same(frame, completion.Frame);
        Assert.True(completion.ReferencesSourceSession(harness.Source));
        Assert.Equal(harness.DecoderGeneration, completion.DecoderGeneration);
        Assert.Equal(harness.DiscontinuityGeneration, completion.DiscontinuityGeneration);
        Assert.Equal(PlaybackBoundedDemuxMediaKind.Video, completion.MediaKind);
        Assert.Equal(0, completion.StreamId);
        Assert.Equal(0, completion.Sequence);
        Assert.Equal(4, completion.ManagedByteCount);
        Assert.Equal(1, harness.ReceiveOperations.ReceiveCount);

        completion.Dispose();

        Assert.True(frame.IsDisposed);
        Assert.All(frame.Planes[0].Buffer.ToArray(), value => Assert.Equal(0, value));
        Assert.False(harness.Boundary.Snapshot.HasActiveDecodedCompletion);
    }

    [Fact]
    public void ReceiveOne_commits_independent_sequences_only_after_completion_construction()
    {
        using var harness = new Harness();
        harness.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            CreateFrame(PlaybackBoundedDemuxMediaKind.Video)));
        harness.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            CreateFrame(PlaybackBoundedDemuxMediaKind.Audio)));
        harness.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            CreateFrame(PlaybackBoundedDemuxMediaKind.Video)));

        var video0 = ReceiveFrame(harness, PlaybackBoundedDemuxMediaKind.Video);
        video0.Dispose();
        var audio0 = ReceiveFrame(harness, PlaybackBoundedDemuxMediaKind.Audio);
        audio0.Dispose();
        var video1 = ReceiveFrame(harness, PlaybackBoundedDemuxMediaKind.Video);

        Assert.Equal(0, video0.Sequence);
        Assert.Equal(0, audio0.Sequence);
        Assert.Equal(1, video1.Sequence);
        Assert.Equal(2, harness.Boundary.Snapshot.NextVideoSequence);
        Assert.Equal(1, harness.Boundary.Snapshot.NextAudioSequence);
        video1.Dispose();
    }

    [Fact]
    public void ReceiveOne_ordinary_need_more_input_is_normal_and_finite()
    {
        using var harness = new Harness();
        harness.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.NeedMoreInput,
            null));

        var operation = harness.Boundary.ReceiveOne(
            harness.Source,
            harness.DecoderGeneration,
            PlaybackBoundedDemuxMediaKind.Audio,
            1,
            PlaybackSourceBoundOneFrameDecodeMode.Ordinary);

        Assert.Equal(PlaybackSourceBoundOneFrameDecodeResult.NeedMoreInput, operation.Result);
        Assert.Equal(PlaybackSourceBoundOneFrameDecodeState.Accepting, harness.Boundary.Snapshot.State);
        Assert.Equal(1, harness.ReceiveOperations.ReceiveCount);
    }

    [Fact]
    public void ReceiveOne_multiple_frames_require_multiple_finite_operations()
    {
        using var harness = new Harness();
        harness.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            CreateFrame(PlaybackBoundedDemuxMediaKind.Video, value: 1)));
        harness.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            CreateFrame(PlaybackBoundedDemuxMediaKind.Video, value: 2)));

        var first = ReceiveFrame(harness, PlaybackBoundedDemuxMediaKind.Video);
        Assert.Equal(1, harness.ReceiveOperations.ReceiveCount);
        first.Dispose();
        var second = ReceiveFrame(harness, PlaybackBoundedDemuxMediaKind.Video);

        Assert.Equal(2, harness.ReceiveOperations.ReceiveCount);
        Assert.Equal(0, first.Sequence);
        Assert.Equal(1, second.Sequence);
        second.Dispose();
    }

    [Fact]
    public void ReceiveOne_enforces_reusable_frame_count_and_managed_byte_budget()
    {
        using var harness = new Harness();
        var budget = new PlaybackSourceBoundOneFrameDecodeBudget(1, 4);
        harness.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            CreateFrame(PlaybackBoundedDemuxMediaKind.Video, 4)));

        var first = harness.Boundary.ReceiveOne(
            harness.Source,
            harness.DecoderGeneration,
            PlaybackBoundedDemuxMediaKind.Video,
            0,
            PlaybackSourceBoundOneFrameDecodeMode.Ordinary,
            budget: budget);
        Assert.IsType<PlaybackSourceBoundDecodedFrameCompletion>(first.Completion).Dispose();
        var second = harness.Boundary.ReceiveOne(
            harness.Source,
            harness.DecoderGeneration,
            PlaybackBoundedDemuxMediaKind.Video,
            0,
            PlaybackSourceBoundOneFrameDecodeMode.Ordinary,
            budget: budget);

        Assert.Equal(PlaybackSourceBoundOneFrameDecodeResult.WorkLimitExceeded, second.Result);
        Assert.Equal(0, budget.RemainingFrameCount);
        Assert.Equal(0, budget.RemainingManagedBytes);
        Assert.Equal(1, harness.ReceiveOperations.ReceiveCount);
        Assert.Equal(PlaybackSourceBoundOneFrameDecodeState.Faulted, harness.Boundary.Snapshot.State);
    }

    [Fact]
    public void ReceiveOne_cancellation_before_and_after_copy_does_not_commit_sequence()
    {
        using var beforeHarness = new Harness();
        using var beforeCancellation = new CancellationTokenSource();
        beforeCancellation.Cancel();

        var before = beforeHarness.Boundary.ReceiveOne(
            beforeHarness.Source,
            beforeHarness.DecoderGeneration,
            PlaybackBoundedDemuxMediaKind.Video,
            0,
            PlaybackSourceBoundOneFrameDecodeMode.Ordinary,
            beforeCancellation.Token);

        using var afterHarness = new Harness();
        using var afterCancellation = new CancellationTokenSource();
        var frame = CreateFrame(PlaybackBoundedDemuxMediaKind.Audio);
        afterHarness.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            frame));
        afterHarness.ReceiveOperations.CancelDuringReceive = afterCancellation;
        var after = afterHarness.Boundary.ReceiveOne(
            afterHarness.Source,
            afterHarness.DecoderGeneration,
            PlaybackBoundedDemuxMediaKind.Audio,
            1,
            PlaybackSourceBoundOneFrameDecodeMode.Ordinary,
            afterCancellation.Token);

        Assert.Equal(PlaybackSourceBoundOneFrameDecodeResult.Cancelled, before.Result);
        Assert.Equal(0, beforeHarness.ReceiveOperations.ReceiveCount);
        Assert.Equal(PlaybackSourceBoundOneFrameDecodeResult.Cancelled, after.Result);
        Assert.True(frame.IsDisposed);
        Assert.Equal(0, afterHarness.Boundary.Snapshot.NextAudioSequence);
    }

    [Fact]
    public void ReceiveOne_rejects_contradictory_frame_and_undefined_native_result_fail_closed()
    {
        using var frameHarness = new Harness();
        var wrongFrame = CreateFrame(PlaybackBoundedDemuxMediaKind.Audio);
        frameHarness.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            wrongFrame));

        var wrongFrameResult = frameHarness.Boundary.ReceiveOne(
            frameHarness.Source,
            frameHarness.DecoderGeneration,
            PlaybackBoundedDemuxMediaKind.Video,
            0,
            PlaybackSourceBoundOneFrameDecodeMode.Ordinary);

        using var undefinedHarness = new Harness();
        undefinedHarness.ReceiveOperations.Results.Enqueue(new(
            (PlaybackDecoderFfmpegManagedFrameCopyResult)999,
            null));
        var undefinedResult = undefinedHarness.Boundary.ReceiveOne(
            undefinedHarness.Source,
            undefinedHarness.DecoderGeneration,
            PlaybackBoundedDemuxMediaKind.Video,
            0,
            PlaybackSourceBoundOneFrameDecodeMode.Ordinary);

        Assert.Equal(PlaybackSourceBoundOneFrameDecodeResult.FrameRejected, wrongFrameResult.Result);
        Assert.True(wrongFrame.IsDisposed);
        Assert.Equal(PlaybackSourceBoundOneFrameDecodeState.Faulted, frameHarness.Boundary.Snapshot.State);
        Assert.Equal(PlaybackSourceBoundOneFrameDecodeResult.NativeFailure, undefinedResult.Result);
        Assert.Equal(PlaybackSourceBoundOneFrameDecodeState.Faulted, undefinedHarness.Boundary.Snapshot.State);
    }

    [Fact]
    public void ReceiveOne_rejects_disposed_source_before_native_access()
    {
        using var harness = new Harness();
        harness.Source.Dispose();

        var operation = harness.Boundary.ReceiveOne(
            harness.Source,
            harness.DecoderGeneration,
            PlaybackBoundedDemuxMediaKind.Video,
            0,
            PlaybackSourceBoundOneFrameDecodeMode.Ordinary);

        Assert.Equal(PlaybackSourceBoundOneFrameDecodeResult.InvalidConfiguration, operation.Result);
        Assert.Equal(0, harness.ReceiveOperations.ReceiveCount);
    }

    private static PlaybackSourceBoundDecodedFrameCompletion ReceiveFrame(
        Harness harness,
        PlaybackBoundedDemuxMediaKind mediaKind)
    {
        var operation = harness.Boundary.ReceiveOne(
            harness.Source,
            harness.DecoderGeneration,
            mediaKind,
            StreamId(mediaKind),
            PlaybackSourceBoundOneFrameDecodeMode.Ordinary);
        Assert.Equal(PlaybackSourceBoundOneFrameDecodeResult.FrameAvailable, operation.Result);
        return Assert.IsType<PlaybackSourceBoundDecodedFrameCompletion>(operation.Completion);
    }
}
