using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjectionResult
{
    Projected,
    MissingIntegralClockTimeProjection,
    EventLimitExceeded,
    SourceMismatch,
    InvalidInterval,
}

public sealed class PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginTime
{
    internal PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginTime(
        PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTime time,
        BigInteger originTick)
    {
        Time = time;
        OriginTick = originTick;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTime Time
    {
        get;
    }

    public BigInteger OriginTick { get; }

    public BigInteger StartTimelineTick => OriginTick + Time.StartTick;

    public BigInteger EndTimelineTickExclusive =>
        OriginTick + Time.EndTickExclusive;

    public BigInteger DurationTicks =>
        EndTimelineTickExclusive - StartTimelineTick;
}

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjection
{
    internal PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjection(
        PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjection
            source,
        BigInteger originTick,
        ReadOnlyCollection<
            PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginTime> events)
    {
        Source = source;
        OriginTick = originTick;
        Events = events;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjection
        Source
    { get; }

    public BigInteger OriginTick { get; }

    public IReadOnlyList<
        PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginTime> Events
    {
        get;
    }
}

public static class
    PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjector
{
    public static
        PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjectionResult
        Project(
            PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjection?
                source,
            BigInteger originTick,
            out PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjection?
                projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjectionResult
                    .MissingIntegralClockTimeProjection;
        }

        if (source.Events.Count >
            PlaybackIsoBmffMovieCodecPacketEventProjector.MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjectionResult
                    .EventLimitExceeded;
        }

        if (source.Events.Count != source.Source.Events.Count)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjectionResult
                    .SourceMismatch;
        }

        var events = new
            PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginTime[
                source.Events.Count];
        for (var index = 0; index < source.Events.Count; index++)
        {
            var time = source.Events[index];
            if (!ReferenceEquals(time.Time, source.Source.Events[index]))
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjectionResult
                        .SourceMismatch;
            }

            if (time.StartTick >= time.EndTickExclusive)
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjectionResult
                        .InvalidInterval;
            }

            events[index] = new(time, originTick);
        }

        projection = new(source, originTick, Array.AsReadOnly(events));
        return
            PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjectionResult
                .Projected;
    }
}
