using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjectionResult
{
    Projected,
    MissingSnapshot,
    EventLimitExceeded,
    InvalidTicksPerSecond,
    InvalidSequence,
    InvalidEventTime,
}

public sealed class PlaybackIsoBmffMovieCodecPacketEventQueueClockTime
{
    internal PlaybackIsoBmffMovieCodecPacketEventQueueClockTime(
        PlaybackIsoBmffMovieCodecPacketEventQueueEntry entry,
        uint ticksPerSecond)
    {
        Entry = entry;
        TicksPerSecond = ticksPerSecond;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueEntry Entry { get; }

    public uint TicksPerSecond { get; }

    public BigInteger StartTickNumerator =>
        (BigInteger)Entry.Time.StartSecondsNumerator * TicksPerSecond;

    public BigInteger EndTickNumeratorExclusive =>
        (BigInteger)Entry.Time.EndSecondsNumeratorExclusive * TicksPerSecond;

    public BigInteger DurationTickNumerator =>
        EndTickNumeratorExclusive - StartTickNumerator;

    public ulong TickDenominator => Entry.Time.SecondsDenominator;
}

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjection
{
    internal PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjection(
        PlaybackIsoBmffMovieCodecPacketEventQueueSnapshot snapshot,
        uint ticksPerSecond,
        ReadOnlyCollection<
            PlaybackIsoBmffMovieCodecPacketEventQueueClockTime> events)
    {
        Snapshot = snapshot;
        TicksPerSecond = ticksPerSecond;
        Events = events;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueSnapshot Snapshot { get; }

    public uint TicksPerSecond { get; }

    public IReadOnlyList<PlaybackIsoBmffMovieCodecPacketEventQueueClockTime>
        Events
    { get; }
}

public static class
    PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjector
{
    public static
        PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjectionResult
        Project(
            PlaybackIsoBmffMovieCodecPacketEventQueueSnapshot? snapshot,
            uint ticksPerSecond,
            out PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjection?
                projection)
    {
        projection = null;
        if (snapshot is null)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjectionResult
                    .MissingSnapshot;
        }

        if (snapshot.Count >
            PlaybackIsoBmffMovieCodecPacketEventProjector.MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjectionResult
                    .EventLimitExceeded;
        }

        if (ticksPerSecond == 0)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjectionResult
                    .InvalidTicksPerSecond;
        }

        var events = new
            PlaybackIsoBmffMovieCodecPacketEventQueueClockTime[snapshot.Count];
        for (var index = 0; index < snapshot.Count; index++)
        {
            var entry = snapshot.Entries[index];
            if (entry.SequenceIndex != index)
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjectionResult
                        .InvalidSequence;
            }

            var time = entry.Time;
            if (time.SecondsDenominator == 0 ||
                time.StartSecondsNumerator < 0 ||
                time.StartSecondsNumerator >=
                    time.EndSecondsNumeratorExclusive)
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjectionResult
                        .InvalidEventTime;
            }

            events[index] = new(entry, ticksPerSecond);
        }

        projection = new(
            snapshot,
            ticksPerSecond,
            Array.AsReadOnly(events));
        return
            PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjectionResult
                .Projected;
    }
}
