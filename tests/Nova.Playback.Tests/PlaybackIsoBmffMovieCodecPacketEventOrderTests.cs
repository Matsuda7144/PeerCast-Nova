using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffMovieCodecPacketEventOrderTests
{
    [Fact]
    public void Order_UsesExactRationalTimeAndDeterministicTies()
    {
        var videoAppearances = new[]
        {
            Appearance(0, 45_000, 54_000, 90_000),
            Appearance(1, 90_000, 99_000, 90_000),
        };
        var audioAppearances = new[]
        {
            Appearance(0, 24_000, 28_800, 48_000),
            Appearance(1, 12_000, 16_800, 48_000),
        };
        var projection = Projection(videoAppearances, audioAppearances);

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventOrderingResult.Ordered,
            PlaybackIsoBmffMovieCodecPacketEventOrderer.Order(
                projection,
                out var order));
        var ordered = Assert.IsType<PlaybackIsoBmffMovieCodecPacketEventOrder>(
            order);
        Assert.Same(projection, ordered.Projection);
        Assert.Collection(
            ordered.Events,
            first => Assert.Same(projection.AudioEvents[1], first),
            second => Assert.Same(projection.VideoEvents[0], second),
            third => Assert.Same(projection.AudioEvents[0], third),
            fourth => Assert.Same(projection.VideoEvents[1], fourth));
    }

    [Fact]
    public void Order_AvoidsInt128CrossMultiplicationOverflow()
    {
        var nearMaximum = Int128.MaxValue - 2;
        var projection = Projection(
            [Appearance(0, nearMaximum, nearMaximum + 1, 90_000)],
            [Appearance(0, nearMaximum - 1, nearMaximum, 48_000)]);

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventOrderingResult.Ordered,
            PlaybackIsoBmffMovieCodecPacketEventOrderer.Order(
                projection,
                out var order));
        var ordered = Assert.IsType<PlaybackIsoBmffMovieCodecPacketEventOrder>(
            order);
        Assert.Same(projection.VideoEvents[0], ordered.Events[0]);
        Assert.Same(projection.AudioEvents[0], ordered.Events[1]);
    }

    [Fact]
    public void Order_RejectsMissingProjectionAndInvalidEvent()
    {
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventOrderingResult
                .MissingProjection,
            PlaybackIsoBmffMovieCodecPacketEventOrderer.Order(
                null,
                out var missing));
        Assert.Null(missing);

        var projection = Projection();
        var source = projection.VideoEvents[0];
        var invalidEvent = new PlaybackIsoBmffMovieCodecPacketEvent(
            source.Stream,
            source.AppearanceIndex,
            Appearance(0, 10, 10, source.Denominator));
        var invalidProjection = new PlaybackIsoBmffMovieCodecPacketEventProjection(
            projection.Selection,
            ReadOnly([invalidEvent]),
            ReadOnly(projection.AudioEvents.ToArray()));

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventOrderingResult.InvalidEvent,
            PlaybackIsoBmffMovieCodecPacketEventOrderer.Order(
                invalidProjection,
                out var invalid));
        Assert.Null(invalid);
    }

    internal static PlaybackIsoBmffMovieCodecPacketEventProjection Projection(
        IReadOnlyList<PlaybackIsoBmffSampleEditMovieOverlap>? videoAppearances =
            null,
        IReadOnlyList<PlaybackIsoBmffSampleEditMovieOverlap>? audioAppearances =
            null)
    {
        var selection =
            PlaybackIsoBmffMovieCodecPacketEventProjectionTests.Selection(
                videoAppearances,
                audioAppearances: audioAppearances);
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventProjectionResult.Projected,
            PlaybackIsoBmffMovieCodecPacketEventProjector.Project(
                selection,
                out var projection));
        return Assert.IsType<PlaybackIsoBmffMovieCodecPacketEventProjection>(
            projection);
    }

    internal static PlaybackIsoBmffSampleEditMovieOverlap Appearance(
        int entryIndex,
        Int128 start,
        Int128 end,
        uint denominator) =>
        PlaybackIsoBmffMovieCodecPacketEventProjectionTests.Appearance(
            entryIndex,
            start,
            end,
            denominator);

    private static ReadOnlyCollection<PlaybackIsoBmffMovieCodecPacketEvent>
        ReadOnly(PlaybackIsoBmffMovieCodecPacketEvent[] events) =>
        Array.AsReadOnly(events);
}
