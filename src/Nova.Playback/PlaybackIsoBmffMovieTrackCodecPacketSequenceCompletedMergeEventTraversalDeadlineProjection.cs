using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineProjectionResult
{
    Projected,
    MissingClockOriginProjection,
    EventLimitExceeded,
    SourceMismatch,
    InvalidDeadlineInterval,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadline
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadline(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginTime
                time)
    {
        Time = time;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginTime
            Time
    { get; }

    public BigInteger ReadyTick => Time.StartTimelineTick;

    public BigInteger ExpirationTickExclusive =>
        Time.EndTimelineTickExclusive;

    public BigInteger DurationTicks => ExpirationTickExclusive - ReadyTick;
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineProjection
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineProjection(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginProjection
                source,
            ReadOnlyCollection<
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadline>
                deadlines)
    {
        Source = source;
        Deadlines = deadlines;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginProjection
            Source
    { get; }

    public IReadOnlyList<
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadline>
        Deadlines
    { get; }

    public int EventCount => Deadlines.Count;
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineProjectionResult
            TryProject(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginProjection?
                    source,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineProjection?
                    projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineProjectionResult
                    .MissingClockOriginProjection;
        }

        if (source.EventCount >
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjector
                .MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineProjectionResult
                    .EventLimitExceeded;
        }

        if (source.EventCount != source.Source.EventCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineProjectionResult
                    .SourceMismatch;
        }

        var deadlines = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadline[
                source.EventCount];
        for (var index = 0; index < source.EventCount; index++)
        {
            var time = source.Events[index];
            if (!ReferenceEquals(time.Time, source.Source.Events[index]))
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineProjectionResult
                        .SourceMismatch;
            }

            if (time.StartTimelineTick >= time.EndTimelineTickExclusive)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineProjectionResult
                        .InvalidDeadlineInterval;
            }

            deadlines[index] = new(time);
        }

        projection = new(source, Array.AsReadOnly(deadlines));
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineProjectionResult
                .Projected;
    }
}
