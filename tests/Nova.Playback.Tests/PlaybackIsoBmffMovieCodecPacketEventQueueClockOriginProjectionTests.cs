using System.Numerics;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjectionTests
{
    [Fact]
    public void Project_AppliesCallerExplicitOriginWithoutChangingDuration()
    {
        var source = IntegralProjection();
        var origin = BigInteger.Pow(2, 130);

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjectionResult
                .Projected,
            PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjector
                .Project(source, origin, out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjection>(
                projection);
        Assert.Same(source, projected.Source);
        Assert.Equal(origin, projected.OriginTick);
        Assert.Collection(
            projected.Events,
            first =>
            {
                Assert.Same(source.Events[0], first.Time);
                Assert.Equal(origin, first.StartTimelineTick);
                Assert.Equal(origin + BigInteger.One,
                    first.EndTimelineTickExclusive);
                Assert.Equal(BigInteger.One, first.DurationTicks);
            },
            second => Assert.Same(source.Events[1], second.Time));
    }

    [Fact]
    public void Project_PreservesSignedCallerOrigin()
    {
        var source = IntegralProjection();

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjectionResult
                .Projected,
            PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjector
                .Project(source, -1, out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjection>(
                projection);
        Assert.Equal(-BigInteger.One,
            projected.Events[0].StartTimelineTick);
        Assert.Equal(BigInteger.Zero,
            projected.Events[0].EndTimelineTickExclusive);
    }

    [Fact]
    public void Project_RejectsMissingSource()
    {
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjectionResult
                .MissingIntegralClockTimeProjection,
            PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjector
                .Project(null, 0, out var projection));
        Assert.Null(projection);
    }

    [Fact]
    public void Project_RejectsMismatchedSourceEvents()
    {
        var source = IntegralProjection();
        var events = source.Events.Reverse().ToArray();
        var mismatched = new
            PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjection(
                source.Source,
                source.StartRounding,
                source.EndRounding,
                Array.AsReadOnly(events));

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjectionResult
                .SourceMismatch,
            PlaybackIsoBmffMovieCodecPacketEventQueueClockOriginProjector
                .Project(mismatched, 0, out var projection));
        Assert.Null(projection);
    }

    private static
        PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjection
        IntegralProjection()
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
                    out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueIntegralClockTimeProjection>(
                projection);
    }
}
