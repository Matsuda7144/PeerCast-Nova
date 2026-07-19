namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDiagnosticFrameMetadataTransactionTests
{
    [Fact]
    public void Execute_captures_video_metadata_and_accepts_audio_need_more_input()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var operations = new FakeOperations(events);
        var transaction = new PlaybackDiagnosticFrameMetadataTransaction(operations);

        var completion = transaction.Execute(lease, CreateInput());

        Assert.Equal(PlaybackDiagnosticFrameMetadataTransactionResult.Advanced, completion.Result);
        Assert.Equal(PlaybackDecoderFfmpegFrameMetadataResult.Extracted, completion.VideoState);
        Assert.Equal(PlaybackDecoderFfmpegFrameMediaKind.Video, completion.VideoMetadata?.MediaKind);
        Assert.Equal(PlaybackDecoderFfmpegFrameMetadataResult.NeedMoreInput, completion.AudioState);
        Assert.Null(completion.AudioMetadata);
        Assert.Same(lease, completion.Lease);
        Assert.Equal(
            ["send-video", "receive-video", "send-audio", "receive-audio"],
            events);

        completion.Lease!.Dispose();
        Assert.Equal("dispose-audio", events[^2]);
        Assert.Equal("dispose-video", events[^1]);
    }

    [Fact]
    public void Execute_rejects_extracted_without_metadata_and_disposes_lease()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var operations = new FakeOperations(events) { EmitVideoMetadata = false };
        var transaction = new PlaybackDiagnosticFrameMetadataTransaction(operations);

        var completion = transaction.Execute(lease, CreateInput());

        Assert.Equal(
            PlaybackDiagnosticFrameMetadataTransactionResult.VideoMetadataRejected,
            completion.Result);
        Assert.Null(completion.Lease);
        Assert.Equal(
            ["send-video", "receive-video", "dispose-audio", "dispose-video"],
            events);
    }

    [Fact]
    public void Execute_rejects_audio_metadata_failure_and_preserves_video_metadata_in_result()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var operations = new FakeOperations(events)
        {
            AudioState = PlaybackDecoderFfmpegFrameMetadataResult.InvalidMetadata,
        };
        var transaction = new PlaybackDiagnosticFrameMetadataTransaction(operations);

        var completion = transaction.Execute(lease, CreateInput());

        Assert.Equal(
            PlaybackDiagnosticFrameMetadataTransactionResult.AudioMetadataRejected,
            completion.Result);
        Assert.Equal(PlaybackDecoderFfmpegFrameMediaKind.Video, completion.VideoMetadata?.MediaKind);
        Assert.Null(completion.Lease);
        Assert.Equal("dispose-audio", events[^2]);
        Assert.Equal("dispose-video", events[^1]);
    }

    [Fact]
    public void Execute_fault_disposes_complete_lease()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var operations = new FakeOperations(events) { ThrowOnAudioReceive = true };
        var transaction = new PlaybackDiagnosticFrameMetadataTransaction(operations);

        var completion = transaction.Execute(lease, CreateInput());

        Assert.Equal(PlaybackDiagnosticFrameMetadataTransactionResult.Faulted, completion.Result);
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
        var transaction = new PlaybackDiagnosticFrameMetadataTransaction(
            new FakeOperations(events));

        var completion = transaction.Execute(lease, input);

        Assert.Equal(
            PlaybackDiagnosticFrameMetadataTransactionResult.InvalidConfiguration,
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
        IPlaybackDiagnosticFrameMetadataOperations
    {
        public bool EmitVideoMetadata { get; init; } = true;

        public PlaybackDecoderFfmpegFrameMetadataResult AudioState { get; init; } =
            PlaybackDecoderFfmpegFrameMetadataResult.NeedMoreInput;

        public bool ThrowOnAudioReceive { get; init; }

        public PlaybackDecoderFfmpegPacketSendResult SendVideo(
            PlaybackDiagnosticNativeDecoderLease lease,
            ReadOnlyMemory<byte> packet)
        {
            events.Add("send-video");
            return PlaybackDecoderFfmpegPacketSendResult.Sent;
        }

        public PlaybackDecoderFfmpegFrameMetadataResult ReceiveVideo(
            PlaybackDiagnosticNativeDecoderLease lease,
            IPlaybackDecoderFfmpegFrameMetadataConsumer consumer)
        {
            events.Add("receive-video");
            if (EmitVideoMetadata)
            {
                consumer.Consume(CreateVideoMetadata());
            }

            return PlaybackDecoderFfmpegFrameMetadataResult.Extracted;
        }

        public PlaybackDecoderFfmpegPacketSendResult SendAudio(
            PlaybackDiagnosticNativeDecoderLease lease,
            ReadOnlyMemory<byte> packet)
        {
            events.Add("send-audio");
            return PlaybackDecoderFfmpegPacketSendResult.Sent;
        }

        public PlaybackDecoderFfmpegFrameMetadataResult ReceiveAudio(
            PlaybackDiagnosticNativeDecoderLease lease,
            IPlaybackDecoderFfmpegFrameMetadataConsumer consumer)
        {
            events.Add("receive-audio");
            if (ThrowOnAudioReceive)
            {
                throw new InvalidOperationException("test");
            }

            return AudioState;
        }

        private static PlaybackDecoderFfmpegFrameMetadata CreateVideoMetadata() =>
            new(
                PlaybackDecoderFfmpegFrameMediaKind.Video,
                PlaybackDecoderFfmpegFrameFormat.VideoYuv420P,
                640,
                360,
                0,
                0,
                0,
                PlaybackDecoderFfmpegFrameTime.Unknown,
                PlaybackDecoderFfmpegFrameTime.Unknown,
                PlaybackDecoderFfmpegFrameTime.Unknown);
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
