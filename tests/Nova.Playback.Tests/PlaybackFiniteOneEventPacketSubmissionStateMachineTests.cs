namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFiniteOneEventPacketSubmissionStateMachineTests
{
    private static readonly PlaybackBoundedDemuxTimeBase TimeBase = new(1, 1_000);

    [Fact]
    public void Begin_accepts_one_packet_and_releases_parser_completion()
    {
        using var source = CreateSource();
        using var session = CreateSession(source);
        var operations = new FakeOperations();
        using var machine = new PlaybackFiniteOneEventPacketSubmissionStateMachine(operations);
        var completion = CreateOwnedPacketCompletion(source, PlaybackBoundedDemuxMediaKind.Video, 0);

        var result = machine.Begin(session, source, completion);

        Assert.Equal(PlaybackFiniteOneEventPacketSubmissionResult.PacketAccepted, result);
        Assert.True(completion.IsDisposed);
        Assert.False(machine.HasActiveSubmission);
        Assert.Equal(1, operations.SubmitCount);
        Assert.Equal(0, operations.ReceiveCount);
        Assert.Equal(0, operations.RetryCount);
    }

    [Fact]
    public void Begin_retains_packet_and_source_backpressure_on_again()
    {
        using var source = CreateSource();
        using var session = CreateSession(source);
        var operations = new FakeOperations
        {
            SubmitResult = PlaybackMetadataAwareNativePacketResult.NeedFrameDrain,
        };
        using var machine = new PlaybackFiniteOneEventPacketSubmissionStateMachine(operations);
        var completion = CreateOwnedPacketCompletion(source, PlaybackBoundedDemuxMediaKind.Video, 0);

        var result = machine.Begin(session, source, completion);
        var blocked = source.CreateReadCompletion(
            PlaybackBoundedDemuxResult.PacketsAvailable,
            [CreatePacketInput(PlaybackBoundedDemuxMediaKind.Audio, 1)],
            3,
            0);

        Assert.Equal(PlaybackFiniteOneEventPacketSubmissionResult.ReceiveRequired, result);
        Assert.True(machine.HasActiveSubmission);
        Assert.False(completion.IsDisposed);
        Assert.Equal(PlaybackBoundedDemuxResult.ActiveCompletion, blocked.Result);
        Assert.Equal(1, operations.SubmitCount);
        Assert.Equal(0, operations.OwnerDisposeCount);
    }

    [Fact]
    public void Advance_retries_once_after_frame_progress_and_accepts()
    {
        using var source = CreateSource();
        using var session = CreateSession(source);
        var operations = new FakeOperations
        {
            SubmitResult = PlaybackMetadataAwareNativePacketResult.NeedFrameDrain,
        };
        operations.ReceiveResults.Enqueue(PlaybackDecoderFfmpegFrameReceiveResult.FrameAvailable);
        operations.RetryResults.Enqueue(PlaybackMetadataAwareNativePacketResult.Submitted);
        using var machine = new PlaybackFiniteOneEventPacketSubmissionStateMachine(operations);
        var completion = CreateOwnedPacketCompletion(source, PlaybackBoundedDemuxMediaKind.Video, 0);
        Assert.Equal(
            PlaybackFiniteOneEventPacketSubmissionResult.ReceiveRequired,
            machine.Begin(session, source, completion));

        var result = machine.Advance();
        var next = CreateOwnedPacketCompletion(source, PlaybackBoundedDemuxMediaKind.Audio, 0);

        Assert.Equal(
            PlaybackFiniteOneEventPacketSubmissionResult.FrameProgressAndAccepted,
            result);
        Assert.False(machine.HasActiveSubmission);
        Assert.True(completion.IsDisposed);
        Assert.False(next.IsDisposed);
        Assert.Equal(1, operations.ReceiveCount);
        Assert.Equal(1, operations.RetryCount);
        Assert.Equal(1, operations.OwnerDisposeCount);
        next.Dispose();
    }

    [Fact]
    public void Advance_retains_when_retry_still_needs_frame_progress()
    {
        using var source = CreateSource();
        using var session = CreateSession(source);
        var operations = new FakeOperations
        {
            SubmitResult = PlaybackMetadataAwareNativePacketResult.NeedFrameDrain,
        };
        operations.ReceiveResults.Enqueue(PlaybackDecoderFfmpegFrameReceiveResult.FrameAvailable);
        operations.ReceiveResults.Enqueue(PlaybackDecoderFfmpegFrameReceiveResult.FrameAvailable);
        operations.RetryResults.Enqueue(PlaybackMetadataAwareNativePacketResult.NeedFrameDrain);
        operations.RetryResults.Enqueue(PlaybackMetadataAwareNativePacketResult.Submitted);
        using var machine = new PlaybackFiniteOneEventPacketSubmissionStateMachine(operations);
        var completion = CreateOwnedPacketCompletion(source, PlaybackBoundedDemuxMediaKind.Video, 0);
        machine.Begin(session, source, completion);

        var first = machine.Advance();
        var second = machine.Advance();

        Assert.Equal(
            PlaybackFiniteOneEventPacketSubmissionResult.FrameProgressAndStillPending,
            first);
        Assert.Equal(
            PlaybackFiniteOneEventPacketSubmissionResult.FrameProgressAndAccepted,
            second);
        Assert.Equal(2, operations.ReceiveCount);
        Assert.Equal(2, operations.RetryCount);
        Assert.True(completion.IsDisposed);
    }

    [Fact]
    public void Advance_does_not_retry_without_receive_progress()
    {
        using var source = CreateSource();
        using var session = CreateSession(source);
        var operations = new FakeOperations
        {
            SubmitResult = PlaybackMetadataAwareNativePacketResult.NeedFrameDrain,
        };
        operations.ReceiveResults.Enqueue(PlaybackDecoderFfmpegFrameReceiveResult.NeedMoreInput);
        using var machine = new PlaybackFiniteOneEventPacketSubmissionStateMachine(operations);
        var completion = CreateOwnedPacketCompletion(source, PlaybackBoundedDemuxMediaKind.Video, 0);
        machine.Begin(session, source, completion);

        var result = machine.Advance();

        Assert.Equal(PlaybackFiniteOneEventPacketSubmissionResult.NoReceiveProgress, result);
        Assert.True(machine.HasActiveSubmission);
        Assert.False(completion.IsDisposed);
        Assert.Equal(0, operations.RetryCount);
    }

    [Fact]
    public void Accepted_flush_blocks_later_packets_for_that_stream()
    {
        using var source = CreateSource();
        using var session = CreateSession(source);
        var operations = new FakeOperations();
        using var machine = new PlaybackFiniteOneEventPacketSubmissionStateMachine(operations);
        var eos = CreateOwnedEndOfStreamCompletion(
            source,
            PlaybackBoundedDemuxMediaKind.Video,
            0);

        var flushResult = machine.Begin(session, source, eos);
        var packet = CreateDetachedPacketCompletion(
            PlaybackBoundedDemuxMediaKind.Video,
            1);
        var packetResult = machine.Begin(session, source, packet);

        Assert.Equal(PlaybackFiniteOneEventPacketSubmissionResult.FlushAccepted, flushResult);
        Assert.Equal(
            PlaybackFiniteOneEventPacketSubmissionResult.StreamAlreadyFlushed,
            packetResult);
        Assert.True(machine.VideoFlushAccepted);
        Assert.True(packet.IsDisposed);
        Assert.Equal(1, operations.SubmitCount);
    }

    [Fact]
    public void Pending_flush_retries_after_frame_progress_and_marks_flush_accepted()
    {
        using var source = CreateSource();
        using var session = CreateSession(source);
        var operations = new FakeOperations
        {
            SubmitResult = PlaybackMetadataAwareNativePacketResult.NeedFrameDrain,
        };
        operations.ReceiveResults.Enqueue(PlaybackDecoderFfmpegFrameReceiveResult.FrameAvailable);
        operations.RetryResults.Enqueue(PlaybackMetadataAwareNativePacketResult.Submitted);
        using var machine = new PlaybackFiniteOneEventPacketSubmissionStateMachine(operations);
        var completion = CreateOwnedEndOfStreamCompletion(
            source,
            PlaybackBoundedDemuxMediaKind.Audio,
            0);
        machine.Begin(session, source, completion);

        var result = machine.Advance();

        Assert.Equal(
            PlaybackFiniteOneEventPacketSubmissionResult.FrameProgressAndAccepted,
            result);
        Assert.True(machine.AudioFlushAccepted);
        Assert.True(completion.IsDisposed);
        Assert.True(operations.LastRetryWasFlush);
    }

    [Fact]
    public void Decoder_eof_completes_pending_flush_and_marks_stream_end()
    {
        using var source = CreateSource();
        using var session = CreateSession(source);
        var operations = new FakeOperations
        {
            SubmitResult = PlaybackMetadataAwareNativePacketResult.NeedFrameDrain,
        };
        operations.ReceiveResults.Enqueue(PlaybackDecoderFfmpegFrameReceiveResult.EndOfStream);
        using var machine = new PlaybackFiniteOneEventPacketSubmissionStateMachine(operations);
        var completion = CreateOwnedEndOfStreamCompletion(
            source,
            PlaybackBoundedDemuxMediaKind.Video,
            0);
        machine.Begin(session, source, completion);

        var result = machine.Advance();

        Assert.Equal(PlaybackFiniteOneEventPacketSubmissionResult.StreamEndOfStream, result);
        Assert.True(machine.VideoFlushAccepted);
        Assert.True(machine.VideoEndOfStream);
        Assert.True(completion.IsDisposed);
        Assert.Equal(0, operations.RetryCount);
    }

    [Fact]
    public void Decoder_eof_terminates_pending_packet_without_committing_flush()
    {
        using var source = CreateSource();
        using var session = CreateSession(source);
        var operations = new FakeOperations
        {
            SubmitResult = PlaybackMetadataAwareNativePacketResult.NeedFrameDrain,
        };
        operations.ReceiveResults.Enqueue(PlaybackDecoderFfmpegFrameReceiveResult.EndOfStream);
        using var machine = new PlaybackFiniteOneEventPacketSubmissionStateMachine(operations);
        var completion = CreateOwnedPacketCompletion(source, PlaybackBoundedDemuxMediaKind.Audio, 0);
        machine.Begin(session, source, completion);

        var result = machine.Advance();

        Assert.Equal(PlaybackFiniteOneEventPacketSubmissionResult.StreamEndOfStream, result);
        Assert.False(machine.AudioFlushAccepted);
        Assert.True(machine.AudioEndOfStream);
        Assert.True(completion.IsDisposed);
    }

    [Fact]
    public void Begin_rejects_source_mismatch_before_native_submission()
    {
        using var source = CreateSource();
        using var wrongSource = CreateSource();
        using var session = CreateSession(source);
        var operations = new FakeOperations();
        using var machine = new PlaybackFiniteOneEventPacketSubmissionStateMachine(operations);
        var completion = CreateDetachedPacketCompletion(
            PlaybackBoundedDemuxMediaKind.Video,
            0);

        var result = machine.Begin(session, wrongSource, completion);

        Assert.Equal(PlaybackFiniteOneEventPacketSubmissionResult.SourceMismatch, result);
        Assert.True(completion.IsDisposed);
        Assert.Equal(0, operations.SubmitCount);
    }

    [Fact]
    public void Begin_rejects_non_increasing_sequence_after_acceptance()
    {
        using var source = CreateSource();
        using var session = CreateSession(source);
        var operations = new FakeOperations();
        using var machine = new PlaybackFiniteOneEventPacketSubmissionStateMachine(operations);
        var first = CreateDetachedPacketCompletion(
            PlaybackBoundedDemuxMediaKind.Video,
            5);
        var duplicate = CreateDetachedPacketCompletion(
            PlaybackBoundedDemuxMediaKind.Video,
            5);

        Assert.Equal(
            PlaybackFiniteOneEventPacketSubmissionResult.PacketAccepted,
            machine.Begin(session, source, first));
        var result = machine.Begin(session, source, duplicate);

        Assert.Equal(PlaybackFiniteOneEventPacketSubmissionResult.SequenceRejected, result);
        Assert.True(duplicate.IsDisposed);
        Assert.Equal(1, operations.SubmitCount);
    }

    [Fact]
    public void Begin_rejects_new_input_while_submission_is_active()
    {
        using var source = CreateSource();
        using var session = CreateSession(source);
        var operations = new FakeOperations
        {
            SubmitResult = PlaybackMetadataAwareNativePacketResult.NeedFrameDrain,
        };
        using var machine = new PlaybackFiniteOneEventPacketSubmissionStateMachine(operations);
        var completion = CreateOwnedPacketCompletion(source, PlaybackBoundedDemuxMediaKind.Video, 0);
        machine.Begin(session, source, completion);

        var result = machine.Begin(session, source, completion);

        Assert.Equal(PlaybackFiniteOneEventPacketSubmissionResult.ActiveSubmission, result);
        Assert.False(completion.IsDisposed);
        Assert.Equal(1, operations.SubmitCount);
    }

    [Fact]
    public void Advance_cancellation_releases_pending_ownership()
    {
        using var source = CreateSource();
        using var session = CreateSession(source);
        var operations = new FakeOperations
        {
            SubmitResult = PlaybackMetadataAwareNativePacketResult.NeedFrameDrain,
        };
        using var machine = new PlaybackFiniteOneEventPacketSubmissionStateMachine(operations);
        var completion = CreateOwnedPacketCompletion(source, PlaybackBoundedDemuxMediaKind.Video, 0);
        machine.Begin(session, source, completion);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = machine.Advance(cancellation.Token);

        Assert.Equal(PlaybackFiniteOneEventPacketSubmissionResult.Cancelled, result);
        Assert.True(completion.IsDisposed);
        Assert.False(machine.HasActiveSubmission);
        Assert.Equal(1, operations.OwnerDisposeCount);
    }

    [Fact]
    public void Native_receive_failure_releases_pending_ownership()
    {
        using var source = CreateSource();
        using var session = CreateSession(source);
        var operations = new FakeOperations
        {
            SubmitResult = PlaybackMetadataAwareNativePacketResult.NeedFrameDrain,
        };
        operations.ReceiveResults.Enqueue(PlaybackDecoderFfmpegFrameReceiveResult.NativeFailure);
        using var machine = new PlaybackFiniteOneEventPacketSubmissionStateMachine(operations);
        var completion = CreateOwnedPacketCompletion(source, PlaybackBoundedDemuxMediaKind.Video, 0);
        machine.Begin(session, source, completion);

        var result = machine.Advance();

        Assert.Equal(PlaybackFiniteOneEventPacketSubmissionResult.NativeFailure, result);
        Assert.True(completion.IsDisposed);
        Assert.False(machine.HasActiveSubmission);
    }

    [Fact]
    public void Dispose_is_idempotent_and_releases_pending_packet_before_source()
    {
        using var source = CreateSource();
        using var session = CreateSession(source);
        var operations = new FakeOperations
        {
            SubmitResult = PlaybackMetadataAwareNativePacketResult.NeedFrameDrain,
        };
        var machine = new PlaybackFiniteOneEventPacketSubmissionStateMachine(operations);
        var completion = CreateOwnedPacketCompletion(source, PlaybackBoundedDemuxMediaKind.Video, 0);
        machine.Begin(session, source, completion);

        machine.Dispose();
        machine.Dispose();

        Assert.True(machine.IsDisposed);
        Assert.True(completion.IsDisposed);
        Assert.Equal(1, operations.OwnerDisposeCount);
        Assert.Equal(1, operations.ReleaseCount);
    }

    private static PlaybackBoundedDemuxSession CreateSource()
    {
        var description = new PlaybackBoundedDemuxSessionDescriptionInput(
            PlaybackBoundedDemuxContainerKind.Flv,
            new PlaybackBoundedDemuxStreamDescriptionInput(
                0,
                PlaybackBoundedDemuxMediaKind.Video,
                PlaybackBoundedDemuxCodec.H264,
                1280,
                720,
                0,
                0,
                TimeBase,
                new byte[] { 1, 100, 0, 31 }),
            new PlaybackBoundedDemuxStreamDescriptionInput(
                1,
                PlaybackBoundedDemuxMediaKind.Audio,
                PlaybackBoundedDemuxCodec.Aac,
                0,
                0,
                48_000,
                2,
                TimeBase,
                new byte[] { 0x11, 0x90 }));
        Assert.Equal(
            PlaybackBoundedDemuxResult.StreamDescriptionAvailable,
            PlaybackBoundedDemuxSession.TryCreate(
                new PlaybackBoundedDemuxOptions(),
                description,
                out var session));
        return Assert.IsType<PlaybackBoundedDemuxSession>(session);
    }

    private static PlaybackBoundedFlvNativeDecoderSession CreateSession(
        PlaybackBoundedDemuxSession source) =>
        new(
            source,
            0,
            1,
            TimeBase,
            TimeBase,
            new FakeContextOwner(PlaybackDecoderFfmpegCodecId.H264),
            new FakeContextOwner(PlaybackDecoderFfmpegCodecId.Aac));

    private static PlaybackBoundedDemuxReadCompletion CreateOwnedPacketCompletion(
        PlaybackBoundedDemuxSession source,
        PlaybackBoundedDemuxMediaKind mediaKind,
        long sequence)
    {
        var completion = source.CreateReadCompletion(
            PlaybackBoundedDemuxResult.PacketsAvailable,
            [CreatePacketInput(mediaKind, sequence)],
            3,
            0);
        Assert.Equal(PlaybackBoundedDemuxResult.PacketsAvailable, completion.Result);
        Assert.Single(completion.Events);
        return completion;
    }

    private static PlaybackBoundedDemuxReadCompletion CreateOwnedEndOfStreamCompletion(
        PlaybackBoundedDemuxSession source,
        PlaybackBoundedDemuxMediaKind mediaKind,
        long sequence)
    {
        var completion = source.CreateReadCompletion(
            PlaybackBoundedDemuxResult.PacketsAvailable,
            [CreateEndOfStreamInput(mediaKind, sequence)],
            0,
            0);
        Assert.Equal(PlaybackBoundedDemuxResult.PacketsAvailable, completion.Result);
        Assert.Single(completion.Events);
        return completion;
    }

    private static PlaybackBoundedDemuxReadCompletion CreateDetachedPacketCompletion(
        PlaybackBoundedDemuxMediaKind mediaKind,
        long sequence) =>
        CreateDetachedCompletion(
            PlaybackBoundedDemuxResult.PacketsAvailable,
            CreatePacketInput(mediaKind, sequence));

    private static PlaybackBoundedDemuxReadCompletion CreateDetachedCompletion(
        PlaybackBoundedDemuxResult result,
        PlaybackBoundedDemuxEventInput input) =>
        new(
            result,
            [new PlaybackBoundedDemuxEvent(
                input,
                input.Payload.IsEmpty ? null : input.Payload.ToArray())],
            input.Payload.Length,
            0);

    private static PlaybackBoundedDemuxEventInput CreatePacketInput(
        PlaybackBoundedDemuxMediaKind mediaKind,
        long sequence) =>
        new(
            PlaybackBoundedDemuxEventKind.Packet,
            mediaKind == PlaybackBoundedDemuxMediaKind.Video ? 0 : 1,
            mediaKind,
            sequence,
            new byte[] { 1, 2, 3 },
            PlaybackBoundedDemuxTime.Known(TimeSpan.FromMilliseconds(120)),
            PlaybackBoundedDemuxTime.Known(TimeSpan.FromMilliseconds(100)),
            PlaybackBoundedDemuxTime.Known(TimeSpan.FromMilliseconds(40)),
            mediaKind == PlaybackBoundedDemuxMediaKind.Video
                ? PlaybackBoundedDemuxKeyframeState.Keyframe
                : PlaybackBoundedDemuxKeyframeState.NotApplicable,
            PlaybackDecodedFrameDiscontinuityKind.None);

    private static PlaybackBoundedDemuxEventInput CreateEndOfStreamInput(
        PlaybackBoundedDemuxMediaKind mediaKind,
        long sequence) =>
        new(
            PlaybackBoundedDemuxEventKind.EndOfStream,
            mediaKind == PlaybackBoundedDemuxMediaKind.Video ? 0 : 1,
            mediaKind,
            sequence,
            ReadOnlyMemory<byte>.Empty,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxKeyframeState.NotApplicable,
            PlaybackDecodedFrameDiscontinuityKind.None);

    private sealed class FakeContextOwner(PlaybackDecoderFfmpegCodecId codecId) :
        IPlaybackBoundedFlvNativeContextOwner
    {
        public PlaybackDecoderFfmpegCodecId CodecId { get; } = codecId;

        public PlaybackDecoderFfmpegContextLease? Context => null;

        public void Dispose()
        {
        }
    }

    private sealed class FakePacketOwner(FakeOperations operations) :
        IPlaybackMetadataAwareNativePacketOwner
    {
        private int _isDisposed;

        public PlaybackMetadataAwareNativeSendResult SendOnce(
            PlaybackBoundedFlvNativeDecoderSession session,
            PlaybackBoundedDemuxMediaKind mediaKind) =>
            PlaybackMetadataAwareNativeSendResult.Rejected;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                operations.OwnerDisposeCount++;
            }
        }
    }

    private sealed class FakeOperations : IPlaybackFiniteOneEventPacketSubmissionOperations
    {
        public PlaybackMetadataAwareNativePacketResult SubmitResult { get; init; } =
            PlaybackMetadataAwareNativePacketResult.Submitted;

        public Queue<PlaybackDecoderFfmpegFrameReceiveResult> ReceiveResults { get; } = new();

        public Queue<PlaybackMetadataAwareNativePacketResult> RetryResults { get; } = new();

        public int SubmitCount { get; private set; }

        public int ReceiveCount { get; private set; }

        public int RetryCount { get; private set; }

        public int ReleaseCount { get; private set; }

        public int OwnerDisposeCount { get; set; }

        public bool LastRetryWasFlush { get; private set; }

        public PlaybackMetadataAwareNativePacketCompletion SubmitOne(
            PlaybackBoundedFlvNativeDecoderSession nativeSession,
            PlaybackBoundedDemuxSession sourceSession,
            PlaybackBoundedDemuxReadCompletion sourceCompletion)
        {
            SubmitCount++;
            var demuxEvent = sourceCompletion.Events[0];
            return new(
                SubmitResult,
                SubmitResult == PlaybackMetadataAwareNativePacketResult.NeedFrameDrain &&
                    demuxEvent.Kind == PlaybackBoundedDemuxEventKind.Packet
                    ? new FakePacketOwner(this)
                    : null,
                sourceCompletion);
        }

        public PlaybackDecoderFfmpegFrameReceiveResult ReceiveOne(
            PlaybackBoundedFlvNativeDecoderSession nativeSession,
            PlaybackBoundedDemuxMediaKind mediaKind)
        {
            ReceiveCount++;
            return ReceiveResults.Count == 0
                ? PlaybackDecoderFfmpegFrameReceiveResult.NeedMoreInput
                : ReceiveResults.Dequeue();
        }

        public PlaybackMetadataAwareNativePacketResult RetryOne(
            PlaybackBoundedFlvNativeDecoderSession nativeSession,
            PlaybackBoundedDemuxMediaKind mediaKind,
            bool isFlush)
        {
            RetryCount++;
            LastRetryWasFlush = isFlush;
            return RetryResults.Count == 0
                ? PlaybackMetadataAwareNativePacketResult.NativeRejected
                : RetryResults.Dequeue();
        }

        public void ReleasePending() => ReleaseCount++;
    }
}
