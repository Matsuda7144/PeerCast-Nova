using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli;

internal readonly record struct PlaybackControlledWindowsAudioRenderEvidenceSnapshot(
    PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot DeliveryTrace,
    PlaybackWindowsAudioRenderTraceSnapshot AudioRenderTrace);

internal static class PlaybackControlledWindowsAudioRenderEvidenceProjection
{
    internal static PlaybackControlledWindowsAudioRenderEvidenceSnapshot Capture(
        PlaybackApprovedExternalRuntimeWindowsLifecycleSnapshot lifecycle) =>
        new(
            lifecycle.DeliveryTrace,
            lifecycle.Buffers.NativeEvidence.AudioRenderTrace);
}
