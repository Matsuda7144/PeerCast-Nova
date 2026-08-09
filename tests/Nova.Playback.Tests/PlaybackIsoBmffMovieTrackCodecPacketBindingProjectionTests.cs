using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketBindingProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffMovieTrackCodecPacketBindingProjectionTests
{
    [Fact]
    public void Bind_RetainsExactMovieAndTrackPacketSourcesInOrder()
    {
        var source = TrackBindings();
        var movieTiming = new PlaybackIsoBmffMovieHeaderTimingBox(
            0,
            0,
            0,
            1_000,
            null);

        Assert.Equal(
            Result.Bound,
            PlaybackIsoBmffMovieTrackCodecPacketBindingProjector.Bind(
                source,
                movieTiming,
                out var projection));
        var bound = Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketBindingProjection>(projection);
        Assert.Same(source, bound.Source);
        Assert.Same(movieTiming, bound.MovieTiming);
        for (var index = 0; index < bound.Entries.Count; index++)
        {
            var entry = bound.Entries[index];
            Assert.Same(source.Entries[index], entry.Source);
            Assert.Same(
                entry.Source.TrackPacket,
                entry.MovieTrackPacket.TrackPacket);
            Assert.Same(movieTiming, entry.MovieTrackPacket.MovieTiming);
        }
    }

    [Fact]
    public void Bind_RejectsMissingInputsAndInvalidMovieTimescale()
    {
        AssertResult(Result.MissingTrackBindingProjection, null, null);
        var source = TrackBindings();
        AssertResult(Result.MissingMovieTiming, source, null);
        AssertResult(
            Result.InvalidMovieTimescale,
            source,
            new(0, 0, 0, 0, null));
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffTrackCodecPacketBindingProjection? source,
        PlaybackIsoBmffMovieHeaderTimingBox? movieTiming)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketBindingProjector.Bind(
                source,
                movieTiming,
                out var projection));
        Assert.Null(projection);
    }

    internal static PlaybackIsoBmffMovieTrackCodecPacketBindingProjection
        Projection()
    {
        var source = TrackBindings();
        var movieTiming = new PlaybackIsoBmffMovieHeaderTimingBox(
            0,
            0,
            0,
            1_000,
            null);
        Assert.Equal(
            Result.Bound,
            PlaybackIsoBmffMovieTrackCodecPacketBindingProjector.Bind(
                source,
                movieTiming,
                out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketBindingProjection>(projection);
    }

    private static PlaybackIsoBmffTrackCodecPacketBindingProjection
        TrackBindings()
    {
        var (box, _) = PlaybackIsoBmffElementaryStreamDescriptorParserTests
            .CreateAudioEntry(
                PlaybackIsoBmffElementaryStreamDescriptorParserTests
                    .ElementaryStreamBox(
                        PlaybackIsoBmffElementaryStreamDescriptorParserTests
                            .BaselineElementaryStream()));
        var configurations =
            PlaybackIsoBmffTrackCodecPacketCompositionProjectionTests
                .Configurations(box, 0x736F756E);
        var packets =
            PlaybackIsoBmffTrackCodecPacketCompositionProjectionTests.Compose(
                configurations,
                null);
        Assert.Equal(
            PlaybackIsoBmffTrackCodecPacketBindingProjectionResult.Bound,
            PlaybackIsoBmffTrackCodecPacketBindingProjector.Bind(
                packets,
                PlaybackIsoBmffTrackCodecPacketTests.TrackIdentity(7),
                out var projection));
        return Assert.IsType<
            PlaybackIsoBmffTrackCodecPacketBindingProjection>(projection);
    }
}
