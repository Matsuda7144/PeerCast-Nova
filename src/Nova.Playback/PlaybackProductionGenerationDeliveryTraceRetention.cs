namespace PeerCast.Nova.Playback;

internal sealed class PlaybackProductionGenerationDeliveryTraceRetention
{
    private readonly object _sync = new();
    private PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot _retained;

    internal PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot Select(
        PlaybackProductionDecoderGeneration? generation)
    {
        if (TryCapture(generation, out var live))
        {
            return live;
        }

        lock (_sync)
        {
            return _retained;
        }
    }

    internal void Capture(PlaybackProductionDecoderGeneration? generation)
    {
        if (!TryCapture(generation, out var trace))
        {
            return;
        }

        lock (_sync)
        {
            _retained = trace;
        }
    }

    internal void Reset()
    {
        lock (_sync)
        {
            _retained = default;
        }
    }

    private static bool TryCapture(
        PlaybackProductionDecoderGeneration? generation,
        out PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot trace)
    {
        if (generation is null || generation.IsDisposed)
        {
            trace = default;
            return false;
        }

        try
        {
            var snapshot = generation.Snapshot;
            if (snapshot is null)
            {
                trace = default;
                return false;
            }

            trace = snapshot.DeliveryTrace;
            return true;
        }
        catch (ObjectDisposedException)
        {
            trace = default;
            return false;
        }
    }
}
