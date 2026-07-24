namespace PeerCast.Nova.Playback;

internal static class PlaybackProductionGenerationDeliveryTraceProjection
{
    internal static PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot Capture(
        PlaybackProductionDecoderGeneration? generation)
    {
        try
        {
            return generation is { IsDisposed: false }
                ? generation.Snapshot?.DeliveryTrace ?? default
                : default;
        }
        catch (ObjectDisposedException)
        {
            return default;
        }
    }
}
