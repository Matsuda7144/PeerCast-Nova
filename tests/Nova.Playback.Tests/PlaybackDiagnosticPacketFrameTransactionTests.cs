namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDiagnosticPacketFrameTransactionTests
{
    [Fact]
    public void Execute_advances_video_then_audio_and_retains_lease()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var operations = new FakeOperations(events);
        var transaction = new PlaybackDiagnosticPacketFrameTransaction(operations);

        var completion = transaction.Execute(lease, CreateInput());

        Assert.Equal(PlaybackDiagnosticPacketFrameTransactionResult.Advanced, completion.Result);
        Assert.Equal(
            PlaybackDecoderFfmpegFrameReceiveResult.FrameAvailable,
            completion.VideoFrameState);
        Assert.Equal(
            PlaybackDecoderFfmpegFrameReceiveResult.NeedMoreInput,
            completion.AudioFrameState);
        Assert.Same(lease, completion.Lease);
        Assert.Equal(
            ["send-video", "receive-video", "send-audio", "receive-audio"],
            events);

        completion.Lease!.Dispose();
        Assert.Equal(
            [
                "send-video",
                "receive-video",
                "send-audio",
                "receive-audio",
                "dispose-audio",
                "dispose-video",
            ],
            events);
    }

    [Fact]
    public void Execute_video_receive_rejection_stops_and_disposes_complete_lease()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var operations = new FakeOperations(events)
        {
            VideoReceiveResult = PlaybackDecoderFfmpegFrameReceiveResult.NativeRejected,
        };
        var transaction = new PlaybackDiagnosticPacketFrameTransaction(operations);

        var completion = transaction.Execute(lease, CreateInput());

        Assert.Equal(
            PlaybackDiagnosticPacketFrameTransactionResult.VideoReceiveRejected,
            completion.Result);
        Assert.Equal(
            PlaybackDecoderFfmpegFrameReceiveResult.NativeRejected,
            completion.VideoFrameState);
        Assert.Null(completion.Lease);
        Assert.Equal(
            ["send-video", "receive-video", "dispose-audio", "dispose-video"],
            events);
    }

    [Fact]
    public void Execute_audio_send_rejection_disposes_complete_lease()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var operations = new FakeOperations(events)
        {
            AudioSendResult = PlaybackDecoderFfmpegPacketSendResult.NeedFrameDrain,
        };
        var transaction = new PlaybackDiagnosticPacketFrameTransaction(operations);

        var completion = transaction.Execute(lease, CreateInput());

        Assert.Equal(
            PlaybackDiagnosticPacketFrameTransactionResult.AudioSendRejected,
            completion.Result);
        Assert.Null(completion.Lease);
        Assert.Equal(
            [
                "send-video",
                "receive-video",
                "send-audio",
                "dispose-audio",
                "dispose-video",
            ],
            events);
    }

    [Fact]
    public void Execute_fault_disposes_complete_lease()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var operations = new FakeOperations(events) { ThrowOnAudioReceive = true };
        var transaction = new PlaybackDiagnosticPacketFrameTransaction(operations);

        var completion = transaction.Execute(lease, CreateInput());

        Assert.Equal(PlaybackDiagnosticPacketFrameTransactionResult.Faulted, completion.Result);
        Assert.Null(completion.Lease);
        Assert.Equal(
            [
                "send-video",
                "receive-video",
                "send-audio",
                "receive-audio",
                "dispose-audio",
                "dispose-video",
            ],
            events);
    }

    [Fact]
    public void Execute_rejects_mismatched_codecs_without_operations()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var input = CreateInput() with
        {
            Video = CreateInput().Audio,
            Audio = CreateInput().Video,
        };
        var transaction = new PlaybackDiagnosticPacketFrameTransaction(
            new FakeOperations(events));

        var completion = transaction.Execute(lease, input);

        Assert.Equal(
            PlaybackDiagnosticPacketFrameTransactionResult.InvalidConfiguration,
            completion.Result);
        Assert.Null(completion.Lease);
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
        IPlaybackDiagnosticPacketFrameOperations
    {
        public PlaybackDecoderFfmpegPacketSendResult VideoSendResult { get; init; } =
            PlaybackDecoderFfmpegPacketSendResult.Sent;

        public PlaybackDecoderFfmpegFrameReceiveResult VideoReceiveResult { get; init; } =
            PlaybackDecoderFfmpegFrameReceiveResult.FrameAvailable;

        public PlaybackDecoderFfmpegPacketSendResult AudioSendResult { get; init; } =
            PlaybackDecoderFfmpegPacketSendResult.Sent;

        public PlaybackDecoderFfmpegFrameReceiveResult AudioReceiveResult { get; init; } =
            PlaybackDecoderFfmpegFrameReceiveResult.NeedMoreInput;

        public bool ThrowOnAudioReceive { get; init; }

        public PlaybackDecoderFfmpegPacketSendResult SendVideo(
            PlaybackDiagnosticNativeDecoderLease lease,
            ReadOnlyMemory<byte> packet)
        {
            events.Add("send-video");
            return VideoSendResult;
        }

        public PlaybackDecoderFfmpegFrameReceiveResult ReceiveVideo(
            PlaybackDiagnosticNativeDecoderLease lease)
        {
            events.Add("receive-video");
            return VideoReceiveResult;
        }

        public PlaybackDecoderFfmpegPacketSendResult SendAudio(
            PlaybackDiagnosticNativeDecoderLease lease,
            ReadOnlyMemory<byte> packet)
        {
            events.Add("send-audio");
            return AudioSendResult;
        }

        public PlaybackDecoderFfmpegFrameReceiveResult ReceiveAudio(
            PlaybackDiagnosticNativeDecoderLease lease)
        {
            events.Add("receive-audio");
            if (ThrowOnAudioReceive)
            {
                throw new InvalidOperationException("test");
            }

            return AudioReceiveResult;
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
