namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffEditSegmentLookupResult
{
    Located,
    MissingEditList,
    MovieTimeOutOfRange,
}

public readonly record struct PlaybackIsoBmffEditSegmentLocation(
    int EntryIndex,
    PlaybackIsoBmffEditListEntry Entry,
    ulong MovieStartUnits,
    ulong MovieEndUnits,
    ulong SegmentOffsetUnits)
{
    public bool IsEmptyEdit => Entry.IsEmptyEdit;
}

public static class PlaybackIsoBmffEditSegmentLookup
{
    public static PlaybackIsoBmffEditSegmentLookupResult TryLocate(
        ulong movieTimeUnits,
        PlaybackIsoBmffEditListBox? editList,
        out PlaybackIsoBmffEditSegmentLocation? location)
    {
        location = null;
        if (editList is null)
        {
            return PlaybackIsoBmffEditSegmentLookupResult.MissingEditList;
        }

        if (movieTimeUnits >= editList.TotalSegmentDurationUnits)
        {
            return PlaybackIsoBmffEditSegmentLookupResult.MovieTimeOutOfRange;
        }

        ulong movieStartUnits = 0;
        for (var index = 0; index < editList.Entries.Count; index++)
        {
            var entry = editList.Entries[index];
            var movieEndUnits = checked(
                movieStartUnits + entry.SegmentDurationUnits);
            if (movieTimeUnits < movieEndUnits)
            {
                location = new(
                    index,
                    entry,
                    movieStartUnits,
                    movieEndUnits,
                    movieTimeUnits - movieStartUnits);
                return PlaybackIsoBmffEditSegmentLookupResult.Located;
            }

            movieStartUnits = movieEndUnits;
        }

        return PlaybackIsoBmffEditSegmentLookupResult.MovieTimeOutOfRange;
    }
}
