namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffSampleDecodeTimeLookupResult
{
    Found,
    InvalidReconciliation,
    SampleNumberOutOfRange,
}

public readonly record struct PlaybackIsoBmffSampleDecodeTime(
    ulong StartTimeUnits,
    uint DurationUnits,
    ulong EndTimeUnitsExclusive);

public static class PlaybackIsoBmffSampleDecodeTimeLookup
{
    public static PlaybackIsoBmffSampleDecodeTimeLookupResult Lookup(
        PlaybackIsoBmffTimeToSampleBox? timeToSample,
        PlaybackIsoBmffReconciledSampleCount? reconciliation,
        ulong sampleNumber,
        out PlaybackIsoBmffSampleDecodeTime? decodeTime)
    {
        decodeTime = null;
        if (timeToSample is null ||
            reconciliation is null ||
            timeToSample.TotalSampleCount != reconciliation.SampleCount)
        {
            return PlaybackIsoBmffSampleDecodeTimeLookupResult
                .InvalidReconciliation;
        }

        if (sampleNumber == 0 || sampleNumber > reconciliation.SampleCount)
        {
            return PlaybackIsoBmffSampleDecodeTimeLookupResult
                .SampleNumberOutOfRange;
        }

        var targetSampleIndex = sampleNumber - 1;
        ulong firstSampleIndex = 0;
        ulong runStartTimeUnits = 0;
        foreach (var entry in timeToSample.Entries)
        {
            var nextSampleIndex = firstSampleIndex + entry.SampleCount;
            if (targetSampleIndex < nextSampleIndex)
            {
                var sampleIndexWithinRun =
                    targetSampleIndex - firstSampleIndex;
                var startTimeUnits = runStartTimeUnits +
                    (sampleIndexWithinRun * entry.SampleDelta);
                decodeTime = new(
                    startTimeUnits,
                    entry.SampleDelta,
                    startTimeUnits + entry.SampleDelta);
                return PlaybackIsoBmffSampleDecodeTimeLookupResult.Found;
            }

            firstSampleIndex = nextSampleIndex;
            runStartTimeUnits += (ulong)entry.SampleCount * entry.SampleDelta;
        }

        return PlaybackIsoBmffSampleDecodeTimeLookupResult
            .InvalidReconciliation;
    }
}
