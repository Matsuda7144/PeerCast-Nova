using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetTests
{
    [Fact]
    public void TryCreate_RetainsSelectionAndStartsBothExactSequences()
    {
        var selection = Selection(0, 1);

        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetCreationResult
                .Created,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetFactory
                .TryCreate(selection, out var cursorSet));
        var created = Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet>(cursorSet);
        Assert.Same(selection, created.Selection);
        AssertCursor(selection.VideoStream, created.VideoCursor);
        AssertCursor(selection.AudioStream, created.AudioCursor);
        Assert.True(created.HasVideo);
        Assert.True(created.HasAudio);
    }

    [Fact]
    public void TryCreate_AllowsEachExplicitSingleMediaSelection()
    {
        var videoSelection = Selection(0, null);
        var video = Create(videoSelection);
        AssertCursor(videoSelection.VideoStream, video.VideoCursor);
        Assert.Null(video.AudioCursor);
        Assert.True(video.HasVideo);
        Assert.False(video.HasAudio);

        var audioSelection = Selection(null, 1);
        var audio = Create(audioSelection);
        Assert.Null(audio.VideoCursor);
        AssertCursor(audioSelection.AudioStream, audio.AudioCursor);
        Assert.False(audio.HasVideo);
        Assert.True(audio.HasAudio);
    }

    [Fact]
    public void TryCreate_RejectsMissingAndEmptySelections()
    {
        AssertResult(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetCreationResult
                .MissingSelection,
            null);
        var valid = Selection(0, null);
        AssertResult(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetCreationResult
                .NoSelectedSequences,
            new(valid.SequenceSet, null, null));
    }

    [Fact]
    public void TryCreate_RejectsInvalidVideoSequenceWithoutPartialResult()
    {
        var valid = Selection(0, 1);
        var invalidVideo = InvalidStream(valid.VideoStream!);

        AssertResult(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetCreationResult
                .VideoCursorCreationFailed,
            new(valid.SequenceSet, invalidVideo, valid.AudioStream));
    }

    [Fact]
    public void TryCreate_RejectsInvalidAudioSequenceWithoutPartialResult()
    {
        var valid = Selection(0, 1);
        var invalidAudio = InvalidStream(valid.AudioStream!);

        AssertResult(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetCreationResult
                .AudioCursorCreationFailed,
            new(valid.SequenceSet, valid.VideoStream, invalidAudio));
    }

    private static void AssertCursor(
        PlaybackIsoBmffIndexedTrackCodecPacketSequence? stream,
        PlaybackIsoBmffTrackCodecPacketSequenceCursor? cursor)
    {
        var selected = Assert.IsType<
            PlaybackIsoBmffIndexedTrackCodecPacketSequence>(stream);
        var created = Assert.IsType<
            PlaybackIsoBmffTrackCodecPacketSequenceCursor>(cursor);
        Assert.Same(selected.Sequence, created.Sequence);
        Assert.Equal(0, created.NextSequenceIndex);
    }

    private static void AssertResult(
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetCreationResult
            expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceSelection? selection)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetFactory
                .TryCreate(selection, out var cursorSet));
        Assert.Null(cursorSet);
    }

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet Create(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelection selection)
    {
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetCreationResult
                .Created,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetFactory
                .TryCreate(selection, out var cursorSet));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet>(cursorSet);
    }

    private static PlaybackIsoBmffIndexedTrackCodecPacketSequence InvalidStream(
        PlaybackIsoBmffIndexedTrackCodecPacketSequence stream)
    {
        var source = stream.Sequence;
        var invalidEntry = new PlaybackIsoBmffTrackCodecPacketSequenceEntry(
            1,
            source.Entries[0].Packet);
        var invalidSequence = new PlaybackIsoBmffTrackCodecPacketSequence(
            source.MovieTiming,
            source.TrackIdentity,
            source.MediaTiming,
            source.Handler,
            new ReadOnlyCollection<
                PlaybackIsoBmffTrackCodecPacketSequenceEntry>([invalidEntry]));
        return new(stream.StreamIndex, invalidSequence);
    }

    private static PlaybackIsoBmffMovieTrackCodecPacketSequenceSelection
        Selection(int? videoStreamIndex, int? audioStreamIndex)
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
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult
                .Selected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelector.Select(
                sequenceSet,
                new(videoStreamIndex, audioStreamIndex),
                out var selection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelection>(selection);
    }
}
