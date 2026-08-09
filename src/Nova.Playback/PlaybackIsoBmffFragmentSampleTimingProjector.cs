using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffFragmentSampleTimingProjectResult
{
    Projected,
    InvalidInput,
    SampleLimitExceeded,
}

public readonly record struct PlaybackIsoBmffTimedFragmentSample(
    int SampleIndex,
    uint SampleDuration,
    uint SampleSize,
    uint SampleFlags,
    BigInteger DecodeTime,
    BigInteger DecodeEndTimeExclusive,
    BigInteger PresentationTime);

public sealed class PlaybackIsoBmffTimedFragmentRun
{
    internal PlaybackIsoBmffTimedFragmentRun(
        uint trackId,
        uint sampleDescriptionIndex,
        BigInteger baseMediaDecodeTime,
        BigInteger nextDecodeTime,
        ReadOnlyCollection<PlaybackIsoBmffTimedFragmentSample> samples)
    {
        TrackId = trackId;
        SampleDescriptionIndex = sampleDescriptionIndex;
        BaseMediaDecodeTime = baseMediaDecodeTime;
        NextDecodeTime = nextDecodeTime;
        Samples = samples;
    }

    public uint TrackId { get; }

    public uint SampleDescriptionIndex { get; }

    public BigInteger BaseMediaDecodeTime { get; }

    public BigInteger NextDecodeTime { get; }

    public IReadOnlyList<PlaybackIsoBmffTimedFragmentSample> Samples { get; }
}

public static class PlaybackIsoBmffFragmentSampleTimingProjector
{
    public static PlaybackIsoBmffFragmentSampleTimingProjectResult Project(
        PlaybackIsoBmffTrackFragmentDecodeTimeBox? decodeTime,
        PlaybackIsoBmffResolvedFragmentRun? resolvedRun,
        out PlaybackIsoBmffTimedFragmentRun? timedRun)
    {
        timedRun = null;
        if (decodeTime is null || resolvedRun is null)
        {
            return PlaybackIsoBmffFragmentSampleTimingProjectResult.InvalidInput;
        }

        if (resolvedRun.Samples.Count >
            PlaybackIsoBmffTrackRunBoxParserOptions.MaximumEntryCountHardLimit)
        {
            return
                PlaybackIsoBmffFragmentSampleTimingProjectResult
                    .SampleLimitExceeded;
        }

        var cursor = new BigInteger(decodeTime.BaseMediaDecodeTime);
        var samples = new PlaybackIsoBmffTimedFragmentSample[
            resolvedRun.Samples.Count];
        for (var index = 0; index < samples.Length; index++)
        {
            var sample = resolvedRun.Samples[index];
            var end = cursor + sample.SampleDuration;
            var presentation = cursor +
                (sample.SampleCompositionTimeOffset ?? 0);
            samples[index] = new(
                index,
                sample.SampleDuration,
                sample.SampleSize,
                sample.SampleFlags,
                cursor,
                end,
                presentation);
            cursor = end;
        }

        timedRun = new(
            resolvedRun.TrackId,
            resolvedRun.SampleDescriptionIndex,
            decodeTime.BaseMediaDecodeTime,
            cursor,
            Array.AsReadOnly(samples));
        return PlaybackIsoBmffFragmentSampleTimingProjectResult.Projected;
    }
}
