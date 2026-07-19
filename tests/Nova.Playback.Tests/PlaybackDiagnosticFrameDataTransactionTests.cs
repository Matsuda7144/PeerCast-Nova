namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDiagnosticFrameDataTransactionTests
{
    [Fact]
    public void Execute_summarizes_video_views_and_accepts_audio_need_more_input()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var transaction = new PlaybackDiagnosticFrameDataTransaction(new FakeOperations(events));

        var completion = transaction.Execute(lease, CreateInput());

        Assert.Equal(PlaybackDiagnosticFrameDataTransactionResult.Advanced, completion.Result);
        Assert.Equal(2, completion.VideoSummary?.ViewCount);
        Assert.Equal(150, completion.VideoSummary?.ReferencedBytes);
        Assert.Null(completion.AudioSummary);
        Assert.Same(lease, completion.Lease);
        Assert.Equal(["send-video", "receive-video", "send-audio", "receive-audio"], events);

        completion.Lease!.Dispose();
        Assert.Equal("dispose-audio", events[^2]);
        Assert.Equal("dispose-video", events[^1]);
    }

    [Fact]
    public void Execute_rejects_processed_without_summary_and_disposes_lease()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var transaction = new PlaybackDiagnosticFrameDataTransaction(
            new FakeOperations(events) { EmitVideoViews = false });

        var completion = transaction.Execute(lease, CreateInput());

        Assert.Equal(PlaybackDiagnosticFrameDataTransactionResult.VideoDataRejected, completion.Result);
        Assert.Null(completion.Lease);
        Assert.Equal(["send-video", "receive-video", "dispose-audio", "dispose-video"], events);
    }

    [Fact]
    public void Execute_rejects_audio_failure_and_preserves_video_summary()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var transaction = new PlaybackDiagnosticFrameDataTransaction(
            new FakeOperations(events)
            {
                AudioState = PlaybackDecoderFfmpegFrameDataResult.InvalidData,
            });

        var completion = transaction.Execute(lease, CreateInput());

        Assert.Equal(PlaybackDiagnosticFrameDataTransactionResult.AudioDataRejected, completion.Result);
        Assert.Equal(2, completion.VideoSummary?.ViewCount);
        Assert.Null(completion.Lease);
        Assert.Equal("dispose-audio", events[^2]);
        Assert.Equal("dispose-video", events[^1]);
    }

    [Fact]
    public void Execute_fault_disposes_complete_lease()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var transaction = new PlaybackDiagnosticFrameDataTransaction(
            new FakeOperations(events) { ThrowOnAudioReceive = true });

        var completion = transaction.Execute(lease, CreateInput());

        Assert.Equal(PlaybackDiagnosticFrameDataTransactionResult.Faulted, completion.Result);
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
        var transaction = new PlaybackDiagnosticFrameDataTransaction(new FakeOperations(events));

        var completion = transaction.Execute(lease, input);

        Assert.Equal(PlaybackDiagnosticFrameDataTransactionResult.InvalidConfiguration, completion.Result);
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
        IPlaybackDiagnosticFrameDataOperations
    {
        public bool EmitVideoViews { get; init; } = true;

        public PlaybackDecoderFfmpegFrameDataResult AudioState { get; init; } =
            PlaybackDecoderFfmpegFrameDataResult.NeedMoreInput;

        public bool ThrowOnAudioReceive { get; init; }

        public PlaybackDecoderFfmpegPacketSendResult SendVideo(
            PlaybackDiagnosticNativeDecoderLease lease,
            ReadOnlyMemory<byte> packet)
        {
            events.Add("send-video");
            return PlaybackDecoderFfmpegPacketSendResult.Sent;
        }

        public PlaybackDecoderFfmpegFrameDataResult ReceiveVideo(
            PlaybackDiagnosticNativeDecoderLease lease,
            IPlaybackDecoderFfmpegFrameDataConsumer consumer)
        {
            events.Add("receive-video");
            if (EmitVideoViews)
            {
                var metadata = CreateVideoMetadata();
                consumer.Consume(
                    metadata,
                    new PlaybackDecoderFfmpegFrameDataView(
                        1,
                        PlaybackDecoderFfmpegFrameDataKind.VideoPlane,
                        0,
                        100,
                        10,
                        10));
                consumer.Consume(
                    metadata,
                    new PlaybackDecoderFfmpegFrameDataView(
                        1,
                        PlaybackDecoderFfmpegFrameDataKind.VideoPlane,
                        1,
                        50,
                        10,
                        5));
            }

            return PlaybackDecoderFfmpegFrameDataResult.Processed;
        }

        public PlaybackDecoderFfmpegPacketSendResult SendAudio(
            PlaybackDiagnosticNativeDecoderLease lease,
            ReadOnlyMemory<byte> packet)
        {
            events.Add("send-audio");
            return PlaybackDecoderFfmpegPacketSendResult.Sent;
        }

        public PlaybackDecoderFfmpegFrameDataResult ReceiveAudio(
            PlaybackDiagnosticNativeDecoderLease lease,
            IPlaybackDecoderFfmpegFrameDataConsumer consumer)
        {
            events.Add("receive-audio");
            if (ThrowOnAudioReceive)
            {
                throw new InvalidOperationException("test");
            }

            return AudioState;
        }
    }

    private static PlaybackDecoderFfmpegFrameMetadata CreateVideoMetadata() =>
        new(
            PlaybackDecoderFfmpegFrameMediaKind.Video,
            PlaybackDecoderFfmpegFrameFormat.VideoYuv420P,
            10,
            10,
            0,
            0,
            0,
            PlaybackDecoderFfmpegFrameTime.Unknown,
            PlaybackDecoderFfmpegFrameTime.Unknown,
            PlaybackDecoderFfmpegFrameTime.Unknown);

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
