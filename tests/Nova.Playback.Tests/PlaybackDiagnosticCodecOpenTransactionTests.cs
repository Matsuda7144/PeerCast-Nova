namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDiagnosticCodecOpenTransactionTests
{
    [Fact]
    public void Execute_opens_video_then_audio_and_retains_lease()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var operations = new FakeOperations(events);
        var transaction = new PlaybackDiagnosticCodecOpenTransaction(operations);

        var completion = transaction.Execute(lease);

        Assert.Equal(PlaybackDiagnosticCodecOpenTransactionResult.Opened, completion.Result);
        Assert.Same(lease, completion.Lease);
        Assert.Equal(["open-video", "open-audio"], events);
        Assert.False(lease.IsDisposed);

        completion.Lease!.Dispose();
        Assert.Equal(
            ["open-video", "open-audio", "dispose-audio", "dispose-video"],
            events);
    }

    [Fact]
    public void Execute_video_rejection_stops_and_disposes_audio_then_video()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var operations = new FakeOperations(events)
        {
            VideoResult = PlaybackDecoderFfmpegCodecOpenResult.NativeOpenRejected,
        };
        var transaction = new PlaybackDiagnosticCodecOpenTransaction(operations);

        var completion = transaction.Execute(lease);

        Assert.Equal(
            PlaybackDiagnosticCodecOpenTransactionResult.VideoOpenRejected,
            completion.Result);
        Assert.Null(completion.Lease);
        Assert.Equal(
            ["open-video", "dispose-audio", "dispose-video"],
            events);
        Assert.True(lease.IsDisposed);
    }

    [Fact]
    public void Execute_audio_rejection_disposes_audio_then_video()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var operations = new FakeOperations(events)
        {
            AudioResult = PlaybackDecoderFfmpegCodecOpenResult.NativeOpenRejected,
        };
        var transaction = new PlaybackDiagnosticCodecOpenTransaction(operations);

        var completion = transaction.Execute(lease);

        Assert.Equal(
            PlaybackDiagnosticCodecOpenTransactionResult.AudioOpenRejected,
            completion.Result);
        Assert.Null(completion.Lease);
        Assert.Equal(
            ["open-video", "open-audio", "dispose-audio", "dispose-video"],
            events);
        Assert.True(lease.IsDisposed);
    }

    [Fact]
    public void Execute_fault_disposes_complete_lease()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        var operations = new FakeOperations(events) { ThrowOnAudio = true };
        var transaction = new PlaybackDiagnosticCodecOpenTransaction(operations);

        var completion = transaction.Execute(lease);

        Assert.Equal(PlaybackDiagnosticCodecOpenTransactionResult.Faulted, completion.Result);
        Assert.Null(completion.Lease);
        Assert.Equal(
            ["open-video", "open-audio", "dispose-audio", "dispose-video"],
            events);
    }

    [Fact]
    public void Execute_rejects_null_without_operations()
    {
        var events = new List<string>();
        var transaction = new PlaybackDiagnosticCodecOpenTransaction(
            new FakeOperations(events));

        var completion = transaction.Execute(null);

        Assert.Equal(
            PlaybackDiagnosticCodecOpenTransactionResult.InvalidConfiguration,
            completion.Result);
        Assert.Null(completion.Lease);
        Assert.Empty(events);
    }

    private static PlaybackDiagnosticNativeDecoderLease CreateLease(List<string> events) =>
        new(
            new FakeContextOwner("video", events),
            new FakeContextOwner("audio", events));

    private sealed class FakeOperations(List<string> events) :
        IPlaybackDiagnosticCodecOpenOperations
    {
        public PlaybackDecoderFfmpegCodecOpenResult VideoResult { get; init; } =
            PlaybackDecoderFfmpegCodecOpenResult.Opened;

        public PlaybackDecoderFfmpegCodecOpenResult AudioResult { get; init; } =
            PlaybackDecoderFfmpegCodecOpenResult.Opened;

        public bool ThrowOnAudio { get; init; }

        public PlaybackDecoderFfmpegCodecOpenResult OpenVideo(
            PlaybackDiagnosticNativeDecoderLease lease)
        {
            events.Add("open-video");
            return VideoResult;
        }

        public PlaybackDecoderFfmpegCodecOpenResult OpenAudio(
            PlaybackDiagnosticNativeDecoderLease lease)
        {
            events.Add("open-audio");
            if (ThrowOnAudio)
            {
                throw new InvalidOperationException("test");
            }

            return AudioResult;
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
