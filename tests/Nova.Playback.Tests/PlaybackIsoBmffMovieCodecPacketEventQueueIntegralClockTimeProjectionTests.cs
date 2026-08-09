using System.Numerics;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjectionTests
{
    [Fact]
    public void Project_AppliesCallerExplicitIntervalCoveringRounding()
    {
        var source = ClockTimeProjection(1_000);

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjectionResult
                .Projected,
            PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjector
                .Project(
                    source,
                    PlaybackIsoBmffClockTickRoundingMode.Floor,
                    PlaybackIsoBmffClockTickRoundingMode.Ceiling,
                    out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjection>(
                projection);
        Assert.Same(source, projected.Source);
        Assert.Equal(
            PlaybackIsoBmffClockTickRoundingMode.Floor,
            projected.StartRounding);
        Assert.Equal(
            PlaybackIsoBmffClockTickRoundingMode.Ceiling,
            projected.EndRounding);
        Assert.Collection(
            projected.Events,
            first =>
            {
                Assert.Same(source.Events[0], first.Time);
                Assert.Equal(BigInteger.Zero, first.StartTick);
                Assert.Equal(BigInteger.One, first.EndTickExclusive);
                Assert.Equal(BigInteger.One, first.DurationTicks);
            },
            second => Assert.Same(source.Events[1], second.Time));
    }

    [Fact]
    public void Project_RequireExactAcceptsExactlyIntegralValues()
    {
        var source = ClockTimeProjection(720_000_000);

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjectionResult
                .Projected,
            PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjector
                .Project(
                    source,
                    PlaybackIsoBmffClockTickRoundingMode.RequireExact,
                    PlaybackIsoBmffClockTickRoundingMode.RequireExact,
                    out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjection>(
                projection);
        Assert.Equal(new BigInteger(80), projected.Events[0].StartTick);
        Assert.Equal(new BigInteger(96),
            projected.Events[0].EndTickExclusive);
    }

    [Fact]
    public void Project_RejectsNonIntegralAndEmptyRoundedIntervals()
    {
        var source = ClockTimeProjection(1_000);

        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjectionResult
                .NonIntegralValue,
            source,
            PlaybackIsoBmffClockTickRoundingMode.RequireExact,
            PlaybackIsoBmffClockTickRoundingMode.Ceiling);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjectionResult
                .EmptyRoundedInterval,
            source,
            PlaybackIsoBmffClockTickRoundingMode.Floor,
            PlaybackIsoBmffClockTickRoundingMode.Floor);
    }

    [Fact]
    public void Project_RejectsMissingSourceAndInvalidRoundingMode()
    {
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjectionResult
                .MissingClockTimeProjection,
            null,
            PlaybackIsoBmffClockTickRoundingMode.Floor,
            PlaybackIsoBmffClockTickRoundingMode.Ceiling);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjectionResult
                .InvalidRoundingMode,
            ClockTimeProjection(1_000),
            (PlaybackIsoBmffClockTickRoundingMode)int.MaxValue,
            PlaybackIsoBmffClockTickRoundingMode.Ceiling);
    }

    private static void AssertResult(
        PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjectionResult
            expected,
        PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjection? source,
        PlaybackIsoBmffClockTickRoundingMode startRounding,
        PlaybackIsoBmffClockTickRoundingMode endRounding)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjector
                .Project(
                    source,
                    startRounding,
                    endRounding,
                    out var projection));
        Assert.Null(projection);
    }

    private static
        PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjection
        ClockTimeProjection(uint ticksPerSecond)
    {
        var eventProjection =
            PlaybackIsoBmffMovieCodecPacketEventOrderTests.Projection();
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventOrderingResult.Ordered,
            PlaybackIsoBmffMovieCodecPacketEventOrderer.Order(
                eventProjection,
                out var order));
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventTimeProjectionResult.Projected,
            PlaybackIsoBmffMovieCodecPacketEventTimeProjector.Project(
                order,
                out var timeProjection));
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueCreationResult.Created,
            PlaybackIsoBmffMovieCodecPacketEventQueueFactory.TryCreate(
                timeProjection,
                out var snapshot));
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjectionResult
                .Projected,
            PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjector.Project(
                snapshot,
                ticksPerSecond,
                out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjection>(
                projection);
    }
}
