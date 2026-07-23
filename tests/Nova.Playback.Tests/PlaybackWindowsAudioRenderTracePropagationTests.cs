namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackWindowsAudioRenderTracePropagationTests
{
    [Fact]
    public void Native_evidence_snapshot_carries_inner_audio_render_trace()
    {
        var expected = new PlaybackWindowsAudioRenderTraceSnapshot(
            Count: 1,
            Overflowed: false,
            First: new(
                RequestedFrameCount: 1024,
                RequestedByteCount: 4096,
                HasCapacityFrameCount: true,
                CapacityFrameCount: 4800,
                HasPaddingFrameCount: true,
                PaddingFrameCount: 0,
                HasAvailableFrameCount: true,
                AvailableFrameCount: 4800,
                StartRequested: true,
                GetBufferAcknowledged: true,
                ReleaseBufferAcknowledged: true,
                StartAcknowledged: true,
                Result: PlaybackWindowsAudioRenderWriteResult.Processed),
            Second: default,
            Third: default,
            Fourth: default,
            PreStop: new(
                QueryCount: 1,
                Available: true,
                PaddingFrameCount: 256,
                Overflowed: false));
        var processor = new PlaybackWindowsOutputNativeEvidenceProcessor(
            new TraceProcessor(expected),
            PlaybackControlledWindowsOutputEvidenceSource.SystemWindows);

        var snapshot = processor.Snapshot;

        Assert.Equal(expected, snapshot.AudioRenderTrace);
        Assert.Equal(expected, processor.Snapshot.AudioRenderTrace);
    }

    [Fact]
    public void Native_evidence_snapshot_is_empty_when_inner_has_no_trace_source()
    {
        var processor = new PlaybackWindowsOutputNativeEvidenceProcessor(
            new PlainProcessor(),
            PlaybackControlledWindowsOutputEvidenceSource.SystemWindows);

        Assert.Equal(
            default(PlaybackWindowsAudioRenderTraceSnapshot),
            processor.Snapshot.AudioRenderTrace);
    }

    private sealed class TraceProcessor(PlaybackWindowsAudioRenderTraceSnapshot trace) :
        IPlaybackWindowsOutputNativeBufferProcessor,
        IPlaybackWindowsAudioRenderTraceSource
    {
        public PlaybackWindowsAudioRenderTraceSnapshot AudioRenderTrace => trace;

        public PlaybackWindowsOutputNativeBufferResult ProcessVideo(
            IDisposable borrowedResource,
            PlaybackWindowsVideoFrameBuffer frame) =>
            PlaybackWindowsOutputNativeBufferResult.Processed;

        public PlaybackWindowsOutputNativeBufferResult ProcessAudio(
            IDisposable borrowedResource,
            PlaybackWindowsAudioSampleBuffer samples,
            bool startStream) =>
            PlaybackWindowsOutputNativeBufferResult.Processed;

        public void ResetVideo(IDisposable borrowedResource)
        {
        }

        public void ResetAudio(IDisposable borrowedResource)
        {
        }
    }

    private sealed class PlainProcessor : IPlaybackWindowsOutputNativeBufferProcessor
    {
        public PlaybackWindowsOutputNativeBufferResult ProcessVideo(
            IDisposable borrowedResource,
            PlaybackWindowsVideoFrameBuffer frame) =>
            PlaybackWindowsOutputNativeBufferResult.Processed;

        public PlaybackWindowsOutputNativeBufferResult ProcessAudio(
            IDisposable borrowedResource,
            PlaybackWindowsAudioSampleBuffer samples,
            bool startStream) =>
            PlaybackWindowsOutputNativeBufferResult.Processed;

        public void ResetVideo(IDisposable borrowedResource)
        {
        }

        public void ResetAudio(IDisposable borrowedResource)
        {
        }
    }
}
