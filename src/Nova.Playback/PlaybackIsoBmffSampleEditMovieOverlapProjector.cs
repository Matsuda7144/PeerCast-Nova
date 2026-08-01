using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffSampleEditMovieOverlapProjectionResult
{
    Projected,
    MissingOverlapSet,
    MissingEditList,
    MissingMovieTiming,
    MissingMediaTiming,
    InvalidOverlap,
}

public readonly record struct PlaybackIsoBmffSampleEditMovieOverlap(
    int EntryIndex,
    Int128 MovieStartNumerator,
    Int128 MovieEndNumeratorExclusive,
    uint Denominator,
    bool ContainsCompleteSample)
{
    public bool HasIntegralStart => MovieStartNumerator % Denominator == 0;

    public bool HasIntegralEnd =>
        MovieEndNumeratorExclusive % Denominator == 0;
}

public sealed class PlaybackIsoBmffSampleEditMovieOverlapSet
{
    internal PlaybackIsoBmffSampleEditMovieOverlapSet(
        PlaybackIsoBmffSamplePresentationTime sample,
        ReadOnlyCollection<PlaybackIsoBmffSampleEditMovieOverlap> overlaps)
    {
        Sample = sample;
        Overlaps = overlaps;
    }

    public PlaybackIsoBmffSamplePresentationTime Sample { get; }

    public IReadOnlyList<PlaybackIsoBmffSampleEditMovieOverlap> Overlaps
    {
        get;
    }
}

public static class PlaybackIsoBmffSampleEditMovieOverlapProjector
{
    public static PlaybackIsoBmffSampleEditMovieOverlapProjectionResult
        TryProject(
            PlaybackIsoBmffSampleEditOverlapSet? mediaOverlapSet,
            PlaybackIsoBmffEditListBox? editList,
            PlaybackIsoBmffMovieHeaderTimingBox? movieTiming,
            PlaybackIsoBmffMediaHeaderTimingBox? mediaTiming,
            out PlaybackIsoBmffSampleEditMovieOverlapSet? movieOverlapSet)
    {
        movieOverlapSet = null;
        if (mediaOverlapSet is null)
        {
            return PlaybackIsoBmffSampleEditMovieOverlapProjectionResult
                .MissingOverlapSet;
        }

        if (editList is null)
        {
            return PlaybackIsoBmffSampleEditMovieOverlapProjectionResult
                .MissingEditList;
        }

        if (movieTiming is null)
        {
            return PlaybackIsoBmffSampleEditMovieOverlapProjectionResult
                .MissingMovieTiming;
        }

        if (mediaTiming is null)
        {
            return PlaybackIsoBmffSampleEditMovieOverlapProjectionResult
                .MissingMediaTiming;
        }

        var sample = mediaOverlapSet.Sample;
        if (sample.EndTimeUnitsExclusive < sample.StartTimeUnits)
        {
            return PlaybackIsoBmffSampleEditMovieOverlapProjectionResult
                .InvalidOverlap;
        }

        var projected = new PlaybackIsoBmffSampleEditMovieOverlap[
            mediaOverlapSet.Overlaps.Count];
        ulong movieStartUnits = 0;
        var nextEntryIndex = 0;
        for (var overlapIndex = 0;
            overlapIndex < mediaOverlapSet.Overlaps.Count;
            overlapIndex++)
        {
            var overlap = mediaOverlapSet.Overlaps[overlapIndex];
            if (overlap.EntryIndex < nextEntryIndex ||
                overlap.EntryIndex >= editList.Entries.Count)
            {
                return PlaybackIsoBmffSampleEditMovieOverlapProjectionResult
                    .InvalidOverlap;
            }

            while (nextEntryIndex < overlap.EntryIndex)
            {
                movieStartUnits = checked(
                    movieStartUnits + editList.Entries[nextEntryIndex]
                        .SegmentDurationUnits);
                nextEntryIndex++;
            }

            var entry = editList.Entries[nextEntryIndex];
            var movieEndUnits = checked(
                movieStartUnits + entry.SegmentDurationUnits);
            if (entry.IsEmptyEdit ||
                entry.MediaRateInteger != 1 ||
                entry.MediaRateFraction != 0)
            {
                return PlaybackIsoBmffSampleEditMovieOverlapProjectionResult
                    .InvalidOverlap;
            }

            var mediaDurationNumerator =
                (Int128)entry.SegmentDurationUnits * mediaTiming.Timescale;
            if (mediaDurationNumerator % movieTiming.Timescale != 0)
            {
                return PlaybackIsoBmffSampleEditMovieOverlapProjectionResult
                    .InvalidOverlap;
            }

            var mediaStartUnits = (Int128)entry.MediaTimeUnits;
            var mediaEndUnits = mediaStartUnits +
                (mediaDurationNumerator / movieTiming.Timescale);
            var expectedCompleteSample =
                overlap.MediaStartUnits == sample.StartTimeUnits &&
                overlap.MediaEndUnitsExclusive ==
                    sample.EndTimeUnitsExclusive;
            if (overlap.MediaStartUnits < mediaStartUnits ||
                overlap.MediaEndUnitsExclusive > mediaEndUnits ||
                overlap.MediaStartUnits >= overlap.MediaEndUnitsExclusive ||
                overlap.ContainsCompleteSample != expectedCompleteSample)
            {
                return PlaybackIsoBmffSampleEditMovieOverlapProjectionResult
                    .InvalidOverlap;
            }

            var movieBaseNumerator =
                (Int128)movieStartUnits * mediaTiming.Timescale;
            projected[overlapIndex] = new(
                overlap.EntryIndex,
                movieBaseNumerator +
                    ((overlap.MediaStartUnits - mediaStartUnits) *
                        movieTiming.Timescale),
                movieBaseNumerator +
                    ((overlap.MediaEndUnitsExclusive - mediaStartUnits) *
                        movieTiming.Timescale),
                mediaTiming.Timescale,
                overlap.ContainsCompleteSample);

            movieStartUnits = movieEndUnits;
            nextEntryIndex++;
        }

        movieOverlapSet = new(sample, Array.AsReadOnly(projected));
        return PlaybackIsoBmffSampleEditMovieOverlapProjectionResult.Projected;
    }
}
