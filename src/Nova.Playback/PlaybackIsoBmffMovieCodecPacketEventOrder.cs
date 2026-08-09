using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffMovieCodecPacketEventOrderingResult
{
    Ordered,
    MissingProjection,
    EventLimitExceeded,
    InvalidEvent,
}

public sealed class PlaybackIsoBmffMovieCodecPacketEventOrder
{
    internal PlaybackIsoBmffMovieCodecPacketEventOrder(
        PlaybackIsoBmffMovieCodecPacketEventProjection projection,
        ReadOnlyCollection<PlaybackIsoBmffMovieCodecPacketEvent> events)
    {
        Projection = projection;
        Events = events;
    }

    public PlaybackIsoBmffMovieCodecPacketEventProjection Projection { get; }

    public IReadOnlyList<PlaybackIsoBmffMovieCodecPacketEvent> Events { get; }
}

public static class PlaybackIsoBmffMovieCodecPacketEventOrderer
{
    public static PlaybackIsoBmffMovieCodecPacketEventOrderingResult Order(
        PlaybackIsoBmffMovieCodecPacketEventProjection? projection,
        out PlaybackIsoBmffMovieCodecPacketEventOrder? order)
    {
        order = null;
        if (projection is null)
        {
            return PlaybackIsoBmffMovieCodecPacketEventOrderingResult
                .MissingProjection;
        }

        if (projection.EventCount >
            PlaybackIsoBmffMovieCodecPacketEventProjector.MaximumEventCount)
        {
            return PlaybackIsoBmffMovieCodecPacketEventOrderingResult
                .EventLimitExceeded;
        }

        var events = new PlaybackIsoBmffMovieCodecPacketEvent[
            projection.EventCount];
        var outputIndex = 0;
        foreach (var packetEvent in projection.VideoEvents)
        {
            if (!IsValid(packetEvent))
            {
                return PlaybackIsoBmffMovieCodecPacketEventOrderingResult
                    .InvalidEvent;
            }

            events[outputIndex++] = packetEvent;
        }

        foreach (var packetEvent in projection.AudioEvents)
        {
            if (!IsValid(packetEvent))
            {
                return PlaybackIsoBmffMovieCodecPacketEventOrderingResult
                    .InvalidEvent;
            }

            events[outputIndex++] = packetEvent;
        }

        Array.Sort(events, Compare);
        order = new(projection, Array.AsReadOnly(events));
        return PlaybackIsoBmffMovieCodecPacketEventOrderingResult.Ordered;
    }

    private static bool IsValid(
        PlaybackIsoBmffMovieCodecPacketEvent packetEvent) =>
        packetEvent.Denominator != 0 &&
        packetEvent.MovieStartNumerator >= 0 &&
        packetEvent.MovieStartNumerator <
            packetEvent.MovieEndNumeratorExclusive;

    private static int Compare(
        PlaybackIsoBmffMovieCodecPacketEvent left,
        PlaybackIsoBmffMovieCodecPacketEvent right)
    {
        var result = CompareRational(
            left.MovieStartNumerator,
            left.Denominator,
            right.MovieStartNumerator,
            right.Denominator);
        if (result != 0)
        {
            return result;
        }

        result = CompareRational(
            left.MovieEndNumeratorExclusive,
            left.Denominator,
            right.MovieEndNumeratorExclusive,
            right.Denominator);
        if (result != 0)
        {
            return result;
        }

        result = left.StreamIndex.CompareTo(right.StreamIndex);
        return result != 0
            ? result
            : left.AppearanceIndex.CompareTo(right.AppearanceIndex);
    }

    private static int CompareRational(
        Int128 leftNumerator,
        uint leftDenominator,
        Int128 rightNumerator,
        uint rightDenominator) =>
        ((BigInteger)leftNumerator * rightDenominator).CompareTo(
            (BigInteger)rightNumerator * leftDenominator);
}
