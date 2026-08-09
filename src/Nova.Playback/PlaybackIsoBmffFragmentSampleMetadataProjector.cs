using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffFragmentSampleMetadataProjectResult
{
    Projected,
    InvalidInput,
    TrackIdMismatch,
    SampleDescriptionIndexMismatch,
    SampleCountMismatch,
    SampleIndexMismatch,
    SampleSizeMismatch,
}

public readonly record struct PlaybackIsoBmffFragmentSampleMetadata(
    int SampleIndex,
    uint SampleDuration,
    uint SampleSize,
    uint SampleFlags,
    BigInteger DecodeTime,
    BigInteger DecodeEndTimeExclusive,
    BigInteger PresentationTime,
    BigInteger ByteOffset,
    BigInteger ByteEndOffsetExclusive);

public sealed class PlaybackIsoBmffFragmentSampleMetadataProjection
{
    internal PlaybackIsoBmffFragmentSampleMetadataProjection(
        uint trackId,
        uint sampleDescriptionIndex,
        ReadOnlyCollection<PlaybackIsoBmffFragmentSampleMetadata> samples)
    {
        TrackId = trackId;
        SampleDescriptionIndex = sampleDescriptionIndex;
        Samples = samples;
    }

    public uint TrackId { get; }

    public uint SampleDescriptionIndex { get; }

    public IReadOnlyList<PlaybackIsoBmffFragmentSampleMetadata> Samples { get; }
}

public static class PlaybackIsoBmffFragmentSampleMetadataProjector
{
    public static PlaybackIsoBmffFragmentSampleMetadataProjectResult Project(
        PlaybackIsoBmffTimedFragmentRun? timedRun,
        PlaybackIsoBmffFragmentSampleByteRangeProjection? byteRanges,
        out PlaybackIsoBmffFragmentSampleMetadataProjection? projection)
    {
        projection = null;
        if (timedRun is null || byteRanges is null)
        {
            return PlaybackIsoBmffFragmentSampleMetadataProjectResult.InvalidInput;
        }

        if (timedRun.TrackId != byteRanges.TrackId)
        {
            return
                PlaybackIsoBmffFragmentSampleMetadataProjectResult
                    .TrackIdMismatch;
        }

        if (timedRun.SampleDescriptionIndex !=
            byteRanges.SampleDescriptionIndex)
        {
            return
                PlaybackIsoBmffFragmentSampleMetadataProjectResult
                    .SampleDescriptionIndexMismatch;
        }

        if (timedRun.Samples.Count != byteRanges.Ranges.Count)
        {
            return
                PlaybackIsoBmffFragmentSampleMetadataProjectResult
                    .SampleCountMismatch;
        }

        var samples = new PlaybackIsoBmffFragmentSampleMetadata[
            timedRun.Samples.Count];
        for (var index = 0; index < samples.Length; index++)
        {
            var timed = timedRun.Samples[index];
            var range = byteRanges.Ranges[index];
            if (timed.SampleIndex != index || range.SampleIndex != index)
            {
                return
                    PlaybackIsoBmffFragmentSampleMetadataProjectResult
                        .SampleIndexMismatch;
            }

            if (timed.SampleSize != range.Length)
            {
                return
                    PlaybackIsoBmffFragmentSampleMetadataProjectResult
                        .SampleSizeMismatch;
            }

            samples[index] = new(
                index,
                timed.SampleDuration,
                timed.SampleSize,
                timed.SampleFlags,
                timed.DecodeTime,
                timed.DecodeEndTimeExclusive,
                timed.PresentationTime,
                range.Offset,
                range.EndOffsetExclusive);
        }

        projection = new(
            timedRun.TrackId,
            timedRun.SampleDescriptionIndex,
            Array.AsReadOnly(samples));
        return PlaybackIsoBmffFragmentSampleMetadataProjectResult.Projected;
    }
}
