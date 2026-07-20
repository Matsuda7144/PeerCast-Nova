namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDiagnosticManagedFrameSchedulerIngressTransactionTests
{
    [Fact]
    public void Execute_enqueues_video_then_audio_and_transfers_ownership()
    {
        var events = new List<string>();
        var video = CreateFrame(PlaybackDecoderFfmpegFrameMediaKind.Video);
        var audio = CreateFrame(PlaybackDecoderFfmpegFrameMediaKind.Audio);
        var lease = CreateLease(events);
        using var source = CreateSource(video, audio, lease);
        var operations = new FakeOperations(
            PlaybackSchedulerEnqueueResult.Enqueued,
            PlaybackSchedulerEnqueueResult.Enqueued);
        var transaction = new PlaybackDiagnosticManagedFrameSchedulerIngressTransaction(operations);
        using var scheduler = CreateScheduler();

        using var completion = transaction.Execute(source, scheduler);

        Assert.Equal(
            PlaybackDiagnosticManagedFrameSchedulerIngressTransactionResult.Advanced,
            completion.Result);
        Assert.Equal(2, completion.AcceptedFrameCount);
        Assert.Equal(PlaybackSchedulerEnqueueResult.Enqueued, completion.VideoEnqueueResult);
        Assert.Equal(PlaybackSchedulerEnqueueResult.Enqueued, completion.AudioEnqueueResult);
        Assert.Same(lease, completion.Lease);
        Assert.Equal(
            ["Video:0:None", "Audio:1:None"],
            operations.Calls);
        Assert.Null(source.VideoFrame);
        Assert.Null(source.AudioFrame);
        Assert.Null(source.Lease);
        Assert.True(source.IsDisposed);
        Assert.False(video.IsDisposed);
        Assert.False(audio.IsDisposed);

        video.Dispose();
        audio.Dispose();
    }

    [Fact]
    public void Execute_skips_video_need_more_input_and_uses_audio_sequence_one()
    {
        var events = new List<string>();
        var audio = CreateFrame(PlaybackDecoderFfmpegFrameMediaKind.Audio);
        var lease = CreateLease(events);
        using var source = new PlaybackDiagnosticManagedFrameCopyTransactionCompletion(
            PlaybackDiagnosticManagedFrameCopyTransactionResult.Advanced,
            PlaybackDecoderFfmpegManagedFrameCopyResult.NeedMoreInput,
            null,
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            audio,
            lease);
        var operations = new FakeOperations(PlaybackSchedulerEnqueueResult.Enqueued);
        var transaction = new PlaybackDiagnosticManagedFrameSchedulerIngressTransaction(operations);
        using var scheduler = CreateScheduler();

        using var completion = transaction.Execute(source, scheduler);

        Assert.Equal(
            PlaybackDiagnosticManagedFrameSchedulerIngressTransactionResult.Advanced,
            completion.Result);
        Assert.Equal(1, completion.AcceptedFrameCount);
        Assert.Null(completion.VideoEnqueueResult);
        Assert.Equal(PlaybackSchedulerEnqueueResult.Enqueued, completion.AudioEnqueueResult);
        Assert.Equal(["Audio:1:None"], operations.Calls);

        audio.Dispose();
    }

    [Fact]
    public void Execute_video_rejection_disposes_remaining_audio_and_lease()
    {
        var events = new List<string>();
        var video = CreateFrame(PlaybackDecoderFfmpegFrameMediaKind.Video);
        var audio = CreateFrame(PlaybackDecoderFfmpegFrameMediaKind.Audio);
        var lease = CreateLease(events);
        using var source = CreateSource(video, audio, lease);
        var operations = new FakeOperations(PlaybackSchedulerEnqueueResult.DroppedIncoming);
        var transaction = new PlaybackDiagnosticManagedFrameSchedulerIngressTransaction(operations);
        using var scheduler = CreateScheduler();

        using var completion = transaction.Execute(source, scheduler);

        Assert.Equal(
            PlaybackDiagnosticManagedFrameSchedulerIngressTransactionResult.VideoEnqueueRejected,
            completion.Result);
        Assert.Equal(0, completion.AcceptedFrameCount);
        Assert.True(video.IsDisposed);
        Assert.True(audio.IsDisposed);
        Assert.Null(completion.Lease);
        Assert.Equal(["dispose-audio", "dispose-video"], events);
    }

    [Fact]
    public void Execute_audio_rejection_after_video_acceptance_retains_lease()
    {
        var events = new List<string>();
        var video = CreateFrame(PlaybackDecoderFfmpegFrameMediaKind.Video);
        var audio = CreateFrame(PlaybackDecoderFfmpegFrameMediaKind.Audio);
        var lease = CreateLease(events);
        using var source = CreateSource(video, audio, lease);
        var operations = new FakeOperations(
            PlaybackSchedulerEnqueueResult.Enqueued,
            PlaybackSchedulerEnqueueResult.Rejected);
        var transaction = new PlaybackDiagnosticManagedFrameSchedulerIngressTransaction(operations);
        using var scheduler = CreateScheduler();

        var completion = transaction.Execute(source, scheduler);

        Assert.Equal(
            PlaybackDiagnosticManagedFrameSchedulerIngressTransactionResult.AudioEnqueueRejected,
            completion.Result);
        Assert.Equal(1, completion.AcceptedFrameCount);
        Assert.Same(lease, completion.Lease);
        Assert.False(video.IsDisposed);
        Assert.True(audio.IsDisposed);
        Assert.Empty(events);

        completion.Dispose();
        Assert.Equal(["dispose-audio", "dispose-video"], events);
        video.Dispose();
    }

    [Fact]
    public void Execute_fault_after_video_acceptance_retains_lease_and_disposes_audio()
    {
        var events = new List<string>();
        var video = CreateFrame(PlaybackDecoderFfmpegFrameMediaKind.Video);
        var audio = CreateFrame(PlaybackDecoderFfmpegFrameMediaKind.Audio);
        var lease = CreateLease(events);
        using var source = CreateSource(video, audio, lease);
        var operations = new FakeOperations(PlaybackSchedulerEnqueueResult.Enqueued)
        {
            ThrowOnCall = 2,
        };
        var transaction = new PlaybackDiagnosticManagedFrameSchedulerIngressTransaction(operations);
        using var scheduler = CreateScheduler();

        var completion = transaction.Execute(source, scheduler);

        Assert.Equal(
            PlaybackDiagnosticManagedFrameSchedulerIngressTransactionResult.Faulted,
            completion.Result);
        Assert.Equal(1, completion.AcceptedFrameCount);
        Assert.Same(lease, completion.Lease);
        Assert.False(video.IsDisposed);
        Assert.True(audio.IsDisposed);

        completion.Dispose();
        video.Dispose();
    }

    [Fact]
    public void Execute_rejects_nonadvanced_source_and_disposes_it()
    {
        var events = new List<string>();
        var lease = CreateLease(events);
        using var source = new PlaybackDiagnosticManagedFrameCopyTransactionCompletion(
            PlaybackDiagnosticManagedFrameCopyTransactionResult.Faulted,
            null,
            null,
            null,
            null,
            lease);
        var transaction = new PlaybackDiagnosticManagedFrameSchedulerIngressTransaction(
            new FakeOperations());
        using var scheduler = CreateScheduler();

        using var completion = transaction.Execute(source, scheduler);

        Assert.Equal(
            PlaybackDiagnosticManagedFrameSchedulerIngressTransactionResult.InvalidCompletion,
            completion.Result);
        Assert.True(source.IsDisposed);
        Assert.Null(completion.Lease);
        Assert.Equal(["dispose-audio", "dispose-video"], events);
    }

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

    private sealed class FakeOperations(params PlaybackSchedulerEnqueueResult[] results) :
        IPlaybackDiagnosticManagedFrameSchedulerIngressOperations
    {
        private int _callCount;

        public List<string> Calls { get; } = [];

        public int ThrowOnCall { get; init; }

        public PlaybackSchedulerEnqueueResult Enqueue(
            BoundedPlaybackScheduler scheduler,
            PlaybackDecoderFfmpegManagedFrame frame,
            long sequence,
            PlaybackDecodedFrameDiscontinuityKind discontinuity)
        {
            _callCount++;
            if (_callCount == ThrowOnCall)
            {
                throw new InvalidOperationException("test");
            }

            Calls.Add($"{frame.Metadata.MediaKind}:{sequence}:{discontinuity}");
            var result = results.Length == 0
                ? PlaybackSchedulerEnqueueResult.Enqueued
                : results[Math.Min(_callCount - 1, results.Length - 1)];
            if (result is not PlaybackSchedulerEnqueueResult.Enqueued and
                not PlaybackSchedulerEnqueueResult.EnqueuedAfterDroppingOldest)
            {
                frame.Dispose();
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
