namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDiagnosticLateAwareSchedulerDispatchFailClosedTests
{
    [Fact]
    public async Task ExecuteAsync_rejects_wrong_media_kind_before_ingress()
    {
        var events = new List<string>();
        var misplacedAudio = CreateFrame(
            PlaybackDecoderFfmpegFrameMediaKind.Audio,
            TimeSpan.Zero);
        var lease = CreateLease(events);
        using var source = new PlaybackDiagnosticManagedFrameCopyTransactionCompletion(
            PlaybackDiagnosticManagedFrameCopyTransactionResult.Advanced,
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            misplacedAudio,
            PlaybackDecoderFfmpegManagedFrameCopyResult.NeedMoreInput,
            null,
            lease);
        using var scheduler = CreateScheduler();
        var dispatch = new RecordingDispatchOperations();
        var transaction = new PlaybackDiagnosticLateAwareSchedulerDispatchTransaction(
            new PlaybackDiagnosticManagedFrameSchedulerIngressTransaction(),
            dispatch);

        using var completion = await transaction.ExecuteAsync(
            source,
            scheduler,
            new PlaybackTimedSchedulerDispatchOptions());

        Assert.Equal(
            PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.InvalidCompletion,
            completion.Result);
        Assert.True(source.IsDisposed);
        Assert.True(misplacedAudio.IsDisposed);
        Assert.True(lease.IsDisposed);
        Assert.Equal(0, dispatch.CallCount);
        Assert.Equal(["dispose-audio", "dispose-video"], events);
    }

    [Fact]
    public async Task ExecuteAsync_faulted_ingress_retains_accepted_video_without_dispatch()
    {
        var events = new List<string>();
        var video = CreateFrame(
            PlaybackDecoderFfmpegFrameMediaKind.Video,
            TimeSpan.Zero);
        var audio = CreateFrame(
            PlaybackDecoderFfmpegFrameMediaKind.Audio,
            TimeSpan.FromMilliseconds(10));
        var lease = CreateLease(events);
        using var source = new PlaybackDiagnosticManagedFrameCopyTransactionCompletion(
            PlaybackDiagnosticManagedFrameCopyTransactionResult.Advanced,
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            video,
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            audio,
            lease);
        using var scheduler = CreateScheduler();
        var dispatch = new RecordingDispatchOperations();
        var ingress = new PlaybackDiagnosticManagedFrameSchedulerIngressTransaction(
            new FaultOnAudioIngressOperations());
        var transaction = new PlaybackDiagnosticLateAwareSchedulerDispatchTransaction(
            ingress,
            dispatch);

        var completion = await transaction.ExecuteAsync(
            source,
            scheduler,
            new PlaybackTimedSchedulerDispatchOptions());

        Assert.Equal(
            PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.Faulted,
            completion.Result);
        Assert.Equal(
            PlaybackDiagnosticManagedFrameSchedulerIngressTransactionResult.Faulted,
            completion.IngressResult);
        Assert.Equal(1, completion.AcceptedFrameCount);
        Assert.Equal(0, completion.ConsumedFrameCount);
        Assert.True(completion.OutstandingVideo);
        Assert.False(completion.OutstandingAudio);
        Assert.Same(lease, completion.Lease);
        Assert.Equal(0, dispatch.CallCount);
        Assert.False(video.IsDisposed);
        Assert.True(audio.IsDisposed);
        Assert.Empty(events);

        completion.Dispose();

        Assert.True(video.IsDisposed);
        Assert.True(lease.IsDisposed);
        Assert.Equal(["dispose-audio", "dispose-video"], events);
    }

    private static BoundedPlaybackScheduler CreateScheduler() =>
        new(
            new PlaybackSchedulerOptions(),
            new PlaybackPolicyClockBoundary(
                new FakeClock(),
                new PlaybackPolicyOptions()));

    private static PlaybackDiagnosticNativeDecoderLease CreateLease(List<string> events) =>
        new(
            new FakeContextOwner("video", events),
            new FakeContextOwner("audio", events));

    private static PlaybackDecoderFfmpegManagedFrame CreateFrame(
        PlaybackDecoderFfmpegFrameMediaKind kind,
        TimeSpan presentationTimestamp)
    {
        var timestamp = PlaybackDecoderFfmpegFrameTime.Known(presentationTimestamp);
        var metadata = kind == PlaybackDecoderFfmpegFrameMediaKind.Video
            ? new PlaybackDecoderFfmpegFrameMetadata(
                kind,
                PlaybackDecoderFfmpegFrameFormat.VideoBgra32,
                1,
                1,
                0,
                0,
                0,
                timestamp,
                timestamp,
                PlaybackDecoderFfmpegFrameTime.Unknown)
            : new PlaybackDecoderFfmpegFrameMetadata(
                kind,
                PlaybackDecoderFfmpegFrameFormat.AudioSigned16,
                0,
                0,
                48_000,
                2,
                1,
                timestamp,
                timestamp,
                PlaybackDecoderFfmpegFrameTime.Unknown);
        return new(
            metadata,
            [new PlaybackDecoderFfmpegManagedPlane(0, 4, 1, 4, new byte[4])]);
    }

    private sealed class FaultOnAudioIngressOperations :
        IPlaybackDiagnosticManagedFrameSchedulerIngressOperations
    {
        private int _callCount;

        public PlaybackSchedulerEnqueueResult Enqueue(
            BoundedPlaybackScheduler scheduler,
            PlaybackDecoderFfmpegManagedFrame frame,
            long sequence,
            PlaybackDecodedFrameDiscontinuityKind discontinuity)
        {
            _callCount++;
            if (_callCount == 2)
            {
                throw new InvalidOperationException("test");
            }

            return scheduler.Enqueue(frame, sequence, discontinuity);
        }
    }

    private sealed class RecordingDispatchOperations :
        IPlaybackDiagnosticLateAwareSchedulerDispatchOperations
    {
        public int CallCount { get; private set; }

        public ValueTask<PlaybackLateAwareTimedDispatchResult> DispatchOneAsync(
            BoundedPlaybackScheduler scheduler,
            PlaybackTimedSchedulerDispatchOptions options,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.FromResult(PlaybackLateAwareTimedDispatchResult.Empty);
        }
    }

    private sealed class FakeClock : IPlaybackMonotonicClock
    {
        public TimeSpan GetTimestamp() => TimeSpan.Zero;
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
