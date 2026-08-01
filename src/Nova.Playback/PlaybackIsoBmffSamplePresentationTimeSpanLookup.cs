namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffSamplePresentationTimeSpanLookupResult
{
    Found,
    InvalidReconciliation,
    SampleNumberOutOfRange,
    InvalidConversionConfiguration,
    MissingMediaTiming,
    MagnitudeExceeded,
}

public readonly record struct PlaybackIsoBmffSamplePresentationTimeSpan(
    TimeSpan Start,
    TimeSpan Duration,
    TimeSpan EndExclusive,
    long CompositionOffsetUnits,
    bool HasExplicitCompositionOffset);

public static class PlaybackIsoBmffSamplePresentationTimeSpanLookup
{
    public static PlaybackIsoBmffSamplePresentationTimeSpanLookupResult Lookup(
        PlaybackIsoBmffTimeToSampleBox? timeToSample,
        PlaybackIsoBmffCompositionTimeToSampleBox? compositionTimeToSample,
        PlaybackIsoBmffReconciledSampleCount? reconciliation,
        PlaybackIsoBmffMediaHeaderTimingBox? mediaTiming,
        ulong sampleNumber,
        out PlaybackIsoBmffSamplePresentationTimeSpan? presentationTime,
        PlaybackIsoBmffMediaTimeConversionOptions? conversionOptions = null)
    {
        presentationTime = null;
        var lookupResult = PlaybackIsoBmffSamplePresentationTimeLookup.Lookup(
            timeToSample,
            compositionTimeToSample,
            reconciliation,
            sampleNumber,
            out var presentationUnits);
        if (lookupResult ==
            PlaybackIsoBmffSamplePresentationTimeLookupResult
                .SampleNumberOutOfRange)
        {
            return PlaybackIsoBmffSamplePresentationTimeSpanLookupResult
                .SampleNumberOutOfRange;
        }

        if (lookupResult !=
                PlaybackIsoBmffSamplePresentationTimeLookupResult.Found ||
            presentationUnits is null)
        {
            return PlaybackIsoBmffSamplePresentationTimeSpanLookupResult
                .InvalidReconciliation;
        }

        var startResult = PlaybackIsoBmffMediaTimeConverter.TryConvert(
            presentationUnits.Value.StartTimeUnits,
            mediaTiming,
            out var start,
            conversionOptions);
        if (startResult != PlaybackIsoBmffMediaTimeConversionResult.Converted ||
            start is null)
        {
            return MapConversionResult(startResult);
        }

        var endResult = PlaybackIsoBmffMediaTimeConverter.TryConvert(
            presentationUnits.Value.EndTimeUnitsExclusive,
            mediaTiming,
            out var end,
            conversionOptions);
        if (endResult != PlaybackIsoBmffMediaTimeConversionResult.Converted ||
            end is null)
        {
            return MapConversionResult(endResult);
        }

        presentationTime = new(
            start.Value,
            end.Value - start.Value,
            end.Value,
            presentationUnits.Value.CompositionOffsetUnits,
            presentationUnits.Value.HasExplicitCompositionOffset);
        return PlaybackIsoBmffSamplePresentationTimeSpanLookupResult.Found;
    }

    private static PlaybackIsoBmffSamplePresentationTimeSpanLookupResult
        MapConversionResult(PlaybackIsoBmffMediaTimeConversionResult result) =>
        result switch
        {
            PlaybackIsoBmffMediaTimeConversionResult.InvalidConfiguration =>
                PlaybackIsoBmffSamplePresentationTimeSpanLookupResult
                    .InvalidConversionConfiguration,
            PlaybackIsoBmffMediaTimeConversionResult.MissingMediaTiming =>
                PlaybackIsoBmffSamplePresentationTimeSpanLookupResult
                    .MissingMediaTiming,
            _ => PlaybackIsoBmffSamplePresentationTimeSpanLookupResult
                .MagnitudeExceeded,
        };
}
