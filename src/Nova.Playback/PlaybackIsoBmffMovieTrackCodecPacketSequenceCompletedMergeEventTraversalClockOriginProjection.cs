using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginProjectionResult
{
    Projected,
    MissingIntegralClockTimeProjection,
    EventLimitExceeded,
    SourceMismatch,
    InvalidInterval,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginTime
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginTime(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTime
                time,
            BigInteger originTick)
    {
        Time = time;
        OriginTick = originTick;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTime
            Time
    { get; }

    public BigInteger OriginTick { get; }

    public BigInteger StartTimelineTick => OriginTick + Time.StartTick;

    public BigInteger EndTimelineTickExclusive =>
        OriginTick + Time.EndTickExclusive;

    public BigInteger DurationTicks =>
        EndTimelineTickExclusive - StartTimelineTick;
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginProjection
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginProjection(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjection
                source,
            BigInteger originTick,
            ReadOnlyCollection<
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginTime>
                events)
    {
        Source = source;
        OriginTick = originTick;
        Events = events;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjection
            Source
    { get; }

    public BigInteger OriginTick { get; }

    public IReadOnlyList<
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginTime>
        Events
    { get; }

    public int EventCount => Events.Count;
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginProjectionResult
            TryProject(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjection?
                    source,
                BigInteger originTick,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginProjection?
                    projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginProjectionResult
                    .MissingIntegralClockTimeProjection;
        }

        if (source.EventCount >
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjector
                .MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginProjectionResult
                    .EventLimitExceeded;
        }

        if (source.EventCount != source.Source.EventCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginProjectionResult
                    .SourceMismatch;
        }

        var events = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginTime[
                source.EventCount];
        for (var index = 0; index < source.EventCount; index++)
        {
            var time = source.Events[index];
            if (!ReferenceEquals(time.Time, source.Source.Events[index]))
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginProjectionResult
                        .SourceMismatch;
            }

            if (time.StartTick >= time.EndTickExclusive)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginProjectionResult
                        .InvalidInterval;
            }

            events[index] = new(time, originTick);
        }

        projection = new(source, originTick, Array.AsReadOnly(events));
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginProjectionResult
                .Projected;
    }
}
