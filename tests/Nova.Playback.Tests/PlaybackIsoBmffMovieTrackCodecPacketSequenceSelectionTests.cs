namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionTests
{
    [Fact]
    public void Select_RetainsExactSetAndRequestedSequences()
    {
        var sequenceSet = SequenceSet();

        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult
                .Selected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelector.Select(
                sequenceSet,
                new(0, 1),
                out var selection));
        var selected = Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelection>(selection);
        Assert.Same(sequenceSet, selected.SequenceSet);
        Assert.Same(sequenceSet.Streams[0], selected.VideoStream);
        Assert.Same(sequenceSet.Streams[1], selected.AudioStream);
        Assert.True(selected.HasVideo);
        Assert.True(selected.HasAudio);
    }

    [Fact]
    public void Select_AllowsExplicitSingleMediaSelection()
    {
        var sequenceSet = SequenceSet();

        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult
                .Selected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelector.Select(
                sequenceSet,
                new(0, null),
                out var videoOnly));
        var video = Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelection>(videoOnly);
        Assert.True(video.HasVideo);
        Assert.False(video.HasAudio);

        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult
                .Selected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelector.Select(
                sequenceSet,
                new(null, 1),
                out var audioOnly));
        var audio = Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelection>(audioOnly);
        Assert.False(audio.HasVideo);
        Assert.True(audio.HasAudio);
    }

    [Fact]
    public void Select_RejectsMissingSetAndNoRequestedSequence()
    {
        AssertResult(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult
                .MissingSequenceSet,
            null,
            new(0, null));
        AssertResult(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult
                .NoSequencesRequested,
            SequenceSet(),
            new(null, null));
    }

    [Fact]
    public void Select_RejectsInvalidAndDuplicateIndexes()
    {
        var sequenceSet = SequenceSet();

        AssertResult(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult
                .InvalidVideoStreamIndex,
            sequenceSet,
            new(-1, null));
        AssertResult(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult
                .InvalidAudioStreamIndex,
            sequenceSet,
            new(null, sequenceSet.Streams.Count));
        AssertResult(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult
                .DuplicateStreamIndex,
            sequenceSet,
            new(0, 0));
    }

    [Fact]
    public void Select_RejectsMediaKindMismatch()
    {
        var sequenceSet = SequenceSet();

        AssertResult(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult
                .VideoMediaKindMismatch,
            sequenceSet,
            new(1, null));
        AssertResult(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult
                .AudioMediaKindMismatch,
            sequenceSet,
            new(null, 0));
    }

    private static void AssertResult(
        PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceSet? sequenceSet,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionRequest request)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelector.Select(
                sequenceSet,
                request,
                out var selection));
        Assert.Null(selection);
    }

    private static PlaybackIsoBmffMovieTrackCodecPacketSequenceSet
        SequenceSet()
    {
        var movieTiming =
            PlaybackIsoBmffMovieCodecPacketIndexTests.MovieTiming();
        var sequences = new[]
        {
            PlaybackIsoBmffTrackCodecPacketSequenceTests.Sequence(
                7,
                PlaybackIsoBmffHandlerKind.Video,
                movieTiming),
            PlaybackIsoBmffTrackCodecPacketSequenceTests.Sequence(
                9,
                PlaybackIsoBmffHandlerKind.Audio,
                movieTiming),
        };
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSetCreationResult
                .Created,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSetFactory.TryCreate(
                sequences,
                out var sequenceSet));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSet>(sequenceSet);
    }
}
