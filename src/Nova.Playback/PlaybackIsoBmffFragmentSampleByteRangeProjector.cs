using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffFragmentSampleByteRangeProjectResult
{
    Projected,
    InvalidInput,
    SampleLimitExceeded,
}

public readonly record struct PlaybackIsoBmffFragmentSampleByteRange(
    int SampleIndex,
    BigInteger Offset,
    uint Length,
    BigInteger EndOffsetExclusive);

public sealed class PlaybackIsoBmffFragmentSampleByteRangeProjection
{
    internal PlaybackIsoBmffFragmentSampleByteRangeProjection(
        uint trackId,
        uint sampleDescriptionIndex,
        BigInteger runDataStart,
        BigInteger runDataEndExclusive,
        ReadOnlyCollection<PlaybackIsoBmffFragmentSampleByteRange> ranges)
    {
        TrackId = trackId;
        SampleDescriptionIndex = sampleDescriptionIndex;
        RunDataStart = runDataStart;
        RunDataEndExclusive = runDataEndExclusive;
        Ranges = ranges;
    }

    public uint TrackId { get; }

    public uint SampleDescriptionIndex { get; }

    public BigInteger RunDataStart { get; }

    public BigInteger RunDataEndExclusive { get; }

    public IReadOnlyList<PlaybackIsoBmffFragmentSampleByteRange> Ranges { get; }
}

public static class PlaybackIsoBmffFragmentSampleByteRangeProjector
{
    public static PlaybackIsoBmffFragmentSampleByteRangeProjectResult Project(
        PlaybackIsoBmffFragmentRunDataOrigin? dataOrigin,
        PlaybackIsoBmffResolvedFragmentRun? resolvedRun,
        out PlaybackIsoBmffFragmentSampleByteRangeProjection? projection)
    {
        projection = null;
        if (dataOrigin is null || resolvedRun is null)
        {
            return PlaybackIsoBmffFragmentSampleByteRangeProjectResult.InvalidInput;
        }

        if (resolvedRun.Samples.Count >
            PlaybackIsoBmffTrackRunBoxParserOptions.MaximumEntryCountHardLimit)
        {
            return
                PlaybackIsoBmffFragmentSampleByteRangeProjectResult
                    .SampleLimitExceeded;
        }

        var cursor = dataOrigin.RunDataStart;
        var ranges = new PlaybackIsoBmffFragmentSampleByteRange[
            resolvedRun.Samples.Count];
        for (var index = 0; index < ranges.Length; index++)
        {
            var length = resolvedRun.Samples[index].SampleSize;
            var end = cursor + length;
            ranges[index] = new(index, cursor, length, end);
            cursor = end;
        }

        projection = new(
            resolvedRun.TrackId,
            resolvedRun.SampleDescriptionIndex,
            dataOrigin.RunDataStart,
            cursor,
            Array.AsReadOnly(ranges));
        return PlaybackIsoBmffFragmentSampleByteRangeProjectResult.Projected;
    }
}
