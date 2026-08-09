using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffMovieCodecPacketEventQueueCreationResult
{
    Created,
    MissingProjection,
    EventLimitExceeded,
    OrderMismatch,
    InvalidEventTime,
}

public sealed class PlaybackIsoBmffMovieCodecPacketEventQueueEntry
{
    internal PlaybackIsoBmffMovieCodecPacketEventQueueEntry(
        int sequenceIndex,
        PlaybackIsoBmffMovieCodecPacketEventTime time)
    {
        SequenceIndex = sequenceIndex;
        Time = time;
    }

    public int SequenceIndex { get; }

    public PlaybackIsoBmffMovieCodecPacketEventTime Time { get; }

    public PlaybackIsoBmffMovieCodecPacketEvent Event => Time.Event;
}

public sealed class PlaybackIsoBmffMovieCodecPacketEventQueueSnapshot
{
    internal PlaybackIsoBmffMovieCodecPacketEventQueueSnapshot(
        PlaybackIsoBmffMovieCodecPacketEventTimeProjection projection,
        ReadOnlyCollection<PlaybackIsoBmffMovieCodecPacketEventQueueEntry>
            entries)
    {
        Projection = projection;
        Entries = entries;
    }

    public PlaybackIsoBmffMovieCodecPacketEventTimeProjection Projection
    {
        get;
    }

    public IReadOnlyList<PlaybackIsoBmffMovieCodecPacketEventQueueEntry> Entries
    {
        get;
    }

    public int Count => Entries.Count;
}

public static class PlaybackIsoBmffMovieCodecPacketEventQueueFactory
{
    public static PlaybackIsoBmffMovieCodecPacketEventQueueCreationResult
        TryCreate(
            PlaybackIsoBmffMovieCodecPacketEventTimeProjection? projection,
            out PlaybackIsoBmffMovieCodecPacketEventQueueSnapshot? queue)
    {
        queue = null;
        if (projection is null)
        {
            return PlaybackIsoBmffMovieCodecPacketEventQueueCreationResult
                .MissingProjection;
        }

        if (projection.Events.Count >
            PlaybackIsoBmffMovieCodecPacketEventProjector.MaximumEventCount)
        {
            return PlaybackIsoBmffMovieCodecPacketEventQueueCreationResult
                .EventLimitExceeded;
        }

        var orderedEvents = projection.Order.Events;
        if (projection.Events.Count != orderedEvents.Count)
        {
            return PlaybackIsoBmffMovieCodecPacketEventQueueCreationResult
                .OrderMismatch;
        }

        var movieTimescale = projection.Order.Projection.Selection.Index
            .MovieTiming.Timescale;
        var entries = new PlaybackIsoBmffMovieCodecPacketEventQueueEntry[
            projection.Events.Count];
        for (var eventIndex = 0;
            eventIndex < projection.Events.Count;
            eventIndex++)
        {
            var time = projection.Events[eventIndex];
            var packetEvent = time.Event;
            if (!ReferenceEquals(packetEvent, orderedEvents[eventIndex]))
            {
                return PlaybackIsoBmffMovieCodecPacketEventQueueCreationResult
                    .OrderMismatch;
            }

            if (movieTimescale == 0 ||
                time.MovieTimescale != movieTimescale ||
                packetEvent.Denominator == 0 ||
                packetEvent.MovieStartNumerator < 0 ||
                packetEvent.MovieStartNumerator >=
                    packetEvent.MovieEndNumeratorExclusive ||
                time.SecondsDenominator !=
                    (ulong)packetEvent.Denominator * movieTimescale)
            {
                return PlaybackIsoBmffMovieCodecPacketEventQueueCreationResult
                    .InvalidEventTime;
            }

            entries[eventIndex] = new(eventIndex, time);
        }

        queue = new(projection, Array.AsReadOnly(entries));
        return PlaybackIsoBmffMovieCodecPacketEventQueueCreationResult.Created;
    }
}
