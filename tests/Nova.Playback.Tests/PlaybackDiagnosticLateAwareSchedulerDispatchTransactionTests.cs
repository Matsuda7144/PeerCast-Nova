namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDiagnosticLateAwareSchedulerDispatchTransactionTests
{
    [Fact]
    public async Task ExecuteAsync_consumes_video_then_audio_and_releases_lease()
    {
        var events = new List<string>();
        var video = CreateFrame(PlaybackDecoderFfmpegFrameMediaKind.Video, TimeSpan.Zero);
        var audio = CreateFrame(PlaybackDecoderFfmpegFrameMediaKind.Audio, TimeSpan.FromMilliseconds(10));
        var lease = CreateLease(events);
        using var source = CreateSource(video, audio, lease);
        using var scheduler = CreateScheduler();
        var dispatch = new FakeDispatchOperations(
            PlaybackLateAwareTimedDispatchResult.VideoProcessed,
            PlaybackLateAwareTimedDispatchResult.AudioProcessed);
        var transaction = CreateTransaction(dispatch);

        using var completion = await transaction.ExecuteAsync(
            source,
            scheduler,
            new PlaybackTimedSchedulerDispatchOptions());

        Assert.Equal(
            PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.Advanced,
            completion.Result);
        Assert.Equal(
            PlaybackDiagnosticManagedFrameSchedulerIngressTransactionResult.Advanced,
            completion.IngressResult);
        Assert.Equal(2, completion.AcceptedFrameCount);
        Assert.Equal(2, completion.ConsumedFrameCount);
        Assert.False(completion.OutstandingVideo);
        Assert.False(completion.OutstandingAudio);
        Assert.Null(completion.Lease);
        Assert.True(video.IsDisposed);
        Assert.True(audio.IsDisposed);
        Assert.Equal(2, dispatch.CallCount);
        Assert.Equal(0, scheduler.GetSnapshot().QueueLength);
        Assert.Equal(0, scheduler.GetSnapshot().OwnedByteCount);
        Assert.Equal(["dispose-audio", "dispose-video"], events);
    }

    [Fact]
    public async Task ExecuteAsync_accepts_video_drop_as_consumed()
    {
        var events = new List<string>();
        var video = CreateFrame(PlaybackDecoderFfmpegFrameMediaKind.Video, TimeSpan.Zero);
        var audio = CreateFrame(PlaybackDecoderFfmpegFrameMediaKind.Audio, TimeSpan.FromMilliseconds(10));
        using var source = CreateSource(video, audio, CreateLease(events));
        using var scheduler = CreateScheduler();
        var dispatch = new FakeDispatchOperations(
            PlaybackLateAwareTimedDispatchResult.VideoDropConsumed,
            PlaybackLateAwareTimedDispatchResult.AudioProcessed);

        using var completion = await CreateTransaction(dispatch).ExecuteAsync(
            source,
            scheduler,
            new PlaybackTimedSchedulerDispatchOptions());

        Assert.Equal(
            PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.Advanced,
            completion.Result);
        Assert.Equal(2, completion.ConsumedFrameCount);
        Assert.Equal(
            PlaybackLateAwareTimedDispatchResult.AudioProcessed,
            completion.LastDispatchResult);
        Assert.True(video.IsDisposed);
        Assert.True(audio.IsDisposed);
    }

    [Fact]
    public async Task ExecuteAsync_rejects_unknown_timestamp_before_ingress()
    {
        var events = new List<string>();
        var video = CreateFrame(PlaybackDecoderFfmpegFrameMediaKind.Video, null);
        var audio = CreateFrame(PlaybackDecoderFfmpegFrameMediaKind.Audio, TimeSpan.Zero);
        using var source = CreateSource(video, audio, CreateLease(events));
        using var scheduler = CreateScheduler();
        var dispatch = new FakeDispatchOperations();

        using var completion = await CreateTransaction(dispatch).ExecuteAsync(
            source,
            scheduler,
            new PlaybackTimedSchedulerDispatchOptions());

        Assert.Equal(
            PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.InvalidCompletion,
            completion.Result);
        Assert.True(source.IsDisposed);
        Assert.True(video.IsDisposed);
        Assert.True(audio.IsDisposed);
        Assert.Equal(0, dispatch.CallCount);
        Assert.Equal(0, scheduler.GetSnapshot().QueueLength);
        Assert.Equal(["dispose-audio", "dispose-video"], events);
    }

    [Fact]
    public async Task ExecuteAsync_rejects_nonfresh_scheduler_and_preserves_existing_item()
    {
        var events = new List<string>();
        var existing = CreateFrame(PlaybackDecoderFfmpegFrameMediaKind.Video, TimeSpan.Zero);
        using var scheduler = CreateScheduler();
        Assert.Equal(
            PlaybackSchedulerEnqueueResult.Enqueued,
            scheduler.Enqueue(existing, 5, PlaybackDecodedFrameDiscontinuityKind.None));
        var video = CreateFrame(PlaybackDecoderFfmpegFrameMediaKind.Video, TimeSpan.Zero);
        var audio = CreateFrame(PlaybackDecoderFfmpegFrameMediaKind.Audio, TimeSpan.FromMilliseconds(10));
        using var source = CreateSource(video, audio, CreateLease(events));
        var dispatch = new FakeDispatchOperations();

        using var completion = await CreateTransaction(dispatch).ExecuteAsync(
            source,
            scheduler,
            new PlaybackTimedSchedulerDispatchOptions());

        Assert.Equal(
            PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.InvalidScheduler,
            completion.Result);
        Assert.True(video.IsDisposed);
        Assert.True(audio.IsDisposed);
        Assert.False(existing.IsDisposed);
        Assert.Equal(1, scheduler.GetSnapshot().QueueLength);
        Assert.Equal(0, dispatch.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_reports_no_frames_and_releases_lease()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        using var source = new PlaybackDiagnosticManagedFrameCopyTransactionCompletion(
            PlaybackDiagnosticManagedFrameCopyTransactionResult.Advanced,
            PlaybackDecoderFfmpegManagedFrameCopyResult.NeedMoreInput,
            null,
            PlaybackDecoderFfmpegManagedFrameCopyResult.NeedMoreInput,
            null,
            lease);
        using var scheduler = CreateScheduler();
        var dispatch = new FakeDispatchOperations();

        using var completion = await CreateTransaction(dispatch).ExecuteAsync(
            source,
            scheduler,
            new PlaybackTimedSchedulerDispatchOptions());

        Assert.Equal(
            PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.NoFrames,
            completion.Result);
        Assert.Equal(0, completion.AcceptedFrameCount);
        Assert.Null(completion.Lease);
        Assert.Equal(0, dispatch.CallCount);
        Assert.Equal(["dispose-audio", "dispose-video"], events);
    }

    [Fact]
    public async Task ExecuteAsync_retained_video_keeps_frame_and_lease_until_completion_disposal()
    {
        var events = new List<string>();
        var video = CreateFrame(PlaybackDecoderFfmpegFrameMediaKind.Video, TimeSpan.Zero);
        var lease = CreateLease(events);
        using var source = CreateVideoOnlySource(video, lease);
        using var scheduler = CreateScheduler();
        var dispatch = new FakeDispatchOperations(
            PlaybackLateAwareTimedDispatchResult.RetainedVideoLate);

        var completion = await CreateTransaction(dispatch).ExecuteAsync(
            source,
            scheduler,
            new PlaybackTimedSchedulerDispatchOptions());

        Assert.Equal(
            PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.DispatchStopped,
            completion.Result);
        Assert.Equal(1, completion.AcceptedFrameCount);
        Assert.Equal(0, completion.ConsumedFrameCount);
        Assert.True(completion.OutstandingVideo);
        Assert.Same(lease, completion.Lease);
        Assert.False(video.IsDisposed);
        Assert.Empty(events);

        completion.Dispose();

        Assert.True(video.IsDisposed);
        Assert.True(lease.IsDisposed);
        Assert.Equal(["dispose-audio", "dispose-video"], events);
    }

    [Fact]
    public async Task ExecuteAsync_video_then_audio_recovery_reports_partial_advancement()
    {
        var events = new List<string>();
        var video = CreateFrame(PlaybackDecoderFfmpegFrameMediaKind.Video, TimeSpan.Zero);
        var audio = CreateFrame(PlaybackDecoderFfmpegFrameMediaKind.Audio, TimeSpan.FromMilliseconds(10));
        var lease = CreateLease(events);
        using var source = CreateSource(video, audio, lease);
        using var scheduler = CreateScheduler();
        var dispatch = new FakeDispatchOperations(
            PlaybackLateAwareTimedDispatchResult.VideoProcessed,
            PlaybackLateAwareTimedDispatchResult.AudioRecoveryRequired);

        var completion = await CreateTransaction(dispatch).ExecuteAsync(
            source,
            scheduler,
            new PlaybackTimedSchedulerDispatchOptions());

        Assert.Equal(
            PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.PartiallyAdvanced,
            completion.Result);
        Assert.Equal(2, completion.AcceptedFrameCount);
        Assert.Equal(1, completion.ConsumedFrameCount);
        Assert.False(completion.OutstandingVideo);
        Assert.True(completion.OutstandingAudio);
        Assert.Same(lease, completion.Lease);
        Assert.True(video.IsDisposed);
        Assert.False(audio.IsDisposed);

        completion.Dispose();

        Assert.True(audio.IsDisposed);
        Assert.Equal(["dispose-audio", "dispose-video"], events);
    }

    [Fact]
    public async Task ExecuteAsync_cancelled_result_retains_outstanding_ownership()
    {
        var events = new List<string>();
        var video = CreateFrame(PlaybackDecoderFfmpegFrameMediaKind.Video, TimeSpan.Zero);
        var lease = CreateLease(events);
        using var source = CreateVideoOnlySource(video, lease);
        using var scheduler = CreateScheduler();
        var dispatch = new FakeDispatchOperations(PlaybackLateAwareTimedDispatchResult.Cancelled);

        var completion = await CreateTransaction(dispatch).ExecuteAsync(
            source,
            scheduler,
            new PlaybackTimedSchedulerDispatchOptions());

        Assert.Equal(
            PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.Cancelled,
            completion.Result);
        Assert.True(completion.OutstandingVideo);
        Assert.Same(lease, completion.Lease);
        Assert.False(video.IsDisposed);

        completion.Dispose();
        Assert.True(video.IsDisposed);
        Assert.True(lease.IsDisposed);
    }

    [Fact]
    public async Task ExecuteAsync_audio_ingress_rejection_after_video_acceptance_is_partial()
    {
        var events = new List<string>();
        var video = CreateFrame(PlaybackDecoderFfmpegFrameMediaKind.Video, TimeSpan.Zero);
        var audio = CreateFrame(PlaybackDecoderFfmpegFrameMediaKind.Audio, TimeSpan.FromMilliseconds(10));
        using var source = CreateSource(video, audio, CreateLease(events));
        using var scheduler = CreateScheduler(new PlaybackSchedulerOptions
        {
            Capacity = 1,
            FullPolicy = PlaybackSchedulerFullPolicy.Reject,
        });
        var dispatch = new FakeDispatchOperations(
            PlaybackLateAwareTimedDispatchResult.VideoProcessed);

        using var completion = await CreateTransaction(dispatch).ExecuteAsync(
            source,
            scheduler,
            new PlaybackTimedSchedulerDispatchOptions());

        Assert.Equal(
            PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.PartiallyAdvanced,
            completion.Result);
        Assert.Equal(
            PlaybackDiagnosticManagedFrameSchedulerIngressTransactionResult.AudioEnqueueRejected,
            completion.IngressResult);
        Assert.Equal(1, completion.AcceptedFrameCount);
        Assert.Equal(1, completion.ConsumedFrameCount);
        Assert.Null(completion.Lease);
        Assert.True(video.IsDisposed);
        Assert.True(audio.IsDisposed);
        Assert.Equal(["dispose-audio", "dispose-video"], events);
    }

    [Fact]
    public async Task ExecuteAsync_fault_after_video_consumption_retains_audio_until_cleanup()
    {
        var events = new List<string>();
        var video = CreateFrame(PlaybackDecoderFfmpegFrameMediaKind.Video, TimeSpan.Zero);
        var audio = CreateFrame(PlaybackDecoderFfmpegFrameMediaKind.Audio, TimeSpan.FromMilliseconds(10));
        var lease = CreateLease(events);
        using var source = CreateSource(video, audio, lease);
        using var scheduler = CreateScheduler();
        var dispatch = new FakeDispatchOperations(
            PlaybackLateAwareTimedDispatchResult.VideoProcessed)
        {
            ThrowOnCall = 2,
        };

        var completion = await CreateTransaction(dispatch).ExecuteAsync(
            source,
            scheduler,
            new PlaybackTimedSchedulerDispatchOptions());

        Assert.Equal(
            PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.Faulted,
            completion.Result);
        Assert.Equal(1, completion.ConsumedFrameCount);
        Assert.True(completion.OutstandingAudio);
        Assert.Same(lease, completion.Lease);
        Assert.True(video.IsDisposed);
        Assert.False(audio.IsDisposed);

        completion.Dispose();
        Assert.True(audio.IsDisposed);
        Assert.True(lease.IsDisposed);
    }

    [Fact]
    public async Task ExecuteAsync_invalid_options_fail_before_ingress()
    {
        var events = new List<string>();
        var video = CreateFrame(PlaybackDecoderFfmpegFrameMediaKind.Video, TimeSpan.Zero);
        var lease = CreateLease(events);
        using var source = CreateVideoOnlySource(video, lease);
        using var scheduler = CreateScheduler();
        var dispatch = new FakeDispatchOperations();

        using var completion = await CreateTransaction(dispatch).ExecuteAsync(
            source,
            scheduler,
            new PlaybackTimedSchedulerDispatchOptions
            {
                MaximumDecisionCount = 0,
            });

        Assert.Equal(
            PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.InvalidConfiguration,
            completion.Result);
        Assert.True(source.IsDisposed);
        Assert.True(video.IsDisposed);
        Assert.True(lease.IsDisposed);
        Assert.Equal(0, dispatch.CallCount);
    }

    private static PlaybackDiagnosticLateAwareSchedulerDispatchTransaction CreateTransaction(
        FakeDispatchOperations dispatch) =>
        new(
            new PlaybackDiagnosticManagedFrameSchedulerIngressTransaction(),
            dispatch);

    private static PlaybackDiagnosticManagedFrameCopyTransactionCompletion CreateSource(
        PlaybackDecoderFfmpegManagedFrame video,
        PlaybackDecoderFfmpegManagedFrame audio,
        PlaybackDiagnosticNativeDecoderLease lease) =>
        new(
            PlaybackDiagnosticManagedFrameCopyTransactionResult.Advanced,
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            video,
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            audio,
            lease);

    private static PlaybackDiagnosticManagedFrameCopyTransactionCompletion CreateVideoOnlySource(
        PlaybackDecoderFfmpegManagedFrame video,
        PlaybackDiagnosticNativeDecoderLease lease) =>
        new(
            PlaybackDiagnosticManagedFrameCopyTransactionResult.Advanced,
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            video,
            PlaybackDecoderFfmpegManagedFrameCopyResult.NeedMoreInput,
            null,
            lease);

    private static BoundedPlaybackScheduler CreateScheduler(
        PlaybackSchedulerOptions? options = null) =>
        new(
            options ?? new PlaybackSchedulerOptions(),
            new PlaybackPolicyClockBoundary(
                new FakeClock(),
                new PlaybackPolicyOptions()));

    private static PlaybackDiagnosticNativeDecoderLease CreateLease(List<string> events) =>
        new(
            new FakeContextOwner("video", events),
            new FakeContextOwner("audio", events));

    private static PlaybackDecoderFfmpegManagedFrame CreateFrame(
        PlaybackDecoderFfmpegFrameMediaKind kind,
        TimeSpan? presentationTimestamp)
    {
        var timestamp = presentationTimestamp.HasValue
            ? PlaybackDecoderFfmpegFrameTime.Known(presentationTimestamp.Value)
            : PlaybackDecoderFfmpegFrameTime.Unknown;
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

    private sealed class FakeDispatchOperations(
        params PlaybackLateAwareTimedDispatchResult[] results) :
        IPlaybackDiagnosticLateAwareSchedulerDispatchOperations
    {
        public int CallCount { get; private set; }

        public int ThrowOnCall { get; init; }

        public async ValueTask<PlaybackLateAwareTimedDispatchResult> DispatchOneAsync(
            BoundedPlaybackScheduler scheduler,
            PlaybackTimedSchedulerDispatchOptions options,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (CallCount == ThrowOnCall)
            {
                throw new InvalidOperationException("test");
            }

            var result = results.Length == 0
                ? PlaybackLateAwareTimedDispatchResult.Empty
                : results[Math.Min(CallCount - 1, results.Length - 1)];
            if (result is PlaybackLateAwareTimedDispatchResult.VideoProcessed or
                PlaybackLateAwareTimedDispatchResult.VideoDropConsumed or
                PlaybackLateAwareTimedDispatchResult.AudioProcessed)
            {
                var kind = result == PlaybackLateAwareTimedDispatchResult.AudioProcessed
                    ? PlaybackSchedulerOutputKind.Audio
                    : PlaybackSchedulerOutputKind.Video;
                using var reservation = await scheduler.ReserveOutputAsync(kind, cancellationToken);
                if (reservation is null)
                {
                    throw new InvalidOperationException("Missing scheduler reservation.");
                }

                reservation.Complete(consume: true);
            }

            return result;
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
