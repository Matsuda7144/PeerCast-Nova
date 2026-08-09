using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffMovieCodecPacketEventTimeProjectionTests
{
    [Fact]
    public void Project_RetainsExactOrderAndProjectsRationalSeconds()
    {
        var order = Order();

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventTimeProjectionResult.Projected,
            PlaybackIsoBmffMovieCodecPacketEventTimeProjector.Project(
                order,
                out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventTimeProjection>(projection);
        Assert.Same(order, projected.Order);
        Assert.Collection(
            projected.Events,
            video =>
            {
                Assert.Same(order.Events[0], video.Event);
                Assert.Equal(1_000u, video.MovieTimescale);
                Assert.Equal(10, video.StartSecondsNumerator);
                Assert.Equal(12, video.EndSecondsNumeratorExclusive);
                Assert.Equal(2, video.DurationSecondsNumerator);
                Assert.Equal(90_000_000ul, video.SecondsDenominator);
            },
            audio =>
            {
                Assert.Same(order.Events[1], audio.Event);
                Assert.Equal(48_000_000ul, audio.SecondsDenominator);
            });
    }

    [Fact]
    public void Project_PreservesMaximumUintDenominatorProduct()
    {
        var movieTiming = new PlaybackIsoBmffMovieHeaderTimingBox(
            0,
            0,
            0,
            uint.MaxValue,
            null);
        var selection =
            PlaybackIsoBmffMovieCodecPacketEventProjectionTests.Selection(
                [Appearance(0, 1, 2, uint.MaxValue)],
                videoOnly: true,
                movieTiming: movieTiming,
                videoMediaTimescale: uint.MaxValue);
        var order = Order(selection);

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventTimeProjectionResult.Projected,
            PlaybackIsoBmffMovieCodecPacketEventTimeProjector.Project(
                order,
                out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventTimeProjection>(projection);
        Assert.Equal(
            (ulong)uint.MaxValue * uint.MaxValue,
            Assert.Single(projected.Events).SecondsDenominator);
    }

    [Fact]
    public void Project_RejectsMissingOrderAndInvalidMovieTimescale()
    {
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventTimeProjectionResult
                .MissingOrder,
            null);
        var invalidMovieTiming = new PlaybackIsoBmffMovieHeaderTimingBox(
            0,
            0,
            0,
            0,
            null);
        var selection =
            PlaybackIsoBmffMovieCodecPacketEventProjectionTests.Selection(
                videoOnly: true,
                movieTiming: invalidMovieTiming);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventTimeProjectionResult
                .InvalidMovieTimescale,
            Order(selection));
    }

    [Fact]
    public void Project_RejectsMovieTimescaleMismatchAndInvalidEvent()
    {
        var order = Order();
        var foreignSelection =
            PlaybackIsoBmffMovieCodecPacketEventProjectionTests.Selection(
                videoOnly: true,
                movieTiming: new(0, 0, 0, 2_000, null));
        var foreignOrder = Order(foreignSelection);
        var mismatched = new PlaybackIsoBmffMovieCodecPacketEventOrder(
            order.Projection,
            ReadOnly([foreignOrder.Events[0]]));
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventTimeProjectionResult
                .MovieTimescaleMismatch,
            mismatched);

        var source = order.Events[0];
        var invalidEvent = new PlaybackIsoBmffMovieCodecPacketEvent(
            source.Stream,
            source.AppearanceIndex,
            Appearance(0, 10, 10, source.Denominator));
        var invalid = new PlaybackIsoBmffMovieCodecPacketEventOrder(
            order.Projection,
            ReadOnly([invalidEvent]));
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventTimeProjectionResult
                .InvalidEvent,
            invalid);
    }

    private static void AssertResult(
        PlaybackIsoBmffMovieCodecPacketEventTimeProjectionResult expected,
        PlaybackIsoBmffMovieCodecPacketEventOrder? order)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieCodecPacketEventTimeProjector.Project(
                order,
                out var projection));
        Assert.Null(projection);
    }

    private static PlaybackIsoBmffMovieCodecPacketEventOrder Order()
    {
        var projection =
            PlaybackIsoBmffMovieCodecPacketEventOrderTests.Projection();
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventOrderingResult.Ordered,
            PlaybackIsoBmffMovieCodecPacketEventOrderer.Order(
                projection,
                out var order));
        return Assert.IsType<PlaybackIsoBmffMovieCodecPacketEventOrder>(order);
    }

    private static PlaybackIsoBmffMovieCodecPacketEventOrder Order(
        PlaybackIsoBmffMovieCodecStreamSelection selection)
    {
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventProjectionResult.Projected,
            PlaybackIsoBmffMovieCodecPacketEventProjector.Project(
                selection,
                out var projection));
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventOrderingResult.Ordered,
            PlaybackIsoBmffMovieCodecPacketEventOrderer.Order(
                projection,
                out var order));
        return Assert.IsType<PlaybackIsoBmffMovieCodecPacketEventOrder>(order);
    }

    private static PlaybackIsoBmffSampleEditMovieOverlap Appearance(
        int entryIndex,
        Int128 start,
        Int128 end,
        uint denominator) =>
        PlaybackIsoBmffMovieCodecPacketEventOrderTests.Appearance(
            entryIndex,
            start,
            end,
            denominator);

    private static ReadOnlyCollection<PlaybackIsoBmffMovieCodecPacketEvent>
        ReadOnly(PlaybackIsoBmffMovieCodecPacketEvent[] events) =>
        Array.AsReadOnly(events);
}
