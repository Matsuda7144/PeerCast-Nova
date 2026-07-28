namespace PeerCast.Nova.Playback;

internal sealed class PlaybackApprovedExternalRuntimeWindowsDeliveryTraceRetention
{
    private readonly object _sync = new();
    private PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot _retained;

    internal PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot Select(
        PlaybackProductionPcpFlvCompositionSnapshot? production)
    {
        if (production is not null)
        {
            return production.DeliveryTrace;
        }

        lock (_sync)
        {
            return _retained;
        }
    }

    internal void Capture(PlaybackProductionPcpFlvCompositionSnapshot? production)
    {
        if (production is null)
        {
            return;
        }

        lock (_sync)
        {
            _retained = production.DeliveryTrace;
        }
    }

    internal void Reset()
    {
        lock (_sync)
        {
            _retained = default;
        }
    }
}
