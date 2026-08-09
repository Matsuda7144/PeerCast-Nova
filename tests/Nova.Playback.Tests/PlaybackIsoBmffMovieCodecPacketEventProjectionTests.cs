namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffMovieCodecPacketEventProjectionTests
{
    [Fact]
    public void Project_RetainsExactSelectionStreamsAndAppearanceOrder()
    {
        var videoAppearances = new[]
        {
            Appearance(0, 10, 12, 90_000),
            Appearance(1, 20, 22, 90_000),
        };
        var selection = Selection(videoAppearances);

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventProjectionResult.Projected,
            PlaybackIsoBmffMovieCodecPacketEventProjector.Project(
                selection,
                out var projection));
        var projected =
            Assert.IsType<PlaybackIsoBmffMovieCodecPacketEventProjection>(
                projection);
        Assert.Same(selection, projected.Selection);
        Assert.Equal(3, projected.EventCount);
        Assert.Collection(
            projected.VideoEvents,
            first =>
            {
                Assert.Same(selection.VideoStream, first.Stream);
                Assert.Equal(0, first.AppearanceIndex);
                Assert.Equal(videoAppearances[0], first.Appearance);
                Assert.Equal(0, first.StreamIndex);
                Assert.Equal(7u, first.TrackId);
                Assert.Equal(1_000u, first.MovieTimescale);
                Assert.Same(
                    selection.VideoStream!.Packet.TrackPacket.Packet,
                    first.Packet);
            },
            second =>
            {
                Assert.Equal(1, second.AppearanceIndex);
                Assert.Equal(videoAppearances[1], second.Appearance);
            });
        var audio = Assert.Single(projected.AudioEvents);
        Assert.Same(selection.AudioStream, audio.Stream);
        Assert.Equal(1, audio.StreamIndex);
        Assert.Equal(PlaybackIsoBmffHandlerKind.Audio, audio.MediaKind);
    }

    [Fact]
    public void Project_RepresentsUnselectedRoleAsEmptyEventList()
    {
        var selection = Selection(videoOnly: true);

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventProjectionResult.Projected,
            PlaybackIsoBmffMovieCodecPacketEventProjector.Project(
                selection,
                out var projection));
        var projected =
            Assert.IsType<PlaybackIsoBmffMovieCodecPacketEventProjection>(
                projection);
        Assert.Single(projected.VideoEvents);
        Assert.Empty(projected.AudioEvents);
        Assert.Equal(1, projected.EventCount);
    }

    [Fact]
    public void Project_RejectsMissingSelectionAndMismatchedStreamSource()
    {
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventProjectionResult
                .MissingSelection,
            null);
        var first = Selection();
        var second = Selection();
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventProjectionResult
                .StreamSourceMismatch,
            new(
                first.Index,
                second.VideoStream,
                first.AudioStream));
    }

    [Fact]
    public void Project_RejectsMissingInvalidAndExcessAppearances()
    {
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventProjectionResult
                .MissingAppearance,
            Selection([]));
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventProjectionResult
                .InvalidAppearance,
            Selection([Appearance(0, 12, 10, 90_000)]));
        var excess = Enumerable.Range(
                0,
                PlaybackIsoBmffMovieCodecPacketEventProjector
                    .MaximumAppearancesPerStream + 1)
            .Select(index => Appearance(index, index, index + 1, 90_000))
            .ToArray();
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventProjectionResult
                .AppearanceLimitExceeded,
            Selection(excess));
    }

    private static void AssertResult(
        PlaybackIsoBmffMovieCodecPacketEventProjectionResult expected,
        PlaybackIsoBmffMovieCodecStreamSelection? selection)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieCodecPacketEventProjector.Project(
                selection,
                out var projection));
        Assert.Null(projection);
    }

    internal static PlaybackIsoBmffMovieCodecStreamSelection Selection(
        IReadOnlyList<PlaybackIsoBmffSampleEditMovieOverlap>? videoAppearances =
            null,
        bool videoOnly = false,
        IReadOnlyList<PlaybackIsoBmffSampleEditMovieOverlap>? audioAppearances =
            null,
        PlaybackIsoBmffMovieHeaderTimingBox? movieTiming = null,
        uint? videoMediaTimescale = null,
        uint? audioMediaTimescale = null)
    {
        movieTiming ??=
            PlaybackIsoBmffMovieCodecPacketIndexTests.MovieTiming();
        var packets = new List<PlaybackIsoBmffMovieTrackCodecPacket>
        {
            PlaybackIsoBmffMovieCodecPacketIndexTests.MoviePacket(
                7,
                PlaybackIsoBmffHandlerKind.Video,
                movieTiming,
                videoAppearances,
                videoMediaTimescale),
        };
        if (!videoOnly)
        {
            packets.Add(
                PlaybackIsoBmffMovieCodecPacketIndexTests.MoviePacket(
                    9,
                    PlaybackIsoBmffHandlerKind.Audio,
                    movieTiming,
                    audioAppearances,
                    audioMediaTimescale));
        }

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketIndexingResult.Indexed,
            PlaybackIsoBmffMovieCodecPacketIndexer.TryIndex(
                packets,
                out var index));
        var indexed = Assert.IsType<PlaybackIsoBmffMovieCodecPacketIndex>(
            index);
        Assert.Equal(
            PlaybackIsoBmffMovieCodecStreamSelectionResult.Selected,
            PlaybackIsoBmffMovieCodecStreamSelector.Select(
                indexed,
                new(0, videoOnly ? null : 1),
                out var selection));
        return Assert.IsType<PlaybackIsoBmffMovieCodecStreamSelection>(
            selection);
    }

    internal static PlaybackIsoBmffSampleEditMovieOverlap Appearance(
        int entryIndex,
        Int128 start,
        Int128 end,
        uint denominator) => new(
            entryIndex,
            start,
            end,
            denominator,
            true);
}
