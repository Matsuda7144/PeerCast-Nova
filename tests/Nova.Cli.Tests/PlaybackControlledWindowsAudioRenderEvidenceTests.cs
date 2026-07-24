using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsAudioRenderEvidenceTests
{
    [Fact]
    public void Default_lifecycle_projects_absent_traces()
    {
        var lifecycle = CreateLifecycle(default, default);

        var actual = PlaybackControlledWindowsAudioRenderEvidenceProjection.Capture(lifecycle);

        Assert.Equal(
            default(PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot),
            actual.DeliveryTrace);
        Assert.Equal(
            default(PlaybackWindowsAudioRenderTraceSnapshot),
            actual.AudioRenderTrace);
    }

    [Fact]
    public void Projection_preserves_exact_delivery_and_audio_render_traces()
    {
        var deliveryTrace = CreateDeliveryTrace(sequence: 3);
        var audioRenderTrace = CreateAudioRenderTrace();
        var lifecycle = CreateLifecycle(deliveryTrace, audioRenderTrace);

        var actual = PlaybackControlledWindowsAudioRenderEvidenceProjection.Capture(lifecycle);

        Assert.Equal(deliveryTrace, actual.DeliveryTrace);
        Assert.Equal(audioRenderTrace, actual.AudioRenderTrace);
        Assert.Equal(3L, actual.DeliveryTrace.First.Sequence);
        Assert.Equal(1, actual.AudioRenderTrace.Count);
        Assert.Equal(1024, actual.AudioRenderTrace.First.RequestedFrameCount);
        Assert.Equal(1024, actual.AudioRenderTrace.PreStop.PaddingFrameCount);
    }

    [Fact]
    public void Render_trace_opt_in_also_enables_generation_delivery_observations()
    {
        var normal = SystemPlaybackControlledWindowsPhysicalRunOperations.CreateLifecycleOptions(
            TimeSpan.FromMilliseconds(100),
            PlaybackControlledWindowsOutputValidationKind.Audio);
        var configured =
            SystemPlaybackControlledWindowsPhysicalRunOperations.CreateLifecycleOptions(
                TimeSpan.FromMilliseconds(100),
                PlaybackControlledWindowsOutputValidationKind.Audio,
                captureAudioRenderTrace: true,
                capturePreStopPadding: true);

        Assert.False(normal.Generation.CaptureDeliveryObservations);
        Assert.True(configured.Generation.CaptureDeliveryObservations);
    }

    private static PlaybackApprovedExternalRuntimeWindowsLifecycleSnapshot CreateLifecycle(
        PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot deliveryTrace,
        PlaybackWindowsAudioRenderTraceSnapshot audioRenderTrace)
    {
        var native = new PlaybackWindowsOutputNativeEvidenceSnapshot(
            Source: PlaybackControlledWindowsOutputEvidenceSource.Injected,
            CounterOverflowed: false,
            VideoConfigurationConflict: false,
            VideoPixelFormat: PlaybackDecodedVideoPixelFormat.Bgra32,
            VideoWidth: 0,
            VideoHeight: 0,
            VideoStride: 0,
            VideoGeneration: -1,
            VideoPresentAttemptCount: 0,
            VideoPresentAcknowledgementCount: 0,
            VideoAcceptedByteCount: 0,
            VideoWouldBlockCount: 0,
            VideoResizeCount: 0,
            VideoDeviceLostCount: 0,
            AudioConfigurationConflict: false,
            AudioSampleFormat: PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved,
            AudioSampleRate: 48_000,
            AudioChannelCount: 2,
            AudioGeneration: 0,
            AudioWriteAttemptCount: 1,
            AudioWriteAcknowledgementCount: 1,
            AudioAcceptedFrameCount: 1024,
            AudioAcceptedByteCount: 8192,
            AudioWouldBlockCount: 0,
            AudioStartAcknowledgementCount: 1,
            AudioDeviceLostCount: 0)
        {
            AudioRenderTrace = audioRenderTrace,
        };
        var buffers = new PlaybackWindowsOutputBufferSnapshot(
            VideoState: PlaybackWindowsOutputBufferState.Ready,
            AudioState: PlaybackWindowsOutputBufferState.Ready,
            VideoGeneration: -1,
            AudioGeneration: 0,
            AudioStarted: true)
        {
            NativeEvidence = native,
        };
        return new(
            State: PlaybackApprovedExternalRuntimeWindowsLifecycleState.Ready,
            Result: PlaybackApprovedExternalRuntimeWindowsLifecycleResult.AudioDelivered,
            DecoderGeneration: 0,
            DiscontinuityGeneration: 0,
            OutputReadyGeneration: 0,
            VideoWidth: 1920,
            VideoHeight: 1080,
            AudioSampleRate: 48_000,
            AudioChannelCount: 2,
            HasActiveOperation: false,
            ProbeResult: PlaybackWindowsOutputProbeResult.Available,
            ActivationResult: PlaybackWindowsOutputActivationResult.Activated,
            Binding: default,
            Streams: default,
            Buffers: buffers)
        {
            DeliveryTrace = deliveryTrace,
        };
    }

    private static PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot CreateDeliveryTrace(
        long sequence)
    {
        var observation = new PlaybackGenerationMatchedSchedulerDeliveryObservation(
            Sequence: sequence,
            MediaKind: PlaybackPolicyMediaKind.Audio,
            HasPresentationTimestampOffset: true,
            PresentationTimestampOffsetTicks: TimeSpan.FromMilliseconds(20).Ticks,
            HasTimingDecision: true,
            TimingResult: PlaybackAvTimingDecisionResult.ProcessNow,
            TimingWaitTicks: 0,
            Result: PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered,
            Completion: PlaybackGenerationMatchedSchedulerReservationCompletion.Delivered);
        return new(
            Count: 1,
            Overflowed: false,
            First: observation,
            Second: default,
            Third: default,
            Fourth: default);
    }

    private static PlaybackWindowsAudioRenderTraceSnapshot CreateAudioRenderTrace()
    {
        var observation = new PlaybackWindowsAudioRenderWriteObservation(
            RequestedFrameCount: 1024,
            RequestedByteCount: 8192,
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
            Result: PlaybackWindowsAudioRenderWriteResult.Processed);
        return new(
            Count: 1,
            Overflowed: false,
            First: observation,
            Second: default,
            Third: default,
            Fourth: default,
            PreStop: new(
                QueryCount: 1,
                Available: true,
                PaddingFrameCount: 1024,
                Overflowed: false));
    }
}
