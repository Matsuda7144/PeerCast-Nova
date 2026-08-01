namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffEditIntervalProjectionResult
{
    Projected,
    MissingEditList,
    MissingMovieTiming,
    MissingMediaTiming,
    EntryIndexOutOfRange,
    EmptyEdit,
    UnsupportedMediaRate,
    NonIntegralMediaDuration,
}

public readonly record struct PlaybackIsoBmffEditInterval(
    int EntryIndex,
    ulong MovieStartUnits,
    ulong MovieEndUnitsExclusive,
    Int128 MediaStartUnits,
    Int128 MediaEndUnitsExclusive);

public static class PlaybackIsoBmffEditIntervalProjector
{
    public static PlaybackIsoBmffEditIntervalProjectionResult TryProject(
        int entryIndex,
        PlaybackIsoBmffEditListBox? editList,
        PlaybackIsoBmffMovieHeaderTimingBox? movieTiming,
        PlaybackIsoBmffMediaHeaderTimingBox? mediaTiming,
        out PlaybackIsoBmffEditInterval? interval)
    {
        interval = null;
        if (editList is null)
        {
            return PlaybackIsoBmffEditIntervalProjectionResult.MissingEditList;
        }

        if (movieTiming is null)
        {
            return PlaybackIsoBmffEditIntervalProjectionResult
                .MissingMovieTiming;
        }

        if (mediaTiming is null)
        {
            return PlaybackIsoBmffEditIntervalProjectionResult
                .MissingMediaTiming;
        }

        if (entryIndex < 0 || entryIndex >= editList.Entries.Count)
        {
            return PlaybackIsoBmffEditIntervalProjectionResult
                .EntryIndexOutOfRange;
        }

        ulong movieStartUnits = 0;
        for (var index = 0; index < entryIndex; index++)
        {
            movieStartUnits = checked(
                movieStartUnits +
                editList.Entries[index].SegmentDurationUnits);
        }

        return TryProjectAtMovieStart(
            entryIndex,
            movieStartUnits,
            editList,
            movieTiming,
            mediaTiming,
            out interval);
    }

    internal static PlaybackIsoBmffEditIntervalProjectionResult
        TryProjectAtMovieStart(
            int entryIndex,
            ulong movieStartUnits,
            PlaybackIsoBmffEditListBox editList,
            PlaybackIsoBmffMovieHeaderTimingBox movieTiming,
            PlaybackIsoBmffMediaHeaderTimingBox mediaTiming,
            out PlaybackIsoBmffEditInterval? interval)
    {
        interval = null;
        var entry = editList.Entries[entryIndex];
        var movieEndUnits = checked(
            movieStartUnits + entry.SegmentDurationUnits);
        if (entry.IsEmptyEdit)
        {
            return PlaybackIsoBmffEditIntervalProjectionResult.EmptyEdit;
        }

        if (entry.MediaRateInteger != 1 || entry.MediaRateFraction != 0)
        {
            return PlaybackIsoBmffEditIntervalProjectionResult
                .UnsupportedMediaRate;
        }

        var mediaDurationNumerator =
            (Int128)entry.SegmentDurationUnits * mediaTiming.Timescale;
        if (mediaDurationNumerator % movieTiming.Timescale != 0)
        {
            return PlaybackIsoBmffEditIntervalProjectionResult
                .NonIntegralMediaDuration;
        }

        var mediaStartUnits = (Int128)entry.MediaTimeUnits;
        interval = new(
            entryIndex,
            movieStartUnits,
            movieEndUnits,
            mediaStartUnits,
            mediaStartUnits +
                (mediaDurationNumerator / movieTiming.Timescale));
        return PlaybackIsoBmffEditIntervalProjectionResult.Projected;
    }
}
