namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffSampleCompositionOffsetLookupResult
{
    Found,
    InvalidReconciliation,
    SampleNumberOutOfRange,
}

public readonly record struct PlaybackIsoBmffSampleCompositionOffset(
    long OffsetUnits,
    bool HasExplicitOffset);

public static class PlaybackIsoBmffSampleCompositionOffsetLookup
{
    public static PlaybackIsoBmffSampleCompositionOffsetLookupResult Lookup(
        PlaybackIsoBmffCompositionTimeToSampleBox? compositionTimeToSample,
        PlaybackIsoBmffReconciledSampleCount? reconciliation,
        ulong sampleNumber,
        out PlaybackIsoBmffSampleCompositionOffset? compositionOffset)
    {
        compositionOffset = null;
        if (reconciliation is null ||
            reconciliation.HasCompositionOffsets !=
                (compositionTimeToSample is not null) ||
            compositionTimeToSample is not null &&
                compositionTimeToSample.TotalSampleCount !=
                    reconciliation.SampleCount)
        {
            return PlaybackIsoBmffSampleCompositionOffsetLookupResult
                .InvalidReconciliation;
        }

        if (sampleNumber == 0 || sampleNumber > reconciliation.SampleCount)
        {
            return PlaybackIsoBmffSampleCompositionOffsetLookupResult
                .SampleNumberOutOfRange;
        }

        if (compositionTimeToSample is null)
        {
            compositionOffset = new(0, HasExplicitOffset: false);
            return PlaybackIsoBmffSampleCompositionOffsetLookupResult.Found;
        }

        var targetSampleIndex = sampleNumber - 1;
        ulong nextSampleIndex = 0;
        foreach (var entry in compositionTimeToSample.Entries)
        {
            nextSampleIndex += entry.SampleCount;
            if (targetSampleIndex < nextSampleIndex)
            {
                compositionOffset = new(
                    entry.SampleOffset,
                    HasExplicitOffset: true);
                return PlaybackIsoBmffSampleCompositionOffsetLookupResult.Found;
            }
        }

        return PlaybackIsoBmffSampleCompositionOffsetLookupResult
            .InvalidReconciliation;
    }
}
