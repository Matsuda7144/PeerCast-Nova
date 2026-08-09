using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjectionResult
{
    Projected,
    InvalidInput,
    SourceMismatch,
    InvalidDeadlineInterval,
}

public sealed class PlaybackIsoBmffFragmentSampleMergeTraversalDeadline
{
    internal PlaybackIsoBmffFragmentSampleMergeTraversalDeadline(
        PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginTime source)
    {
        Source = source;
    }

    public PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginTime Source
    { get; }

    public BigInteger DecodeReadyTick => Source.DecodeStartTimelineTick;

    public BigInteger DecodeExpirationTickExclusive =>
        Source.DecodeEndTimelineTickExclusive;

    public BigInteger DecodeDurationTicks =>
        DecodeExpirationTickExclusive - DecodeReadyTick;

    public BigInteger PresentationReadyTick =>
        Source.PresentationStartTimelineTick;

    public BigInteger PresentationExpirationTickExclusive =>
        Source.PresentationEndTimelineTickExclusive;

    public BigInteger PresentationDurationTicks =>
        PresentationExpirationTickExclusive - PresentationReadyTick;
}

public sealed class PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjection
{
    internal PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjection(
        PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjection source,
        ReadOnlyCollection<
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadline> deadlines)
    {
        Source = source;
        Deadlines = deadlines;
    }

    public PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjection
        Source
    { get; }

    public IReadOnlyList<PlaybackIsoBmffFragmentSampleMergeTraversalDeadline>
        Deadlines
    { get; }

    public int SampleCount => Deadlines.Count;
}

public static class PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjector
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjectionResult
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjection?
                    source,
                out PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjection?
                    projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjectionResult
                    .InvalidInput;
        }

        if (source.SampleCount != source.Source.SampleCount)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjectionResult
                    .SourceMismatch;
        }

        var deadlines = new PlaybackIsoBmffFragmentSampleMergeTraversalDeadline[
            source.SampleCount];
        for (var index = 0; index < deadlines.Length; index++)
        {
            var time = source.Samples[index];
            if (!ReferenceEquals(time.Source, source.Source.Samples[index]))
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjectionResult
                        .SourceMismatch;
            }

            if (time.DecodeStartTimelineTick >=
                    time.DecodeEndTimelineTickExclusive ||
                time.PresentationStartTimelineTick >=
                    time.PresentationEndTimelineTickExclusive)
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjectionResult
                        .InvalidDeadlineInterval;
            }

            deadlines[index] = new(time);
        }

        projection = new(source, Array.AsReadOnly(deadlines));
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjectionResult
                .Projected;
    }
}
