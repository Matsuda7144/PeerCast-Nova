namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDiagnosticManagedFrameCopyTransactionTests
{
    [Fact]
    public void Execute_retains_video_frame_and_accepts_audio_need_more_input()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var operations = new FakeOperations(events);
        var transaction = new PlaybackDiagnosticManagedFrameCopyTransaction(operations);

        var completion = transaction.Execute(lease, CreateInput());

        Assert.Equal(PlaybackDiagnosticManagedFrameCopyTransactionResult.Advanced, completion.Result);
        Assert.Equal(PlaybackDecoderFfmpegManagedFrameCopyResult.Copied, completion.VideoState);
        Assert.NotNull(completion.VideoFrame);
        Assert.Equal(PlaybackDecoderFfmpegFrameMediaKind.Video, completion.VideoFrame.Metadata.MediaKind);
        Assert.Equal(PlaybackDecoderFfmpegManagedFrameCopyResult.NeedMoreInput, completion.AudioState);
        Assert.Null(completion.AudioFrame);
        Assert.Same(lease, completion.Lease);
        Assert.Equal(
            ["send-video", "receive-video", "send-audio", "receive-audio"],
            events);
        var retainedVideo = completion.VideoFrame;

        completion.Dispose();

        Assert.NotNull(retainedVideo);
        Assert.True(retainedVideo.IsDisposed);
        Assert.Null(completion.VideoFrame);
        Assert.Equal("dispose-audio", events[^2]);
        Assert.Equal("dispose-video", events[^1]);
    }

    [Fact]
    public void Execute_retains_both_frames_until_completion_disposal()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var operations = new FakeOperations(events) { CopyAudio = true };
        var transaction = new PlaybackDiagnosticManagedFrameCopyTransaction(operations);

        var completion = transaction.Execute(lease, CreateInput());

        Assert.Equal(PlaybackDiagnosticManagedFrameCopyTransactionResult.Advanced, completion.Result);
        Assert.NotNull(completion.VideoFrame);
        Assert.NotNull(completion.AudioFrame);
        Assert.False(completion.VideoFrame.IsDisposed);
        Assert.False(completion.AudioFrame.IsDisposed);
        var retainedVideo = completion.VideoFrame;
        var retainedAudio = completion.AudioFrame;

        completion.Dispose();

        Assert.NotNull(retainedVideo);
        Assert.NotNull(retainedAudio);
        Assert.True(retainedVideo.IsDisposed);
        Assert.True(retainedAudio.IsDisposed);
        Assert.Null(completion.VideoFrame);
        Assert.Null(completion.AudioFrame);
        Assert.True(completion.IsDisposed);
    }

    [Fact]
    public void Execute_rejects_copied_without_frame_and_disposes_lease()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var operations = new FakeOperations(events) { EmitVideoFrame = false };
        var transaction = new PlaybackDiagnosticManagedFrameCopyTransaction(operations);

        var completion = transaction.Execute(lease, CreateInput());

        Assert.Equal(
            PlaybackDiagnosticManagedFrameCopyTransactionResult.VideoCopyRejected,
            completion.Result);
        Assert.Null(completion.VideoFrame);
        Assert.Null(completion.Lease);
        Assert.Equal(
            ["send-video", "receive-video", "dispose-audio", "dispose-video"],
            events);
    }

    [Fact]
    public void Execute_audio_rejection_disposes_already_copied_video_frame()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var operations = new FakeOperations(events)
        {
            AudioState = PlaybackDecoderFfmpegManagedFrameCopyResult.InsufficientData,
        };
        var transaction = new PlaybackDiagnosticManagedFrameCopyTransaction(operations);

        var completion = transaction.Execute(lease, CreateInput());

        Assert.Equal(
            PlaybackDiagnosticManagedFrameCopyTransactionResult.AudioCopyRejected,
            completion.Result);
        Assert.True(operations.VideoFrame!.IsDisposed);
        Assert.Null(completion.VideoFrame);
        Assert.Null(completion.Lease);
        Assert.Equal("dispose-audio", events[^2]);
        Assert.Equal("dispose-video", events[^1]);
    }

    [Fact]
    public void Execute_fault_disposes_captured_frames_and_complete_lease()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var operations = new FakeOperations(events) { ThrowOnAudioReceive = true };
        var transaction = new PlaybackDiagnosticManagedFrameCopyTransaction(operations);

        var completion = transaction.Execute(lease, CreateInput());

        Assert.Equal(PlaybackDiagnosticManagedFrameCopyTransactionResult.Faulted, completion.Result);
        Assert.True(operations.VideoFrame!.IsDisposed);
        Assert.Null(completion.Lease);
        Assert.Equal("dispose-audio", events[^2]);
        Assert.Equal("dispose-video", events[^1]);
    }

    [Fact]
    public void Execute_rejects_invalid_codecs_before_operations()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var original = CreateInput();
        var input = original with { Video = original.Audio, Audio = original.Video };
        var transaction = new PlaybackDiagnosticManagedFrameCopyTransaction(
            new FakeOperations(events));

        var completion = transaction.Execute(lease, input);

        Assert.Equal(
            PlaybackDiagnosticManagedFrameCopyTransactionResult.InvalidConfiguration,
            completion.Result);
        Assert.Equal(["dispose-audio", "dispose-video"], events);
    }

    private static PlaybackDiagnosticEncodedInput CreateInput() =>
        new(
            new(BoundedDiagnosticPlaybackCodec.H264, 0, 1, new byte[] { 1, 2, 3 }),
            new(BoundedDiagnosticPlaybackCodec.Aac, 0, 1, new byte[] { 4, 5 }));

    private static PlaybackDiagnosticNativeDecoderLease CreateLease(List<string> events) =>
        new(
            new FakeContextOwner("video", events),
            new FakeContextOwner("audio", events));

    private sealed class FakeOperations(List<string> events) :
        IPlaybackDiagnosticManagedFrameCopyOperations
    {
        public bool EmitVideoFrame { get; init; } = true;

        public bool CopyAudio { get; init; }

        public PlaybackDecoderFfmpegManagedFrameCopyResult AudioState { get; init; } =
            PlaybackDecoderFfmpegManagedFrameCopyResult.NeedMoreInput;

        public bool ThrowOnAudioReceive { get; init; }

        public PlaybackDecoderFfmpegManagedFrame? VideoFrame { get; private set; }

        public PlaybackDecoderFfmpegManagedFrame? AudioFrame { get; private set; }

        public PlaybackDecoderFfmpegPacketSendResult SendVideo(
            PlaybackDiagnosticNativeDecoderLease lease,
            ReadOnlyMemory<byte> packet)
        {
            events.Add("send-video");
            return PlaybackDecoderFfmpegPacketSendResult.Sent;
        }

        public PlaybackDecoderFfmpegManagedFrameCopyResult ReceiveVideo(
            PlaybackDiagnosticNativeDecoderLease lease,
            IPlaybackDecoderFfmpegManagedFrameConsumer consumer)
        {
            events.Add("receive-video");
            if (EmitVideoFrame)
            {
                VideoFrame = CreateFrame(PlaybackDecoderFfmpegFrameMediaKind.Video);
                consumer.Consume(VideoFrame);
            }

            return PlaybackDecoderFfmpegManagedFrameCopyResult.Copied;
        }

        public PlaybackDecoderFfmpegPacketSendResult SendAudio(
            PlaybackDiagnosticNativeDecoderLease lease,
            ReadOnlyMemory<byte> packet)
        {
            events.Add("send-audio");
            return PlaybackDecoderFfmpegPacketSendResult.Sent;
        }

        public PlaybackDecoderFfmpegManagedFrameCopyResult ReceiveAudio(
            PlaybackDiagnosticNativeDecoderLease lease,
            IPlaybackDecoderFfmpegManagedFrameConsumer consumer)
        {
            events.Add("receive-audio");
            if (ThrowOnAudioReceive)
            {
                throw new InvalidOperationException("test");
            }

            if (CopyAudio)
            {
                AudioFrame = CreateFrame(PlaybackDecoderFfmpegFrameMediaKind.Audio);
                consumer.Consume(AudioFrame);
                return PlaybackDecoderFfmpegManagedFrameCopyResult.Copied;
            }

            return AudioState;
        }

        private static PlaybackDecoderFfmpegManagedFrame CreateFrame(
            PlaybackDecoderFfmpegFrameMediaKind kind)
        {
            var metadata = kind == PlaybackDecoderFfmpegFrameMediaKind.Video
                ? new PlaybackDecoderFfmpegFrameMetadata(
                    kind,
                    PlaybackDecoderFfmpegFrameFormat.VideoBgra32,
                    1,
                    1,
                    0,
                    0,
                    0,
                    PlaybackDecoderFfmpegFrameTime.Unknown,
                    PlaybackDecoderFfmpegFrameTime.Unknown,
                    PlaybackDecoderFfmpegFrameTime.Unknown)
                : new PlaybackDecoderFfmpegFrameMetadata(
                    kind,
                    PlaybackDecoderFfmpegFrameFormat.AudioSigned16,
                    0,
                    0,
                    48000,
                    2,
                    1,
                    PlaybackDecoderFfmpegFrameTime.Unknown,
                    PlaybackDecoderFfmpegFrameTime.Unknown,
                    PlaybackDecoderFfmpegFrameTime.Unknown);
            var plane = new PlaybackDecoderFfmpegManagedPlane(0, 4, 1, 4, new byte[4]);
            return new PlaybackDecoderFfmpegManagedFrame(metadata, [plane]);
        }
    }

    private sealed class FakeContextOwner(string name, List<string> events) :
        IPlaybackDiagnosticNativeContextOwner
    {
        private bool _disposed;

        public PlaybackDecoderFfmpegCodecId CodecId =>
            name == "video" ? PlaybackDecoderFfmpegCodecId.H264 : PlaybackDecoderFfmpegCodecId.Aac;

        public PlaybackDecoderFfmpegContextLease? NativeLease => null;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            events.Add($"dispose-{name}");
        }
    }
}
