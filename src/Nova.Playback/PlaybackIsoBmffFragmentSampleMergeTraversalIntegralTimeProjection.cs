using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjectionResult
{
    Projected,
    InvalidInput,
    InvalidTickRate,
    InvalidRoundingMode,
    NonIntegralValue,
    EmptyRoundedInterval,
}

public sealed class PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTime
{
    internal PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTime(
        PlaybackIsoBmffFragmentSampleMergeTraversalTime source,
        BigInteger decodeStartTick,
        BigInteger decodeEndTickExclusive,
        BigInteger presentationStartTick,
        BigInteger presentationEndTickExclusive)
    {
        Source = source;
        DecodeStartTick = decodeStartTick;
        DecodeEndTickExclusive = decodeEndTickExclusive;
        PresentationStartTick = presentationStartTick;
        PresentationEndTickExclusive = presentationEndTickExclusive;
    }

    public PlaybackIsoBmffFragmentSampleMergeTraversalTime Source { get; }

    public BigInteger DecodeStartTick { get; }

    public BigInteger DecodeEndTickExclusive { get; }

    public BigInteger DecodeDurationTicks =>
        DecodeEndTickExclusive - DecodeStartTick;

    public BigInteger PresentationStartTick { get; }

    public BigInteger PresentationEndTickExclusive { get; }

    public BigInteger PresentationDurationTicks =>
        PresentationEndTickExclusive - PresentationStartTick;
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjection
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjection(
            PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjection source,
            uint ticksPerSecond,
            PlaybackIsoBmffClockTickRoundingMode startRounding,
            PlaybackIsoBmffClockTickRoundingMode endRounding,
            ReadOnlyCollection<
                PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTime>
                samples)
    {
        Source = source;
        TicksPerSecond = ticksPerSecond;
        StartRounding = startRounding;
        EndRounding = endRounding;
        Samples = samples;
    }

    public PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjection Source
    { get; }

    public uint TicksPerSecond { get; }

    public PlaybackIsoBmffClockTickRoundingMode StartRounding { get; }

    public PlaybackIsoBmffClockTickRoundingMode EndRounding { get; }

    public IReadOnlyList<
        PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTime> Samples
    { get; }

    public int SampleCount => Samples.Count;
}

public static class
    PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjector
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjectionResult
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjection?
                    source,
                uint ticksPerSecond,
                PlaybackIsoBmffClockTickRoundingMode startRounding,
                PlaybackIsoBmffClockTickRoundingMode endRounding,
                out
                    PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjectionResult
                    .InvalidInput;
        }

        if (ticksPerSecond == 0)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjectionResult
                    .InvalidTickRate;
        }

        if (!Enum.IsDefined(startRounding) || !Enum.IsDefined(endRounding))
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjectionResult
                    .InvalidRoundingMode;
        }

        var samples = new
            PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTime[
                source.SampleCount];
        for (var index = 0; index < samples.Length; index++)
        {
            var time = source.Samples[index];
            var denominator = time.SecondsDenominator;
            var duration = time.DurationSecondsNumerator;
            var decodeEnd = time.DecodeEndSecondsNumeratorExclusive;
            var presentationEnd = time.PresentationSecondsNumerator + duration;
            if (!TryRound(
                    time.DecodeSecondsNumerator * ticksPerSecond,
                    denominator,
                    startRounding,
                    out var decodeStartTick) ||
                !TryRound(
                    decodeEnd * ticksPerSecond,
                    denominator,
                    endRounding,
                    out var decodeEndTickExclusive) ||
                !TryRound(
                    time.PresentationSecondsNumerator * ticksPerSecond,
                    denominator,
                    startRounding,
                    out var presentationStartTick) ||
                !TryRound(
                    presentationEnd * ticksPerSecond,
                    denominator,
                    endRounding,
                    out var presentationEndTickExclusive))
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjectionResult
                        .NonIntegralValue;
            }

            if (decodeStartTick >= decodeEndTickExclusive ||
                presentationStartTick >= presentationEndTickExclusive)
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjectionResult
                        .EmptyRoundedInterval;
            }

            samples[index] = new(
                time,
                decodeStartTick,
                decodeEndTickExclusive,
                presentationStartTick,
                presentationEndTickExclusive);
        }

        projection = new(
            source,
            ticksPerSecond,
            startRounding,
            endRounding,
            Array.AsReadOnly(samples));
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjectionResult
                .Projected;
    }

    private static bool TryRound(
        BigInteger numerator,
        uint denominator,
        PlaybackIsoBmffClockTickRoundingMode mode,
        out BigInteger value)
    {
        var quotient = BigInteger.DivRem(
            numerator,
            denominator,
            out var remainder);
        value = mode switch
        {
            PlaybackIsoBmffClockTickRoundingMode.Floor when remainder.Sign < 0 =>
                quotient - BigInteger.One,
            PlaybackIsoBmffClockTickRoundingMode.Floor => quotient,
            PlaybackIsoBmffClockTickRoundingMode.Ceiling when remainder.Sign > 0 =>
                quotient + BigInteger.One,
            PlaybackIsoBmffClockTickRoundingMode.Ceiling => quotient,
            PlaybackIsoBmffClockTickRoundingMode.RequireExact => quotient,
            _ => BigInteger.Zero,
        };
        return mode != PlaybackIsoBmffClockTickRoundingMode.RequireExact ||
            remainder.IsZero;
    }
}
