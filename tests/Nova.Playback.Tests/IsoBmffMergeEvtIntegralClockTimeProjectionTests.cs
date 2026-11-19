using System.Numerics;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjectionTests
{
    [Fact]
    public void TryProject_AppliesCallerIntervalCoveringRounding()
    {
        var source = ClockTime(1_000);

        var projection = Project(
            source,
            PlaybackIsoBmffClockTickRoundingMode.Floor,
            PlaybackIsoBmffClockTickRoundingMode.Ceiling);
        Assert.Same(source, projection.Source);
        Assert.Equal(BigInteger.Zero, projection.Events[0].StartTick);
        Assert.Equal(BigInteger.One,
            projection.Events[0].EndTickExclusive);
        Assert.Equal(BigInteger.One, projection.Events[0].DurationTicks);
    }

    [Fact]
    public void TryProject_RequireExactAcceptsIntegralValues()
    {
        var projection = Project(
            ClockTime(720_000_000),
            PlaybackIsoBmffClockTickRoundingMode.RequireExact,
            PlaybackIsoBmffClockTickRoundingMode.RequireExact);

        Assert.Equal(new BigInteger(80), projection.Events[0].StartTick);
        Assert.Equal(new BigInteger(96),
            projection.Events[0].EndTickExclusive);
    }

    [Fact]
    public void TryProject_RejectsNonIntegralEmptyAndInvalidPolicy()
    {
        var source = ClockTime(1_000);
        AssertResult(
            Result.NonIntegralValue,
            source,
            PlaybackIsoBmffClockTickRoundingMode.RequireExact,
            PlaybackIsoBmffClockTickRoundingMode.Ceiling);
        AssertResult(
            Result.EmptyRoundedInterval,
            source,
            PlaybackIsoBmffClockTickRoundingMode.Floor,
            PlaybackIsoBmffClockTickRoundingMode.Floor);
        AssertResult(
            Result.InvalidRoundingMode,
            source,
            (PlaybackIsoBmffClockTickRoundingMode)99,
            PlaybackIsoBmffClockTickRoundingMode.Ceiling);
        AssertResult(
            Result.MissingClockTimeProjection,
            null,
            PlaybackIsoBmffClockTickRoundingMode.Floor,
            PlaybackIsoBmffClockTickRoundingMode.Ceiling);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjection?
            source,
        PlaybackIsoBmffClockTickRoundingMode startRounding,
        PlaybackIsoBmffClockTickRoundingMode endRounding)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjector
                .TryProject(
                    source,
                    startRounding,
                    endRounding,
                    out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjection
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjection
                    source,
                PlaybackIsoBmffClockTickRoundingMode startRounding,
                PlaybackIsoBmffClockTickRoundingMode endRounding)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjector
                .TryProject(
                    source,
                    startRounding,
                    endRounding,
                    out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjection>(
                projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjection
            ClockTime(uint ticksPerSecond) =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjectionTests
            .Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjectionTests
                    .TimeProjection(),
                ticksPerSecond);
}
