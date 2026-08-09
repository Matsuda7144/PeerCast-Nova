using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffMovieCodecPacketEventTimeProjectionResult
{
    Projected,
    MissingOrder,
    EventLimitExceeded,
    InvalidMovieTimescale,
    MovieTimescaleMismatch,
    InvalidEvent,
}

public sealed class PlaybackIsoBmffMovieCodecPacketEventTime
{
    internal PlaybackIsoBmffMovieCodecPacketEventTime(
        PlaybackIsoBmffMovieCodecPacketEvent packetEvent,
        uint movieTimescale,
        ulong secondsDenominator)
    {
        Event = packetEvent;
        MovieTimescale = movieTimescale;
        SecondsDenominator = secondsDenominator;
    }

    public PlaybackIsoBmffMovieCodecPacketEvent Event { get; }

    public uint MovieTimescale { get; }

    public Int128 StartSecondsNumerator => Event.MovieStartNumerator;

    public Int128 EndSecondsNumeratorExclusive =>
        Event.MovieEndNumeratorExclusive;

    public Int128 DurationSecondsNumerator =>
        EndSecondsNumeratorExclusive - StartSecondsNumerator;

    public ulong SecondsDenominator { get; }
}

public sealed class PlaybackIsoBmffMovieCodecPacketEventTimeProjection
{
    internal PlaybackIsoBmffMovieCodecPacketEventTimeProjection(
        PlaybackIsoBmffMovieCodecPacketEventOrder order,
        ReadOnlyCollection<PlaybackIsoBmffMovieCodecPacketEventTime> events)
    {
        Order = order;
        Events = events;
    }

    public PlaybackIsoBmffMovieCodecPacketEventOrder Order { get; }

    public IReadOnlyList<PlaybackIsoBmffMovieCodecPacketEventTime> Events
    {
        get;
    }
}

public static class PlaybackIsoBmffMovieCodecPacketEventTimeProjector
{
    public static PlaybackIsoBmffMovieCodecPacketEventTimeProjectionResult
        Project(
            PlaybackIsoBmffMovieCodecPacketEventOrder? order,
            out PlaybackIsoBmffMovieCodecPacketEventTimeProjection? projection)
    {
        projection = null;
        if (order is null)
        {
            return PlaybackIsoBmffMovieCodecPacketEventTimeProjectionResult
                .MissingOrder;
        }

        if (order.Events.Count >
            PlaybackIsoBmffMovieCodecPacketEventProjector.MaximumEventCount)
        {
            return PlaybackIsoBmffMovieCodecPacketEventTimeProjectionResult
                .EventLimitExceeded;
        }

        var movieTimescale = order.Projection.Selection.Index.MovieTiming
            .Timescale;
        if (movieTimescale == 0)
        {
            return PlaybackIsoBmffMovieCodecPacketEventTimeProjectionResult
                .InvalidMovieTimescale;
        }

        var projected = new PlaybackIsoBmffMovieCodecPacketEventTime[
            order.Events.Count];
        for (var eventIndex = 0;
            eventIndex < order.Events.Count;
            eventIndex++)
        {
            var packetEvent = order.Events[eventIndex];
            if (packetEvent.MovieTimescale != movieTimescale)
            {
                return PlaybackIsoBmffMovieCodecPacketEventTimeProjectionResult
                    .MovieTimescaleMismatch;
            }

            if (packetEvent.Denominator == 0 ||
                packetEvent.MovieStartNumerator < 0 ||
                packetEvent.MovieStartNumerator >=
                    packetEvent.MovieEndNumeratorExclusive)
            {
                return PlaybackIsoBmffMovieCodecPacketEventTimeProjectionResult
                    .InvalidEvent;
            }

            var secondsDenominator =
                (ulong)packetEvent.Denominator * movieTimescale;
            projected[eventIndex] = new(
                packetEvent,
                movieTimescale,
                secondsDenominator);
        }

        projection = new(order, Array.AsReadOnly(projected));
        return PlaybackIsoBmffMovieCodecPacketEventTimeProjectionResult
            .Projected;
    }
}
