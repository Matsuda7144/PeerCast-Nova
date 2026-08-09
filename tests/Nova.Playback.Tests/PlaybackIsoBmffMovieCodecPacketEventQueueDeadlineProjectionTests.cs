using System.Numerics;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjectionTests
{
    [Fact]
    public void Project_RetainsExactTimelineIntervalsAsDeadlines()
    {
        var source = ClockOriginProjection(100);

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjectionResult
                .Projected,
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjector.Project(
                source,
                out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjection>(
                projection);
        Assert.Same(source, projected.Source);
        Assert.Collection(
            projected.Deadlines,
            first =>
            {
                Assert.Same(source.Events[0], first.Time);
                Assert.Equal(new BigInteger(100), first.ReadyTick);
                Assert.Equal(new BigInteger(101),
                    first.ExpirationTickExclusive);
                Assert.Equal(BigInteger.One, first.DurationTicks);
            },
            second => Assert.Same(source.Events[1], second.Time));
    }

    [Fact]
    public void Project_PreservesSignedDeadlineTicks()
    {
        var source = ClockOriginProjection(-10);

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjectionResult
                .Projected,
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjector.Project(
                source,
                out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjection>(
                projection);
        Assert.Equal(new BigInteger(-10), projected.Deadlines[0].ReadyTick);
        Assert.Equal(new BigInteger(-9),
            projected.Deadlines[0].ExpirationTickExclusive);
    }

    [Fact]
    public void Project_RejectsMissingSource()
    {
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjectionResult
                .MissingClockOriginProjection,
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjector.Project(
                null,
                out var projection));
        Assert.Null(projection);
    }

    [Fact]
    public void Project_RejectsMismatchedSourceEvents()
    {
        var source = ClockOriginProjection(0);
        var events = source.Events.Reverse().ToArray();
        var mismatched = new
            PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjection(
                source.Source,
                source.OriginTick,
                Array.AsReadOnly(events));

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjectionResult
                .SourceMismatch,
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjector.Project(
                mismatched,
                out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjection
        ClockOriginProjection(BigInteger originTick)
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
                1_000,
                out var clockTime));
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjectionResult
                .Projected,
            PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjector
                .Project(
                    clockTime,
                    PlaybackIsoBmffClockTickRoundingMode.Floor,
                    PlaybackIsoBmffClockTickRoundingMode.Ceiling,
                    out var integralTime));
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjectionResult
                .Projected,
            PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjector
                .Project(integralTime, originTick, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjection>(
                projection);
    }
}
