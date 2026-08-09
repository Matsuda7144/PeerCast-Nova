using System.Numerics;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjectionTests
{
    [Fact]
    public void Project_AppliesCallerIntervalCoveringRounding()
    {
        var source = Time(2, 1);

        var projection = Project(
            source,
            1_000,
            PlaybackIsoBmffClockTickRoundingMode.Floor,
            PlaybackIsoBmffClockTickRoundingMode.Ceiling);

        Assert.Same(source, projection.Source);
        Assert.Equal(1_000u, projection.TicksPerSecond);
        Assert.Equal(new BigInteger(21), projection.Samples[0].DecodeStartTick);
        Assert.Equal(new BigInteger(43),
            projection.Samples[0].DecodeEndTickExclusive);
        Assert.Equal(new BigInteger(10),
            projection.Samples[0].PresentationStartTick);
        Assert.Equal(new BigInteger(32),
            projection.Samples[0].PresentationEndTickExclusive);
    }

    [Fact]
    public void Project_FloorsNegativePresentationAwayFromZero()
    {
        var projection = Project(
            Time(1, 0),
            1_000,
            PlaybackIsoBmffClockTickRoundingMode.Floor,
            PlaybackIsoBmffClockTickRoundingMode.Ceiling);

        Assert.Equal(new BigInteger(-11),
            projection.Samples[0].PresentationStartTick);
        Assert.Equal(new BigInteger(11),
            projection.Samples[0].PresentationEndTickExclusive);
    }

    [Fact]
    public void Project_RequireExactAcceptsMediaTimescaleRate()
    {
        var projection = Project(
            Time(2, 1),
            48_000,
            PlaybackIsoBmffClockTickRoundingMode.RequireExact,
            PlaybackIsoBmffClockTickRoundingMode.RequireExact);

        Assert.Equal(new BigInteger(1_024),
            projection.Samples[0].DecodeStartTick);
        Assert.Equal(new BigInteger(2_048),
            projection.Samples[0].DecodeEndTickExclusive);
        Assert.Equal(new BigInteger(512),
            projection.Samples[0].PresentationStartTick);
    }

    [Fact]
    public void Project_AcceptsEmptySource()
    {
        var projection = Project(
            Time(0, 0),
            1_000,
            PlaybackIsoBmffClockTickRoundingMode.Floor,
            PlaybackIsoBmffClockTickRoundingMode.Ceiling);

        Assert.Empty(projection.Samples);
    }

    [Fact]
    public void Project_RejectsMissingInvalidRatePolicyNonIntegralAndEmpty()
    {
        var source = Time(1, 0);
        AssertResult(
            Result.InvalidInput,
            null,
            1_000,
            PlaybackIsoBmffClockTickRoundingMode.Floor,
            PlaybackIsoBmffClockTickRoundingMode.Ceiling);
        AssertResult(
            Result.InvalidTickRate,
            source,
            0,
            PlaybackIsoBmffClockTickRoundingMode.Floor,
            PlaybackIsoBmffClockTickRoundingMode.Ceiling);
        AssertResult(
            Result.InvalidRoundingMode,
            source,
            1_000,
            (PlaybackIsoBmffClockTickRoundingMode)99,
            PlaybackIsoBmffClockTickRoundingMode.Ceiling);
        AssertResult(
            Result.NonIntegralValue,
            source,
            1_000,
            PlaybackIsoBmffClockTickRoundingMode.RequireExact,
            PlaybackIsoBmffClockTickRoundingMode.Ceiling);
        AssertResult(
            Result.EmptyRoundedInterval,
            source,
            1,
            PlaybackIsoBmffClockTickRoundingMode.Floor,
            PlaybackIsoBmffClockTickRoundingMode.Floor);
    }

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjection
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjection
                    source,
                uint ticksPerSecond,
                PlaybackIsoBmffClockTickRoundingMode startRounding,
                PlaybackIsoBmffClockTickRoundingMode endRounding)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjector
                .Project(
                    source,
                    ticksPerSecond,
                    startRounding,
                    endRounding,
                    out var projection));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjection>(
                projection);
    }

    internal static PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjection
        Time(int sampleCount, int nextIndex) =>
        PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjectionTests.Project(
            PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjectionTests.View(
                sampleCount,
                nextIndex));

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjection? source,
        uint ticksPerSecond,
        PlaybackIsoBmffClockTickRoundingMode startRounding,
        PlaybackIsoBmffClockTickRoundingMode endRounding)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjector
                .Project(
                    source,
                    ticksPerSecond,
                    startRounding,
                    endRounding,
                    out var projection));
        Assert.Null(projection);
    }
}
