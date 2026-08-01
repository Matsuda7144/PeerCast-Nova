namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffSamplePresentationTimeLookupResult
{
    Found,
    InvalidReconciliation,
    SampleNumberOutOfRange,
}

public readonly record struct PlaybackIsoBmffSamplePresentationTime(
    Int128 StartTimeUnits,
    uint DurationUnits,
    Int128 EndTimeUnitsExclusive,
    long CompositionOffsetUnits,
    bool HasExplicitCompositionOffset);

public static class PlaybackIsoBmffSamplePresentationTimeLookup
{
    public static PlaybackIsoBmffSamplePresentationTimeLookupResult Lookup(
        PlaybackIsoBmffTimeToSampleBox? timeToSample,
        PlaybackIsoBmffCompositionTimeToSampleBox? compositionTimeToSample,
        PlaybackIsoBmffReconciledSampleCount? reconciliation,
        ulong sampleNumber,
        out PlaybackIsoBmffSamplePresentationTime? presentationTime)
    {
        presentationTime = null;
        var decodeResult = PlaybackIsoBmffSampleDecodeTimeLookup.Lookup(
            timeToSample,
            reconciliation,
            sampleNumber,
            out var decodeTime);
        var compositionResult =
            PlaybackIsoBmffSampleCompositionOffsetLookup.Lookup(
                compositionTimeToSample,
                reconciliation,
                sampleNumber,
                out var compositionOffset);
        if (decodeResult ==
                PlaybackIsoBmffSampleDecodeTimeLookupResult
                    .SampleNumberOutOfRange ||
            compositionResult ==
                PlaybackIsoBmffSampleCompositionOffsetLookupResult
                    .SampleNumberOutOfRange)
        {
            return PlaybackIsoBmffSamplePresentationTimeLookupResult
                .SampleNumberOutOfRange;
        }

        if (decodeResult != PlaybackIsoBmffSampleDecodeTimeLookupResult.Found ||
            compositionResult !=
                PlaybackIsoBmffSampleCompositionOffsetLookupResult.Found ||
            decodeTime is null ||
            compositionOffset is null)
        {
            return PlaybackIsoBmffSamplePresentationTimeLookupResult
                .InvalidReconciliation;
        }

        var startTimeUnits = (Int128)decodeTime.Value.StartTimeUnits +
            compositionOffset.Value.OffsetUnits;
        presentationTime = new(
            startTimeUnits,
            decodeTime.Value.DurationUnits,
            startTimeUnits + decodeTime.Value.DurationUnits,
            compositionOffset.Value.OffsetUnits,
            compositionOffset.Value.HasExplicitOffset);
        return PlaybackIsoBmffSamplePresentationTimeLookupResult.Found;
    }
}
