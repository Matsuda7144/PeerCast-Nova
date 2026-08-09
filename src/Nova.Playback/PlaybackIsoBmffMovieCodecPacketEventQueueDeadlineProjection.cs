using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjectionResult
{
    Projected,
    MissingClockOriginProjection,
    EventLimitExceeded,
    SourceMismatch,
    InvalidDeadlineInterval,
}

public sealed class PlaybackIsoBmffMovieCodecPacketEventQueueDeadline
{
    internal PlaybackIsoBmffMovieCodecPacketEventQueueDeadline(
        PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginTime time)
    {
        Time = time;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginTime Time
    {
        get;
    }

    public BigInteger ReadyTick => Time.StartTimelineTick;

    public BigInteger ExpirationTickExclusive =>
        Time.EndTimelineTickExclusive;

    public BigInteger DurationTicks =>
        ExpirationTickExclusive - ReadyTick;
}

public sealed class PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjection
{
    internal PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjection(
        PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjection source,
        ReadOnlyCollection<PlaybackIsoBmffMovieCodecPacketEventQueueDeadline>
            deadlines)
    {
        Source = source;
        Deadlines = deadlines;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjection Source
    {
        get;
    }

    public IReadOnlyList<PlaybackIsoBmffMovieCodecPacketEventQueueDeadline>
        Deadlines
    { get; }
}

public static class PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjector
{
    public static
        PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjectionResult
        Project(
            PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjection?
                source,
            out PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjection?
                projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjectionResult
                    .MissingClockOriginProjection;
        }

        if (source.Events.Count >
            PlaybackIsoBmffMovieCodecPacketEventProjector.MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjectionResult
                    .EventLimitExceeded;
        }

        if (source.Events.Count != source.Source.Events.Count)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjectionResult
                    .SourceMismatch;
        }

        var deadlines = new PlaybackIsoBmffMovieCodecPacketEventQueueDeadline[
            source.Events.Count];
        for (var index = 0; index < source.Events.Count; index++)
        {
            var time = source.Events[index];
            if (!ReferenceEquals(time.Time, source.Source.Events[index]))
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjectionResult
                        .SourceMismatch;
            }

            if (time.StartTimelineTick >= time.EndTimelineTickExclusive)
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjectionResult
                        .InvalidDeadlineInterval;
            }

            deadlines[index] = new(time);
        }

        projection = new(source, Array.AsReadOnly(deadlines));
        return PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjectionResult
            .Projected;
    }
}
