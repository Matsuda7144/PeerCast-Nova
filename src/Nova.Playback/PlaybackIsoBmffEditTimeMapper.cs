namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffEditTimeMappingResult
{
    Mapped,
    MissingEditList,
    MissingMovieTiming,
    MissingMediaTiming,
    MovieTimeOutOfRange,
    EmptyEdit,
    UnsupportedMediaRate,
    NonIntegralMediaTime,
}

public readonly record struct PlaybackIsoBmffEditTimeMapping(
    PlaybackIsoBmffEditSegmentLocation Segment,
    Int128 MediaTimeUnits);

public static class PlaybackIsoBmffEditTimeMapper
{
    public static PlaybackIsoBmffEditTimeMappingResult TryMap(
        ulong movieTimeUnits,
        PlaybackIsoBmffEditListBox? editList,
        PlaybackIsoBmffMovieHeaderTimingBox? movieTiming,
        PlaybackIsoBmffMediaHeaderTimingBox? mediaTiming,
        out PlaybackIsoBmffEditTimeMapping? mapping)
    {
        mapping = null;
        if (editList is null)
        {
            return PlaybackIsoBmffEditTimeMappingResult.MissingEditList;
        }

        if (movieTiming is null)
        {
            return PlaybackIsoBmffEditTimeMappingResult.MissingMovieTiming;
        }

        if (mediaTiming is null)
        {
            return PlaybackIsoBmffEditTimeMappingResult.MissingMediaTiming;
        }

        var lookupResult = PlaybackIsoBmffEditSegmentLookup.TryLocate(
            movieTimeUnits,
            editList,
            out var segment);
        if (lookupResult != PlaybackIsoBmffEditSegmentLookupResult.Located ||
            segment is null)
        {
            return PlaybackIsoBmffEditTimeMappingResult.MovieTimeOutOfRange;
        }

        if (segment.Value.IsEmptyEdit)
        {
            return PlaybackIsoBmffEditTimeMappingResult.EmptyEdit;
        }

        if (segment.Value.Entry.MediaRateInteger != 1 ||
            segment.Value.Entry.MediaRateFraction != 0)
        {
            return PlaybackIsoBmffEditTimeMappingResult.UnsupportedMediaRate;
        }

        var mediaOffsetNumerator =
            (Int128)segment.Value.SegmentOffsetUnits * mediaTiming.Timescale;
        if (mediaOffsetNumerator % movieTiming.Timescale != 0)
        {
            return PlaybackIsoBmffEditTimeMappingResult.NonIntegralMediaTime;
        }

        mapping = new(
            segment.Value,
            segment.Value.Entry.MediaTimeUnits +
                (mediaOffsetNumerator / movieTiming.Timescale));
        return PlaybackIsoBmffEditTimeMappingResult.Mapped;
    }
}
