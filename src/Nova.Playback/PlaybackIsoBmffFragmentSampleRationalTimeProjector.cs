using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffFragmentSampleRationalTimeProjectResult
{
    Projected,
    InvalidInput,
    InvalidMediaTimescale,
}

public readonly record struct PlaybackIsoBmffFragmentSampleRationalTime(
    int SampleIndex,
    uint MediaTimescale,
    BigInteger DecodeSecondsNumerator,
    BigInteger DecodeEndSecondsNumeratorExclusive,
    BigInteger PresentationSecondsNumerator,
    BigInteger DurationSecondsNumerator,
    uint SampleSize,
    uint SampleFlags,
    BigInteger ByteOffset,
    BigInteger ByteEndOffsetExclusive);

public sealed class PlaybackIsoBmffFragmentSampleRationalTimeProjection
{
    internal PlaybackIsoBmffFragmentSampleRationalTimeProjection(
        uint trackId,
        uint sampleDescriptionIndex,
        uint mediaTimescale,
        ReadOnlyCollection<PlaybackIsoBmffFragmentSampleRationalTime> samples)
    {
        TrackId = trackId;
        SampleDescriptionIndex = sampleDescriptionIndex;
        MediaTimescale = mediaTimescale;
        Samples = samples;
    }

    public uint TrackId { get; }

    public uint SampleDescriptionIndex { get; }

    public uint MediaTimescale { get; }

    public IReadOnlyList<PlaybackIsoBmffFragmentSampleRationalTime> Samples
    { get; }
}

public static class PlaybackIsoBmffFragmentSampleRationalTimeProjector
{
    public static PlaybackIsoBmffFragmentSampleRationalTimeProjectResult Project(
        PlaybackIsoBmffFragmentSampleMetadataProjection? metadata,
        uint mediaTimescale,
        out PlaybackIsoBmffFragmentSampleRationalTimeProjection? projection)
    {
        projection = null;
        if (metadata is null)
        {
            return PlaybackIsoBmffFragmentSampleRationalTimeProjectResult.InvalidInput;
        }

        if (mediaTimescale == 0)
        {
            return
                PlaybackIsoBmffFragmentSampleRationalTimeProjectResult
                    .InvalidMediaTimescale;
        }

        var samples = new PlaybackIsoBmffFragmentSampleRationalTime[
            metadata.Samples.Count];
        for (var index = 0; index < samples.Length; index++)
        {
            var sample = metadata.Samples[index];
            samples[index] = new(
                sample.SampleIndex,
                mediaTimescale,
                sample.DecodeTime,
                sample.DecodeEndTimeExclusive,
                sample.PresentationTime,
                sample.SampleDuration,
                sample.SampleSize,
                sample.SampleFlags,
                sample.ByteOffset,
                sample.ByteEndOffsetExclusive);
        }

        projection = new(
            metadata.TrackId,
            metadata.SampleDescriptionIndex,
            mediaTimescale,
            Array.AsReadOnly(samples));
        return PlaybackIsoBmffFragmentSampleRationalTimeProjectResult.Projected;
    }
}
