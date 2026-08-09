using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffClockTickRoundingMode
{
    Floor,
    Ceiling,
    RequireExact,
}

public enum
    PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjectionResult
{
    Projected,
    MissingClockTimeProjection,
    EventLimitExceeded,
    InvalidRoundingMode,
    NonIntegralValue,
    EmptyRoundedInterval,
    InvalidEventTime,
}

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTime
{
    internal PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTime(
        PlaybackIsoBmffMovieCodecPacketEventQueueClockTime time,
        BigInteger startTick,
        BigInteger endTickExclusive)
    {
        Time = time;
        StartTick = startTick;
        EndTickExclusive = endTickExclusive;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueClockTime Time { get; }

    public BigInteger StartTick { get; }

    public BigInteger EndTickExclusive { get; }

    public BigInteger DurationTicks => EndTickExclusive - StartTick;
}

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjection
{
    internal
        PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjection(
            PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjection source,
            PlaybackIsoBmffClockTickRoundingMode startRounding,
            PlaybackIsoBmffClockTickRoundingMode endRounding,
            ReadOnlyCollection<
                PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTime>
                events)
    {
        Source = source;
        StartRounding = startRounding;
        EndRounding = endRounding;
        Events = events;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjection Source
    {
        get;
    }

    public PlaybackIsoBmffClockTickRoundingMode StartRounding { get; }

    public PlaybackIsoBmffClockTickRoundingMode EndRounding { get; }

    public IReadOnlyList<
        PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTime> Events
    {
        get;
    }
}

public static class
    PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjector
{
    public static
        PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjectionResult
        Project(
            PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjection? source,
            PlaybackIsoBmffClockTickRoundingMode startRounding,
            PlaybackIsoBmffClockTickRoundingMode endRounding,
            out PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjection?
                projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjectionResult
                    .MissingClockTimeProjection;
        }

        if (source.Events.Count >
            PlaybackIsoBmffMovieCodecPacketEventProjector.MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjectionResult
                    .EventLimitExceeded;
        }

        if (!Enum.IsDefined(startRounding) || !Enum.IsDefined(endRounding))
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjectionResult
                    .InvalidRoundingMode;
        }

        var events = new
            PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTime[
                source.Events.Count];
        for (var index = 0; index < source.Events.Count; index++)
        {
            var time = source.Events[index];
            if (time.TickDenominator == 0 ||
                time.StartTickNumerator < BigInteger.Zero ||
                time.StartTickNumerator >= time.EndTickNumeratorExclusive)
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjectionResult
                        .InvalidEventTime;
            }

            if (!TryRound(
                    time.StartTickNumerator,
                    time.TickDenominator,
                    startRounding,
                    out var startTick) ||
                !TryRound(
                    time.EndTickNumeratorExclusive,
                    time.TickDenominator,
                    endRounding,
                    out var endTickExclusive))
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjectionResult
                        .NonIntegralValue;
            }

            if (startTick >= endTickExclusive)
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjectionResult
                        .EmptyRoundedInterval;
            }

            events[index] = new(time, startTick, endTickExclusive);
        }

        projection = new(
            source,
            startRounding,
            endRounding,
            Array.AsReadOnly(events));
        return
            PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjectionResult
                .Projected;
    }

    private static bool TryRound(
        BigInteger numerator,
        ulong denominator,
        PlaybackIsoBmffClockTickRoundingMode mode,
        out BigInteger value)
    {
        var quotient = BigInteger.DivRem(
            numerator,
            denominator,
            out var remainder);
        value = mode switch
        {
            PlaybackIsoBmffClockTickRoundingMode.Floor => quotient,
            PlaybackIsoBmffClockTickRoundingMode.Ceiling =>
                remainder.IsZero ? quotient : quotient + BigInteger.One,
            PlaybackIsoBmffClockTickRoundingMode.RequireExact => quotient,
            _ => BigInteger.Zero,
        };
        return mode != PlaybackIsoBmffClockTickRoundingMode.RequireExact ||
            remainder.IsZero;
    }
}
