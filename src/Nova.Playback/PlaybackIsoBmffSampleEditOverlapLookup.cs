using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffSampleEditOverlapLookupResult
{
    Found,
    NotPresented,
    InvalidConfiguration,
    MissingSamplePresentation,
    InvalidSampleInterval,
    MissingEditList,
    MissingMovieTiming,
    MissingMediaTiming,
    UnsupportedMediaRate,
    NonIntegralMediaDuration,
    OverlapLimitExceeded,
}

public sealed record PlaybackIsoBmffSampleEditOverlapLookupOptions
{
    public const int DefaultMaximumOverlapCount = 4_096;
    public const int MaximumOverlapCountHardLimit = 65_536;

    public int MaximumOverlapCount { get; init; } =
        DefaultMaximumOverlapCount;
}

public readonly record struct PlaybackIsoBmffSampleEditOverlap(
    int EntryIndex,
    Int128 MediaStartUnits,
    Int128 MediaEndUnitsExclusive,
    bool ContainsCompleteSample);

public sealed class PlaybackIsoBmffSampleEditOverlapSet
{
    internal PlaybackIsoBmffSampleEditOverlapSet(
        PlaybackIsoBmffSamplePresentationTime sample,
        ReadOnlyCollection<PlaybackIsoBmffSampleEditOverlap> overlaps)
    {
        Sample = sample;
        Overlaps = overlaps;
    }

    public PlaybackIsoBmffSamplePresentationTime Sample { get; }

    public IReadOnlyList<PlaybackIsoBmffSampleEditOverlap> Overlaps { get; }
}

public static class PlaybackIsoBmffSampleEditOverlapLookup
{
    public static PlaybackIsoBmffSampleEditOverlapLookupResult Lookup(
        PlaybackIsoBmffSamplePresentationTime? sample,
        PlaybackIsoBmffEditListBox? editList,
        PlaybackIsoBmffMovieHeaderTimingBox? movieTiming,
        PlaybackIsoBmffMediaHeaderTimingBox? mediaTiming,
        out PlaybackIsoBmffSampleEditOverlapSet? overlapSet,
        PlaybackIsoBmffSampleEditOverlapLookupOptions? options = null)
    {
        overlapSet = null;
        options ??= new PlaybackIsoBmffSampleEditOverlapLookupOptions();
        if (options.MaximumOverlapCount is < 0 or
            > PlaybackIsoBmffSampleEditOverlapLookupOptions
                .MaximumOverlapCountHardLimit)
        {
            return PlaybackIsoBmffSampleEditOverlapLookupResult
                .InvalidConfiguration;
        }

        if (sample is null)
        {
            return PlaybackIsoBmffSampleEditOverlapLookupResult
                .MissingSamplePresentation;
        }

        if (sample.Value.EndTimeUnitsExclusive < sample.Value.StartTimeUnits)
        {
            return PlaybackIsoBmffSampleEditOverlapLookupResult
                .InvalidSampleInterval;
        }

        if (editList is null)
        {
            return PlaybackIsoBmffSampleEditOverlapLookupResult.MissingEditList;
        }

        if (movieTiming is null)
        {
            return PlaybackIsoBmffSampleEditOverlapLookupResult
                .MissingMovieTiming;
        }

        if (mediaTiming is null)
        {
            return PlaybackIsoBmffSampleEditOverlapLookupResult
                .MissingMediaTiming;
        }

        var overlaps = new List<PlaybackIsoBmffSampleEditOverlap>();
        ulong movieStartUnits = 0;
        for (var index = 0; index < editList.Entries.Count; index++)
        {
            var projectionResult =
                PlaybackIsoBmffEditIntervalProjector.TryProjectAtMovieStart(
                    index,
                    movieStartUnits,
                    editList,
                    movieTiming,
                    mediaTiming,
                    out var editInterval);
            movieStartUnits = checked(
                movieStartUnits +
                editList.Entries[index].SegmentDurationUnits);
            if (projectionResult ==
                PlaybackIsoBmffEditIntervalProjectionResult.EmptyEdit)
            {
                continue;
            }

            if (projectionResult ==
                PlaybackIsoBmffEditIntervalProjectionResult
                    .UnsupportedMediaRate)
            {
                return PlaybackIsoBmffSampleEditOverlapLookupResult
                    .UnsupportedMediaRate;
            }

            if (projectionResult ==
                PlaybackIsoBmffEditIntervalProjectionResult
                    .NonIntegralMediaDuration)
            {
                return PlaybackIsoBmffSampleEditOverlapLookupResult
                    .NonIntegralMediaDuration;
            }

            if (projectionResult !=
                    PlaybackIsoBmffEditIntervalProjectionResult.Projected ||
                editInterval is null)
            {
                return PlaybackIsoBmffSampleEditOverlapLookupResult
                    .InvalidSampleInterval;
            }

            var overlapStart = Int128.Max(
                sample.Value.StartTimeUnits,
                editInterval.Value.MediaStartUnits);
            var overlapEnd = Int128.Min(
                sample.Value.EndTimeUnitsExclusive,
                editInterval.Value.MediaEndUnitsExclusive);
            if (overlapStart >= overlapEnd)
            {
                continue;
            }

            if (overlaps.Count >= options.MaximumOverlapCount)
            {
                return PlaybackIsoBmffSampleEditOverlapLookupResult
                    .OverlapLimitExceeded;
            }

            overlaps.Add(new(
                index,
                overlapStart,
                overlapEnd,
                overlapStart == sample.Value.StartTimeUnits &&
                    overlapEnd == sample.Value.EndTimeUnitsExclusive));
        }

        if (overlaps.Count == 0)
        {
            return PlaybackIsoBmffSampleEditOverlapLookupResult.NotPresented;
        }

        overlapSet = new(sample.Value, overlaps.AsReadOnly());
        return PlaybackIsoBmffSampleEditOverlapLookupResult.Found;
    }
}
