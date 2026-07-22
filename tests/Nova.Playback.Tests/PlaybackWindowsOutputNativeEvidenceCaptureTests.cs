namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackWindowsOutputNativeEvidenceCaptureTests
{
    private static readonly string FixtureSha256 = new('A', 64);

    [Fact]
    public void VideoResultsProduceSanitizedDeterministicCounters()
    {
        var inner = new RecordingProcessor
        {
            VideoResults = new Queue<PlaybackWindowsOutputNativeBufferResult>([
                PlaybackWindowsOutputNativeBufferResult.Processed,
                PlaybackWindowsOutputNativeBufferResult.WouldBlock,
                PlaybackWindowsOutputNativeBufferResult.ResizeRequired,
                PlaybackWindowsOutputNativeBufferResult.DeviceLost,
            ]),
        };
        var processor = Create(inner);
        var frame = Video();

        Assert.Equal(PlaybackWindowsOutputNativeBufferResult.Processed,
            processor.ProcessVideo(new Lease(), frame));
        Assert.Equal(PlaybackWindowsOutputNativeBufferResult.WouldBlock,
            processor.ProcessVideo(new Lease(), frame));
        Assert.Equal(PlaybackWindowsOutputNativeBufferResult.ResizeRequired,
            processor.ProcessVideo(new Lease(), frame));
        Assert.Equal(PlaybackWindowsOutputNativeBufferResult.DeviceLost,
            processor.ProcessVideo(new Lease(), frame));

        var snapshot = processor.Snapshot;
        Assert.Equal(PlaybackControlledWindowsOutputEvidenceSource.SystemWindows, snapshot.Source);
        Assert.False(snapshot.CounterOverflowed);
        Assert.False(snapshot.VideoConfigurationConflict);
        Assert.Equal(PlaybackDecodedVideoPixelFormat.Bgra32, snapshot.VideoPixelFormat);
        Assert.Equal(2, snapshot.VideoWidth);
        Assert.Equal(2, snapshot.VideoHeight);
        Assert.Equal(8, snapshot.VideoStride);
        Assert.Equal(7, snapshot.VideoGeneration);
        Assert.Equal(4, snapshot.VideoPresentAttemptCount);
        Assert.Equal(1, snapshot.VideoPresentAcknowledgementCount);
        Assert.Equal(16, snapshot.VideoAcceptedByteCount);
        Assert.Equal(1, snapshot.VideoWouldBlockCount);
        Assert.Equal(1, snapshot.VideoResizeCount);
        Assert.Equal(1, snapshot.VideoDeviceLostCount);
    }

    [Fact]
    public void AudioWouldBlockDoesNotConsumeFirstStartAcknowledgement()
    {
        var inner = new RecordingProcessor
        {
            AudioResults = new Queue<PlaybackWindowsOutputNativeBufferResult>([
                PlaybackWindowsOutputNativeBufferResult.WouldBlock,
                PlaybackWindowsOutputNativeBufferResult.Processed,
                PlaybackWindowsOutputNativeBufferResult.Processed,
            ]),
        };
        var processor = Create(inner);
        var samples = Audio();

        Assert.Equal(PlaybackWindowsOutputNativeBufferResult.WouldBlock,
            processor.ProcessAudio(new Lease(), samples, startStream: true));
        Assert.Equal(PlaybackWindowsOutputNativeBufferResult.Processed,
            processor.ProcessAudio(new Lease(), samples, startStream: true));
        Assert.Equal(PlaybackWindowsOutputNativeBufferResult.Processed,
            processor.ProcessAudio(new Lease(), samples, startStream: false));

        var snapshot = processor.Snapshot;
        Assert.False(snapshot.AudioConfigurationConflict);
        Assert.Equal(PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
            snapshot.AudioSampleFormat);
        Assert.Equal(48_000, snapshot.AudioSampleRate);
        Assert.Equal(2, snapshot.AudioChannelCount);
        Assert.Equal(7, snapshot.AudioGeneration);
        Assert.Equal(3, snapshot.AudioWriteAttemptCount);
        Assert.Equal(2, snapshot.AudioWriteAcknowledgementCount);
        Assert.Equal(4, snapshot.AudioAcceptedFrameCount);
        Assert.Equal(16, snapshot.AudioAcceptedByteCount);
        Assert.Equal(1, snapshot.AudioWouldBlockCount);
        Assert.Equal(1, snapshot.AudioStartAcknowledgementCount);
        Assert.Equal(0, snapshot.AudioDeviceLostCount);
    }

    [Fact]
    public void SuccessfulConfigurationChangesLatchConflictWithoutChangingNativeResult()
    {
        var processor = Create(new RecordingProcessor());

        Assert.Equal(PlaybackWindowsOutputNativeBufferResult.Processed,
            processor.ProcessVideo(new Lease(), Video()));
        Assert.Equal(PlaybackWindowsOutputNativeBufferResult.Processed,
            processor.ProcessVideo(new Lease(), Video() with
            {
                Width = 3,
                Stride = 12,
                Buffer = new byte[24],
            }));
        Assert.Equal(PlaybackWindowsOutputNativeBufferResult.Processed,
            processor.ProcessAudio(new Lease(), Audio(), startStream: true));
        Assert.Equal(PlaybackWindowsOutputNativeBufferResult.Processed,
            processor.ProcessAudio(new Lease(), Audio() with { Generation = 8 }, startStream: false));

        Assert.True(processor.Snapshot.VideoConfigurationConflict);
        Assert.True(processor.Snapshot.AudioConfigurationConflict);
    }

    [Fact]
    public void SystemVideoSnapshotPassesPhaseOneGate()
    {
        var processor = Create(new RecordingProcessor
        {
            VideoResults = new Queue<PlaybackWindowsOutputNativeBufferResult>([
                PlaybackWindowsOutputNativeBufferResult.WouldBlock,
                PlaybackWindowsOutputNativeBufferResult.Processed,
            ]),
        });
        processor.ProcessVideo(new Lease(), Video());
        processor.ProcessVideo(new Lease(), Video());
        using var capture = new PlaybackControlledWindowsOutputCaptureBoundary(
            PlaybackControlledWindowsOutputValidationKind.Video,
            FixtureSha256);

        var result = capture.CaptureAndConsume(Context(
            processor.Snapshot,
            deliveryAttempts: 2,
            delivered: 1,
            retainedWouldBlock: 1));

        Assert.Equal(PlaybackControlledWindowsOutputEvidenceResult.Accepted, result);
        Assert.True(capture.Snapshot.HasAcceptedEvidence);
    }

    [Fact]
    public void SystemAudioSnapshotPassesPhaseOneGate()
    {
        var processor = Create(new RecordingProcessor
        {
            AudioResults = new Queue<PlaybackWindowsOutputNativeBufferResult>([
                PlaybackWindowsOutputNativeBufferResult.WouldBlock,
                PlaybackWindowsOutputNativeBufferResult.Processed,
                PlaybackWindowsOutputNativeBufferResult.Processed,
            ]),
        });
        processor.ProcessAudio(new Lease(), Audio(), startStream: true);
        processor.ProcessAudio(new Lease(), Audio(), startStream: true);
        processor.ProcessAudio(new Lease(), Audio(), startStream: false);
        using var capture = new PlaybackControlledWindowsOutputCaptureBoundary(
            PlaybackControlledWindowsOutputValidationKind.Audio,
            FixtureSha256);

        var result = capture.CaptureAndConsume(Context(
            processor.Snapshot,
            deliveryAttempts: 3,
            delivered: 2,
            retainedWouldBlock: 1));

        Assert.Equal(PlaybackControlledWindowsOutputEvidenceResult.Accepted, result);
        Assert.True(capture.Snapshot.HasAcceptedEvidence);
    }

    [Fact]
    public void InjectedProvenanceCannotPassPhysicalAdmission()
    {
        var processor = new PlaybackWindowsOutputNativeEvidenceProcessor(
            new RecordingProcessor(),
            PlaybackControlledWindowsOutputEvidenceSource.Injected);
        processor.ProcessVideo(new Lease(), Video());
        using var capture = new PlaybackControlledWindowsOutputCaptureBoundary(
            PlaybackControlledWindowsOutputValidationKind.Video,
            FixtureSha256);

        var result = capture.CaptureAndConsume(Context(
            processor.Snapshot,
            deliveryAttempts: 1,
            delivered: 1,
            retainedWouldBlock: 0));

        Assert.Equal(PlaybackControlledWindowsOutputEvidenceResult.SourceRejected, result);
        Assert.False(capture.Snapshot.HasAcceptedEvidence);
    }

    [Fact]
    public void ConfigurationAndWouldBlockContradictionsFailClosed()
    {
        var changed = Create(new RecordingProcessor());
        changed.ProcessVideo(new Lease(), Video());
        changed.ProcessVideo(new Lease(), Video() with
        {
            Generation = 8,
        });
        using var configuration = new PlaybackControlledWindowsOutputCaptureBoundary(
            PlaybackControlledWindowsOutputValidationKind.Video,
            FixtureSha256);

        Assert.Equal(
            PlaybackControlledWindowsOutputEvidenceResult.ConfigurationRejected,
            configuration.CaptureAndConsume(Context(
                changed.Snapshot,
                deliveryAttempts: 2,
                delivered: 2,
                retainedWouldBlock: 0)));

        var retained = Create(new RecordingProcessor
        {
            VideoResults = new Queue<PlaybackWindowsOutputNativeBufferResult>([
                PlaybackWindowsOutputNativeBufferResult.WouldBlock,
                PlaybackWindowsOutputNativeBufferResult.Processed,
            ]),
        });
        retained.ProcessVideo(new Lease(), Video());
        retained.ProcessVideo(new Lease(), Video());
        using var mismatch = new PlaybackControlledWindowsOutputCaptureBoundary(
            PlaybackControlledWindowsOutputValidationKind.Video,
            FixtureSha256);

        Assert.Equal(
            PlaybackControlledWindowsOutputEvidenceResult.ConfigurationRejected,
            mismatch.CaptureAndConsume(Context(
                retained.Snapshot,
                deliveryAttempts: 2,
                delivered: 1,
                retainedWouldBlock: 0)));
    }

    [Fact]
    public void BufferSnapshotCarriesEvidenceAndResetRemainsDelegated()
    {
        var inner = new RecordingProcessor();
        var processor = Create(inner);
        processor.ProcessVideo(new Lease(), Video());
        processor.ResetVideo(new Lease());
        processor.ResetAudio(new Lease());
        var buffers = new PlaybackWindowsOutputBufferSnapshot(
            PlaybackWindowsOutputBufferState.Ready,
            PlaybackWindowsOutputBufferState.Ready,
            7,
            7,
            true)
        {
            NativeEvidence = processor.Snapshot,
        };

        Assert.Equal(1, buffers.NativeEvidence.VideoPresentAcknowledgementCount);
        Assert.Equal(["video", "audio"], inner.Resets);
    }

    private static PlaybackWindowsOutputNativeEvidenceProcessor Create(
        IPlaybackWindowsOutputNativeBufferProcessor inner) => new(
            inner,
            PlaybackControlledWindowsOutputEvidenceSource.SystemWindows);

    private static PlaybackControlledWindowsOutputCaptureContext Context(
        PlaybackWindowsOutputNativeEvidenceSnapshot native,
        long deliveryAttempts,
        long delivered,
        long retainedWouldBlock) => new(
            PlaybackFfmpegBackendCandidate.BuildId,
            FixtureSha256,
            DecoderGeneration: 7,
            OutputGeneration: 7,
            deliveryAttempts,
            delivered,
            retainedWouldBlock,
            HiddenRetryCount: 0,
            DeviceFallbackCount: 0,
            DeviceRecreationCount: 0,
            TimeSpan.FromMilliseconds(100),
            new(
                PlaybackWindowsOutputBufferState.Ready,
                PlaybackWindowsOutputBufferState.Ready,
                VideoGeneration: 7,
                AudioGeneration: 7,
                AudioStarted: true)
            {
                NativeEvidence = native,
            },
            new(
                ProductionStopOrder: 1,
                BufferDisposeOrder: 2,
                StreamDisposeOrder: 3,
                BindingDisposeOrder: 4,
                ActivationDisposeOrder: 5,
                OwnerThreadTeardown: true,
                BorrowedWindowPreserved: true,
                BorrowedDispatcherPreserved: true));

    private static PlaybackWindowsVideoFrameBuffer Video() => new(
        PlaybackDecodedVideoPixelFormat.Bgra32,
        Width: 2,
        Height: 2,
        Stride: 8,
        new byte[16],
        Generation: 7);

    private static PlaybackWindowsAudioSampleBuffer Audio() => new(
        PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
        SampleRate: 48_000,
        ChannelCount: 2,
        FrameCount: 2,
        new byte[8],
        Generation: 7);

    private sealed class Lease : IDisposable
    {
        public void Dispose() { }
    }

    private sealed class RecordingProcessor : IPlaybackWindowsOutputNativeBufferProcessor
    {
        public Queue<PlaybackWindowsOutputNativeBufferResult>? VideoResults { get; init; }
        public Queue<PlaybackWindowsOutputNativeBufferResult>? AudioResults { get; init; }
        public List<string> Resets { get; } = [];

        public PlaybackWindowsOutputNativeBufferResult ProcessVideo(
            IDisposable borrowedResource,
            PlaybackWindowsVideoFrameBuffer frame) =>
            VideoResults?.Dequeue() ?? PlaybackWindowsOutputNativeBufferResult.Processed;

        public PlaybackWindowsOutputNativeBufferResult ProcessAudio(
            IDisposable borrowedResource,
            PlaybackWindowsAudioSampleBuffer samples,
            bool startStream) =>
            AudioResults?.Dequeue() ?? PlaybackWindowsOutputNativeBufferResult.Processed;

        public void ResetVideo(IDisposable borrowedResource) => Resets.Add("video");

        public void ResetAudio(IDisposable borrowedResource) => Resets.Add("audio");
    }
}
