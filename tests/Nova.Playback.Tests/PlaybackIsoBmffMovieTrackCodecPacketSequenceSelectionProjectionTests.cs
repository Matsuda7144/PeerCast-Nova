using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjectionTests
{
    [Fact]
    public void Project_RetainsExactRequestedVideoAndAudioSources()
    {
        var source = SequenceSet();
        var request =
            new PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionRequest(
                0,
                1);

        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjector
                .Project(source, request, out var projection));
        var selected = Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjection>(
                projection);
        Assert.Same(source, selected.Source);
        Assert.Equal(request, selected.Request);
        Assert.Same(source.Sources[0], selected.VideoSource);
        Assert.Same(source.Sources[1], selected.AudioSource);
        Assert.Same(
            source.SequenceSet.Streams[0],
            selected.Selection.VideoStream);
        Assert.Same(
            source.SequenceSet.Streams[1],
            selected.Selection.AudioStream);
    }

    [Fact]
    public void Project_RejectsMissingSourceAndImplicitSelection()
    {
        AssertResult(
            Result.MissingSequenceSetProjection,
            null,
            new(0, null));
        AssertResult(Result.NoSequencesRequested, SequenceSet(), new(null, null));
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjection? source,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionRequest request)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjector
                .Project(source, request, out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjection
            Projection()
    {
        var source = SequenceSet();
        var request =
            new PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionRequest(
                0,
                1);
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjector
                .Project(source, request, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjection>(
                projection);
    }

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjection SequenceSet()
    {
        var movieTiming = new PlaybackIsoBmffMovieHeaderTimingBox(
            0,
            0,
            0,
            1_000,
            null);
        return PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjectionTests
            .Project(
                [
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjectionTests
                        .Sequence(
                            7,
                            PlaybackIsoBmffHandlerKind.Video,
                            movieTiming),
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjectionTests
                        .Sequence(
                            9,
                            PlaybackIsoBmffHandlerKind.Audio,
                            movieTiming),
                ]);
    }
}
