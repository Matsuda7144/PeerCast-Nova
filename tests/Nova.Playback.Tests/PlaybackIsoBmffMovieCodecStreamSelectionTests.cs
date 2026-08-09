namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffMovieCodecStreamSelectionTests
{
    [Fact]
    public void Select_RetainsExactIndexAndRequestedStreams()
    {
        var index = Index();

        Assert.Equal(
            PlaybackIsoBmffMovieCodecStreamSelectionResult.Selected,
            PlaybackIsoBmffMovieCodecStreamSelector.Select(
                index,
                new(0, 1),
                out var selection));
        var selected =
            Assert.IsType<PlaybackIsoBmffMovieCodecStreamSelection>(selection);
        Assert.Same(index, selected.Index);
        Assert.Same(index.Streams[0], selected.VideoStream);
        Assert.Same(index.Streams[1], selected.AudioStream);
        Assert.True(selected.HasVideo);
        Assert.True(selected.HasAudio);
    }

    [Fact]
    public void Select_AllowsExplicitSingleMediaSelection()
    {
        var index = Index();

        Assert.Equal(
            PlaybackIsoBmffMovieCodecStreamSelectionResult.Selected,
            PlaybackIsoBmffMovieCodecStreamSelector.Select(
                index,
                new(0, null),
                out var videoOnly));
        var video = Assert.IsType<PlaybackIsoBmffMovieCodecStreamSelection>(
            videoOnly);
        Assert.True(video.HasVideo);
        Assert.False(video.HasAudio);
        Assert.Equal(
            PlaybackIsoBmffMovieCodecStreamSelectionResult.Selected,
            PlaybackIsoBmffMovieCodecStreamSelector.Select(
                index,
                new(null, 1),
                out var audioOnly));
        var audio = Assert.IsType<PlaybackIsoBmffMovieCodecStreamSelection>(
            audioOnly);
        Assert.False(audio.HasVideo);
        Assert.True(audio.HasAudio);
    }

    [Fact]
    public void Select_RejectsMissingIndexAndNoRequestedStream()
    {
        AssertResult(
            PlaybackIsoBmffMovieCodecStreamSelectionResult.MissingIndex,
            null,
            new(0, null));
        AssertResult(
            PlaybackIsoBmffMovieCodecStreamSelectionResult.NoStreamsRequested,
            Index(),
            new(null, null));
    }

    [Fact]
    public void Select_RejectsInvalidAndDuplicateIndexes()
    {
        var index = Index();
        AssertResult(
            PlaybackIsoBmffMovieCodecStreamSelectionResult
                .InvalidVideoStreamIndex,
            index,
            new(-1, null));
        AssertResult(
            PlaybackIsoBmffMovieCodecStreamSelectionResult
                .InvalidAudioStreamIndex,
            index,
            new(null, index.Streams.Count));
        AssertResult(
            PlaybackIsoBmffMovieCodecStreamSelectionResult
                .DuplicateStreamIndex,
            index,
            new(0, 0));
    }

    [Fact]
    public void Select_RejectsMediaKindMismatch()
    {
        var index = Index();
        AssertResult(
            PlaybackIsoBmffMovieCodecStreamSelectionResult
                .VideoMediaKindMismatch,
            index,
            new(1, null));
        AssertResult(
            PlaybackIsoBmffMovieCodecStreamSelectionResult
                .AudioMediaKindMismatch,
            index,
            new(null, 0));
    }

    private static void AssertResult(
        PlaybackIsoBmffMovieCodecStreamSelectionResult expected,
        PlaybackIsoBmffMovieCodecPacketIndex? index,
        PlaybackIsoBmffMovieCodecStreamSelectionRequest request)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieCodecStreamSelector.Select(
                index,
                request,
                out var selection));
        Assert.Null(selection);
    }

    private static PlaybackIsoBmffMovieCodecPacketIndex Index()
    {
        var movieTiming =
            PlaybackIsoBmffMovieCodecPacketIndexTests.MovieTiming();
        var packets = new[]
        {
            PlaybackIsoBmffMovieCodecPacketIndexTests.MoviePacket(
                7,
                PlaybackIsoBmffHandlerKind.Video,
                movieTiming),
            PlaybackIsoBmffMovieCodecPacketIndexTests.MoviePacket(
                9,
                PlaybackIsoBmffHandlerKind.Audio,
                movieTiming),
        };
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketIndexingResult.Indexed,
            PlaybackIsoBmffMovieCodecPacketIndexer.TryIndex(
                packets,
                out var index));
        return Assert.IsType<PlaybackIsoBmffMovieCodecPacketIndex>(index);
    }
}
