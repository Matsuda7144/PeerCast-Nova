namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDiagnosticAwaitedSinkDeliveryTransactionTests
{
    [Fact]
    public async Task ExecuteAsync_delivers_video_then_audio_and_retains_lease()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var operations = new FakeOperations(events) { CopyAudio = true };
        var transaction = new PlaybackDiagnosticAwaitedSinkDeliveryTransaction(operations);
        var sink = new NoOpSink();

        var completion = await transaction.ExecuteAsync(
            lease,
            CreateInput(),
            sink,
            CancellationToken.None);

        Assert.Equal(
            PlaybackDiagnosticAwaitedSinkDeliveryTransactionResult.Advanced,
            completion.Result);
        Assert.Equal(PlaybackDecoderFfmpegManagedFrameCopyResult.Copied, completion.VideoCopyState);
        Assert.Equal(PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Delivered, completion.VideoDeliveryState);
        Assert.Equal(PlaybackDecoderFfmpegManagedFrameCopyResult.Copied, completion.AudioCopyState);
        Assert.Equal(PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Delivered, completion.AudioDeliveryState);
        Assert.Same(lease, completion.Lease);
        Assert.Same(sink, operations.VideoSink);
        Assert.Same(sink, operations.AudioSink);
        Assert.True(operations.VideoFrame!.IsDisposed);
        Assert.True(operations.AudioFrame!.IsDisposed);
        Assert.Equal(
            [
                "send-video",
                "receive-video",
                "deliver-video",
                "send-audio",
                "receive-audio",
                "deliver-audio",
            ],
            events);

        completion.Dispose();

        Assert.Equal("dispose-audio", events[^2]);
        Assert.Equal("dispose-video", events[^1]);
    }

    [Fact]
    public async Task ExecuteAsync_accepts_video_need_more_input_then_delivers_audio()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var operations = new FakeOperations(events)
        {
            EmitVideoFrame = false,
            VideoState = PlaybackDecoderFfmpegManagedFrameCopyResult.NeedMoreInput,
            CopyAudio = true,
        };
        var transaction = new PlaybackDiagnosticAwaitedSinkDeliveryTransaction(operations);

        var completion = await transaction.ExecuteAsync(
            lease,
            CreateInput(),
            new NoOpSink(),
            CancellationToken.None);

        Assert.Equal(
            PlaybackDiagnosticAwaitedSinkDeliveryTransactionResult.Advanced,
            completion.Result);
        Assert.Equal(PlaybackDecoderFfmpegManagedFrameCopyResult.NeedMoreInput, completion.VideoCopyState);
        Assert.Null(completion.VideoDeliveryState);
        Assert.Equal(PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Delivered, completion.AudioDeliveryState);
        Assert.Equal(
            ["send-video", "receive-video", "send-audio", "receive-audio", "deliver-audio"],
            events);

        completion.Dispose();
    }

    [Fact]
    public async Task ExecuteAsync_rejects_wrong_video_media_kind_before_delivery()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var operations = new FakeOperations(events)
        {
            VideoFrameKind = PlaybackDecoderFfmpegFrameMediaKind.Audio,
        };
        var transaction = new PlaybackDiagnosticAwaitedSinkDeliveryTransaction(operations);

        var completion = await transaction.ExecuteAsync(
            lease,
            CreateInput(),
            new NoOpSink(),
            CancellationToken.None);

        Assert.Equal(
            PlaybackDiagnosticAwaitedSinkDeliveryTransactionResult.VideoCopyRejected,
            completion.Result);
        Assert.True(operations.VideoFrame!.IsDisposed);
        Assert.Null(completion.Lease);
        Assert.Equal(
            ["send-video", "receive-video", "dispose-audio", "dispose-video"],
            events);
    }

    [Fact]
    public async Task ExecuteAsync_cancelled_video_delivery_disposes_frame_and_lease()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var operations = new FakeOperations(events)
        {
            VideoDeliveryResult = PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Cancelled,
        };
        var transaction = new PlaybackDiagnosticAwaitedSinkDeliveryTransaction(operations);

        var completion = await transaction.ExecuteAsync(
            lease,
            CreateInput(),
            new NoOpSink(),
            CancellationToken.None);

        Assert.Equal(
            PlaybackDiagnosticAwaitedSinkDeliveryTransactionResult.Cancelled,
            completion.Result);
        Assert.Equal(PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Cancelled, completion.VideoDeliveryState);
        Assert.True(operations.VideoFrame!.IsDisposed);
        Assert.Null(completion.Lease);
        Assert.Equal("dispose-audio", events[^2]);
        Assert.Equal("dispose-video", events[^1]);
    }

    [Fact]
    public async Task ExecuteAsync_audio_delivery_failure_disposes_audio_frame_and_lease()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var operations = new FakeOperations(events)
        {
            CopyAudio = true,
            AudioDeliveryResult = PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.SinkFailed,
        };
        var transaction = new PlaybackDiagnosticAwaitedSinkDeliveryTransaction(operations);

        var completion = await transaction.ExecuteAsync(
            lease,
            CreateInput(),
            new NoOpSink(),
            CancellationToken.None);

        Assert.Equal(
            PlaybackDiagnosticAwaitedSinkDeliveryTransactionResult.AudioDeliveryRejected,
            completion.Result);
        Assert.Equal(PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Delivered, completion.VideoDeliveryState);
        Assert.Equal(PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.SinkFailed, completion.AudioDeliveryState);
        Assert.True(operations.VideoFrame!.IsDisposed);
        Assert.True(operations.AudioFrame!.IsDisposed);
        Assert.Null(completion.Lease);
        Assert.Equal("dispose-audio", events[^2]);
        Assert.Equal("dispose-video", events[^1]);
    }

    [Fact]
    public async Task ExecuteAsync_pre_cancelled_token_skips_operations()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var operations = new FakeOperations(events);
        var transaction = new PlaybackDiagnosticAwaitedSinkDeliveryTransaction(operations);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var completion = await transaction.ExecuteAsync(
            lease,
            CreateInput(),
            new NoOpSink(),
            cancellation.Token);

        Assert.Equal(
            PlaybackDiagnosticAwaitedSinkDeliveryTransactionResult.Cancelled,
            completion.Result);
        Assert.Equal(["dispose-audio", "dispose-video"], events);
        Assert.Null(completion.Lease);
    }

    [Fact]
    public async Task ExecuteAsync_fault_disposes_captured_frame_and_complete_lease()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var operations = new FakeOperations(events) { ThrowOnVideoDelivery = true };
        var transaction = new PlaybackDiagnosticAwaitedSinkDeliveryTransaction(operations);

        var completion = await transaction.ExecuteAsync(
            lease,
            CreateInput(),
            new NoOpSink(),
            CancellationToken.None);

        Assert.Equal(
            PlaybackDiagnosticAwaitedSinkDeliveryTransactionResult.Faulted,
            completion.Result);
        Assert.True(operations.VideoFrame!.IsDisposed);
        Assert.Null(completion.Lease);
        Assert.Equal("dispose-audio", events[^2]);
        Assert.Equal("dispose-video", events[^1]);
    }

    [Fact]
    public async Task ExecuteAsync_invalid_codecs_are_rejected_before_operations()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var original = CreateInput();
        var input = original with { Video = original.Audio, Audio = original.Video };
        var transaction = new PlaybackDiagnosticAwaitedSinkDeliveryTransaction(
            new FakeOperations(events));

        var completion = await transaction.ExecuteAsync(
            lease,
            input,
            new NoOpSink(),
            CancellationToken.None);

        Assert.Equal(
            PlaybackDiagnosticAwaitedSinkDeliveryTransactionResult.InvalidConfiguration,
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
        IPlaybackDiagnosticAwaitedSinkDeliveryOperations
    {
        public bool EmitVideoFrame { get; init; } = true;

        public PlaybackDecoderFfmpegManagedFrameCopyResult VideoState { get; init; } =
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied;

        public bool CopyAudio { get; init; }

        public PlaybackDecoderFfmpegFrameMediaKind VideoFrameKind { get; init; } =
            PlaybackDecoderFfmpegFrameMediaKind.Video;

        public PlaybackDecoderFfmpegAwaitedSinkDeliveryResult VideoDeliveryResult { get; init; } =
            PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Delivered;

        public PlaybackDecoderFfmpegAwaitedSinkDeliveryResult AudioDeliveryResult { get; init; } =
            PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Delivered;

        public bool ThrowOnVideoDelivery { get; init; }

        public PlaybackDecoderFfmpegManagedFrame? VideoFrame { get; private set; }

        public PlaybackDecoderFfmpegManagedFrame? AudioFrame { get; private set; }

        public IPlaybackDecodedFrameSink? VideoSink { get; private set; }

        public IPlaybackDecodedFrameSink? AudioSink { get; private set; }

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
                VideoFrame = CreateFrame(VideoFrameKind);
                consumer.Consume(VideoFrame);
            }

            return VideoState;
        }

        public ValueTask<PlaybackDecoderFfmpegAwaitedSinkDeliveryResult> DeliverVideoAsync(
            PlaybackDiagnosticNativeDecoderLease lease,
            PlaybackDecoderFfmpegManagedFrame frame,
            IPlaybackDecodedFrameSink sink,
            CancellationToken cancellationToken)
        {
            events.Add("deliver-video");
            VideoSink = sink;
            if (ThrowOnVideoDelivery)
            {
                throw new InvalidOperationException("test");
            }

            return ValueTask.FromResult(VideoDeliveryResult);
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
            if (!CopyAudio)
            {
                return PlaybackDecoderFfmpegManagedFrameCopyResult.NeedMoreInput;
            }

            AudioFrame = CreateFrame(PlaybackDecoderFfmpegFrameMediaKind.Audio);
            consumer.Consume(AudioFrame);
            return PlaybackDecoderFfmpegManagedFrameCopyResult.Copied;
        }

        public ValueTask<PlaybackDecoderFfmpegAwaitedSinkDeliveryResult> DeliverAudioAsync(
            PlaybackDiagnosticNativeDecoderLease lease,
            PlaybackDecoderFfmpegManagedFrame frame,
            IPlaybackDecodedFrameSink sink,
            CancellationToken cancellationToken)
        {
            events.Add("deliver-audio");
            AudioSink = sink;
            return ValueTask.FromResult(AudioDeliveryResult);
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
                    48_000,
                    2,
                    1,
                    PlaybackDecoderFfmpegFrameTime.Unknown,
                    PlaybackDecoderFfmpegFrameTime.Unknown,
                    PlaybackDecoderFfmpegFrameTime.Unknown);
            return new(
                metadata,
                [new PlaybackDecoderFfmpegManagedPlane(0, 4, 1, 4, new byte[4])]);
        }
    }

    private sealed class NoOpSink : IPlaybackDecodedFrameSink
    {
        public ValueTask WriteAudioAsync(
            PlaybackDecodedAudioFrame frame,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask WriteVideoAsync(
            PlaybackDecodedVideoFrame frame,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
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
