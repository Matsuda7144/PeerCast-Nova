namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDiagnosticManagedFormatConversionTransactionTests
{
    [Fact]
    public void Execute_converts_video_then_accepts_audio_need_more_input()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var operations = new FakeOperations(events);
        var completion = new PlaybackDiagnosticManagedFormatConversionTransaction(operations)
            .Execute(lease, CreateInput());

        Assert.Equal(PlaybackDiagnosticManagedFormatConversionTransactionResult.Advanced, completion.Result);
        Assert.Equal(PlaybackDiagnosticConvertedOutputKind.Video, completion.VideoOutput?.Kind);
        Assert.Equal(0, completion.VideoOutput?.Sequence);
        Assert.Equal(PlaybackDecoderFfmpegManagedFrameCopyResult.NeedMoreInput, completion.AudioCopyState);
        Assert.Null(completion.AudioOutput);
        Assert.True(operations.VideoFrame!.IsDisposed);
        Assert.Equal(["send-video", "receive-video", "convert-video", "send-audio", "receive-audio"], events);

        completion.Dispose();
        Assert.True(lease.IsDisposed);
    }

    [Fact]
    public void Execute_converts_both_streams_and_keeps_only_summaries()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var operations = new FakeOperations(events) { CopyAudio = true };
        var completion = new PlaybackDiagnosticManagedFormatConversionTransaction(operations)
            .Execute(lease, CreateInput());

        Assert.Equal(PlaybackDiagnosticManagedFormatConversionTransactionResult.Advanced, completion.Result);
        Assert.Equal(PlaybackDiagnosticConvertedOutputKind.Video, completion.VideoOutput?.Kind);
        Assert.Equal(PlaybackDiagnosticConvertedOutputKind.Audio, completion.AudioOutput?.Kind);
        Assert.Equal(1, completion.AudioOutput?.Sequence);
        Assert.True(operations.VideoFrame!.IsDisposed);
        Assert.True(operations.AudioFrame!.IsDisposed);
        Assert.Same(lease, completion.Lease);
    }

    [Fact]
    public void Execute_rejects_conversion_state_without_matching_callback()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var operations = new FakeOperations(events) { WrongVideoCallback = true };
        var completion = new PlaybackDiagnosticManagedFormatConversionTransaction(operations)
            .Execute(lease, CreateInput());

        Assert.Equal(PlaybackDiagnosticManagedFormatConversionTransactionResult.VideoConversionRejected, completion.Result);
        Assert.Equal(PlaybackDiagnosticConvertedOutputKind.Audio, completion.VideoOutput?.Kind);
        Assert.True(operations.VideoFrame!.IsDisposed);
        Assert.True(lease.IsDisposed);
        Assert.Null(completion.Lease);
    }

    [Fact]
    public void Execute_fault_disposes_copied_frame_and_complete_lease()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var operations = new FakeOperations(events) { ThrowOnVideoConvert = true };
        var completion = new PlaybackDiagnosticManagedFormatConversionTransaction(operations)
            .Execute(lease, CreateInput());

        Assert.Equal(PlaybackDiagnosticManagedFormatConversionTransactionResult.Faulted, completion.Result);
        Assert.True(operations.VideoFrame!.IsDisposed);
        Assert.True(lease.IsDisposed);
        Assert.Null(completion.Lease);
    }

    [Fact]
    public void Execute_rejects_invalid_codecs_before_operations()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var original = CreateInput();
        var input = original with { Video = original.Audio, Audio = original.Video };
        var completion = new PlaybackDiagnosticManagedFormatConversionTransaction(new FakeOperations(events))
            .Execute(lease, input);

        Assert.Equal(PlaybackDiagnosticManagedFormatConversionTransactionResult.InvalidConfiguration, completion.Result);
        Assert.Equal(["dispose-audio", "dispose-video"], events);
    }

    private static PlaybackDiagnosticEncodedInput CreateInput() => new(
        new(BoundedDiagnosticPlaybackCodec.H264, 0, 1, new byte[] { 1 }),
        new(BoundedDiagnosticPlaybackCodec.Aac, 0, 1, new byte[] { 2 }));

    private static PlaybackDiagnosticNativeDecoderLease CreateLease(List<string> events) => new(
        new FakeContextOwner("video", events),
        new FakeContextOwner("audio", events));

    private sealed class FakeOperations(List<string> events) : IPlaybackDiagnosticManagedFormatConversionOperations
    {
        public bool CopyAudio { get; init; }
        public bool WrongVideoCallback { get; init; }
        public bool ThrowOnVideoConvert { get; init; }
        public PlaybackDecoderFfmpegManagedFrame? VideoFrame { get; private set; }
        public PlaybackDecoderFfmpegManagedFrame? AudioFrame { get; private set; }

        public PlaybackDecoderFfmpegPacketSendResult SendVideo(PlaybackDiagnosticNativeDecoderLease lease, ReadOnlyMemory<byte> packet)
        { events.Add("send-video"); return PlaybackDecoderFfmpegPacketSendResult.Sent; }

        public PlaybackDecoderFfmpegManagedFrameCopyResult ReceiveVideo(PlaybackDiagnosticNativeDecoderLease lease, IPlaybackDecoderFfmpegManagedFrameConsumer consumer)
        { events.Add("receive-video"); VideoFrame = CreateFrame(true); consumer.Consume(VideoFrame); return PlaybackDecoderFfmpegManagedFrameCopyResult.Copied; }

        public PlaybackDecoderFfmpegManagedFormatConversionResult ConvertVideo(PlaybackDiagnosticNativeDecoderLease lease, PlaybackDecoderFfmpegManagedFrame frame, IPlaybackDecoderFfmpegDecodedOutputConsumer consumer)
        {
            events.Add("convert-video");
            if (ThrowOnVideoConvert) throw new InvalidOperationException("test");
            if (WrongVideoCallback) consumer.ConsumeAudio(default); else consumer.ConsumeVideo(default);
            return PlaybackDecoderFfmpegManagedFormatConversionResult.Converted;
        }

        public PlaybackDecoderFfmpegPacketSendResult SendAudio(PlaybackDiagnosticNativeDecoderLease lease, ReadOnlyMemory<byte> packet)
        { events.Add("send-audio"); return PlaybackDecoderFfmpegPacketSendResult.Sent; }

        public PlaybackDecoderFfmpegManagedFrameCopyResult ReceiveAudio(PlaybackDiagnosticNativeDecoderLease lease, IPlaybackDecoderFfmpegManagedFrameConsumer consumer)
        {
            events.Add("receive-audio");
            if (!CopyAudio) return PlaybackDecoderFfmpegManagedFrameCopyResult.NeedMoreInput;
            AudioFrame = CreateFrame(false); consumer.Consume(AudioFrame); return PlaybackDecoderFfmpegManagedFrameCopyResult.Copied;
        }

        public PlaybackDecoderFfmpegManagedFormatConversionResult ConvertAudio(PlaybackDiagnosticNativeDecoderLease lease, PlaybackDecoderFfmpegManagedFrame frame, IPlaybackDecoderFfmpegDecodedOutputConsumer consumer)
        { events.Add("convert-audio"); consumer.ConsumeAudio(default); return PlaybackDecoderFfmpegManagedFormatConversionResult.Converted; }

        private static PlaybackDecoderFfmpegManagedFrame CreateFrame(bool video)
        {
            var metadata = video
                ? new PlaybackDecoderFfmpegFrameMetadata(PlaybackDecoderFfmpegFrameMediaKind.Video, PlaybackDecoderFfmpegFrameFormat.VideoYuv420P, 2, 2, 0, 0, 0, PlaybackDecoderFfmpegFrameTime.Unknown, PlaybackDecoderFfmpegFrameTime.Unknown, PlaybackDecoderFfmpegFrameTime.Unknown)
                : new PlaybackDecoderFfmpegFrameMetadata(PlaybackDecoderFfmpegFrameMediaKind.Audio, PlaybackDecoderFfmpegFrameFormat.AudioSigned16Planar, 0, 0, 48000, 2, 1, PlaybackDecoderFfmpegFrameTime.Unknown, PlaybackDecoderFfmpegFrameTime.Unknown, PlaybackDecoderFfmpegFrameTime.Unknown);
            var planes = video
                ? new[] { new PlaybackDecoderFfmpegManagedPlane(0, 2, 2, 2, new byte[4]), new PlaybackDecoderFfmpegManagedPlane(1, 1, 1, 1, new byte[1]), new PlaybackDecoderFfmpegManagedPlane(2, 1, 1, 1, new byte[1]) }
                : new[] { new PlaybackDecoderFfmpegManagedPlane(0, 2, 1, 2, new byte[2]), new PlaybackDecoderFfmpegManagedPlane(1, 2, 1, 2, new byte[2]) };
            return new PlaybackDecoderFfmpegManagedFrame(metadata, planes);
        }
    }

    private sealed class FakeContextOwner(string name, List<string> events) : IPlaybackDiagnosticNativeContextOwner
    {
        private bool _disposed;
        public PlaybackDecoderFfmpegCodecId CodecId => name == "video" ? PlaybackDecoderFfmpegCodecId.H264 : PlaybackDecoderFfmpegCodecId.Aac;
        public PlaybackDecoderFfmpegContextLease? NativeLease => null;
        public void Dispose() { if (_disposed) return; _disposed = true; events.Add($"dispose-{name}"); }
    }
}
