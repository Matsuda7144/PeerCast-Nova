using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjectionResult
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
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTime
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTime(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTime
                time,
            BigInteger startTick,
            BigInteger endTickExclusive)
    {
        Time = time;
        StartTick = startTick;
        EndTickExclusive = endTickExclusive;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTime
            Time
    { get; }

    public BigInteger StartTick { get; }

    public BigInteger EndTickExclusive { get; }

    public BigInteger DurationTicks => EndTickExclusive - StartTick;
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjection
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjection(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjection
                source,
            PlaybackIsoBmffClockTickRoundingMode startRounding,
            PlaybackIsoBmffClockTickRoundingMode endRounding,
            ReadOnlyCollection<
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTime>
                events)
    {
        Source = source;
        StartRounding = startRounding;
        EndRounding = endRounding;
        Events = events;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjection
            Source
    { get; }

    public PlaybackIsoBmffClockTickRoundingMode StartRounding { get; }

    public PlaybackIsoBmffClockTickRoundingMode EndRounding { get; }

    public IReadOnlyList<
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTime>
        Events
    { get; }

    public int EventCount => Events.Count;
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjectionResult
            TryProject(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjection?
                    source,
                PlaybackIsoBmffClockTickRoundingMode startRounding,
                PlaybackIsoBmffClockTickRoundingMode endRounding,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjection?
                    projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjectionResult
                    .MissingClockTimeProjection;
        }

        if (source.EventCount >
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjector
                .MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjectionResult
                    .EventLimitExceeded;
        }

        if (!Enum.IsDefined(startRounding) || !Enum.IsDefined(endRounding))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjectionResult
                    .InvalidRoundingMode;
        }

        var events = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTime[
                source.EventCount];
        for (var index = 0; index < source.EventCount; index++)
        {
            var time = source.Events[index];
            if (time.TickDenominator == 0 ||
                time.StartTickNumerator < BigInteger.Zero ||
                time.StartTickNumerator >= time.EndTickNumeratorExclusive)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjectionResult
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
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjectionResult
                        .NonIntegralValue;
            }

            if (startTick >= endTickExclusive)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjectionResult
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
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjectionResult
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
