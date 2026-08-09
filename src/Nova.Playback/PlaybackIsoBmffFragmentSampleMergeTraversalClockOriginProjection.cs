using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjectionResult
{
    Projected,
    InvalidInput,
    SourceMismatch,
    InvalidInterval,
}

public sealed class PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginTime
{
    internal PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginTime(
        PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTime source,
        BigInteger originTick)
    {
        Source = source;
        OriginTick = originTick;
    }

    public PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTime Source
    { get; }

    public BigInteger OriginTick { get; }

    public BigInteger DecodeStartTimelineTick =>
        OriginTick + Source.DecodeStartTick;

    public BigInteger DecodeEndTimelineTickExclusive =>
        OriginTick + Source.DecodeEndTickExclusive;

    public BigInteger DecodeDurationTicks =>
        DecodeEndTimelineTickExclusive - DecodeStartTimelineTick;

    public BigInteger PresentationStartTimelineTick =>
        OriginTick + Source.PresentationStartTick;

    public BigInteger PresentationEndTimelineTickExclusive =>
        OriginTick + Source.PresentationEndTickExclusive;

    public BigInteger PresentationDurationTicks =>
        PresentationEndTimelineTickExclusive - PresentationStartTimelineTick;
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjection
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjection(
            PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjection
                source,
            BigInteger originTick,
            ReadOnlyCollection<
                PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginTime>
                samples)
    {
        Source = source;
        OriginTick = originTick;
        Samples = samples;
    }

    public
        PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjection Source
    { get; }

    public BigInteger OriginTick { get; }

    public IReadOnlyList<
        PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginTime> Samples
    { get; }

    public int SampleCount => Samples.Count;
}

public static class
    PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjector
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjectionResult
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjection?
                    source,
                BigInteger originTick,
                out
                    PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjectionResult
                    .InvalidInput;
        }

        if (source.SampleCount != source.Source.SampleCount)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjectionResult
                    .SourceMismatch;
        }

        var samples = new
            PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginTime[
                source.SampleCount];
        for (var index = 0; index < samples.Length; index++)
        {
            var time = source.Samples[index];
            if (!ReferenceEquals(time.Source, source.Source.Samples[index]))
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjectionResult
                        .SourceMismatch;
            }

            if (time.DecodeStartTick >= time.DecodeEndTickExclusive ||
                time.PresentationStartTick >=
                    time.PresentationEndTickExclusive)
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjectionResult
                        .InvalidInterval;
            }

            samples[index] = new(time, originTick);
        }

        projection = new(source, originTick, Array.AsReadOnly(samples));
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjectionResult
                .Projected;
    }
}
