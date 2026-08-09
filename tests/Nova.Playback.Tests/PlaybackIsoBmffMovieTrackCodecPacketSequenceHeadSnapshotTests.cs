namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshotTests
{
    [Fact]
    public void TryProject_RetainsExactCursorSetAndCurrentHeads()
    {
        var cursorSet = CursorSet(0, 1);

        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshotProjectionResult
                .Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshotProjector
                .TryProject(cursorSet, out var snapshot));
        var projected = Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshot>(
                snapshot);
        Assert.Same(cursorSet, projected.CursorSet);
        Assert.Same(cursorSet.VideoCursor!.Sequence.Entries[0],
            projected.VideoHead);
        Assert.Same(cursorSet.AudioCursor!.Sequence.Entries[0],
            projected.AudioHead);
        Assert.Equal(0, cursorSet.VideoCursor.NextSequenceIndex);
        Assert.Equal(0, cursorSet.AudioCursor.NextSequenceIndex);
        Assert.True(projected.HasVideoHead);
        Assert.True(projected.HasAudioHead);
        Assert.False(projected.IsExhausted);
    }

    [Fact]
    public void TryProject_PreservesExplicitSingleMediaAbsence()
    {
        var videoSet = CursorSet(0, null);
        var video = Project(videoSet);
        Assert.Same(videoSet.VideoCursor!.Sequence.Entries[0],
            video.VideoHead);
        Assert.Null(video.AudioHead);

        var audioSet = CursorSet(null, 1);
        var audio = Project(audioSet);
        Assert.Null(audio.VideoHead);
        Assert.Same(audioSet.AudioCursor!.Sequence.Entries[0],
            audio.AudioHead);
    }

    [Fact]
    public void TryProject_RepresentsRoleAndWholeSetExhaustion()
    {
        var source = CursorSet(0, 1);
        var videoExhausted = Exhausted(source.VideoCursor!);
        var partialSet = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet(
                source.Selection,
                videoExhausted,
                source.AudioCursor);
        var partial = Project(partialSet);
        Assert.Null(partial.VideoHead);
        Assert.Same(source.AudioCursor!.Sequence.Entries[0],
            partial.AudioHead);
        Assert.False(partial.IsExhausted);

        var exhaustedSet = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet(
                source.Selection,
                videoExhausted,
                Exhausted(source.AudioCursor));
        var exhausted = Project(exhaustedSet);
        Assert.Null(exhausted.VideoHead);
        Assert.Null(exhausted.AudioHead);
        Assert.True(exhausted.IsExhausted);
    }

    [Fact]
    public void TryProject_RejectsMissingAndEmptyCursorSets()
    {
        AssertResult(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshotProjectionResult
                .MissingCursorSet,
            null);
        var source = CursorSet(0, null);
        var emptySelection = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelection(
                source.Selection.SequenceSet,
                null,
                null);
        AssertResult(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshotProjectionResult
                .NoSelectedCursors,
            new(emptySelection, null, null));
    }

    [Fact]
    public void TryProject_RejectsInvalidOrForeignRoleCursors()
    {
        var source = CursorSet(0, 1);
        var invalidVideo = new PlaybackIsoBmffTrackCodecPacketSequenceCursor(
            source.VideoCursor!.Sequence,
            source.VideoCursor.Sequence.Entries.Count + 1);
        AssertResult(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshotProjectionResult
                .InvalidVideoCursor,
            new(source.Selection, invalidVideo, source.AudioCursor));

        AssertResult(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshotProjectionResult
                .InvalidAudioCursor,
            new(source.Selection, source.VideoCursor, source.VideoCursor));
    }

    private static PlaybackIsoBmffTrackCodecPacketSequenceCursor Exhausted(
        PlaybackIsoBmffTrackCodecPacketSequenceCursor cursor) =>
        new(cursor.Sequence, cursor.Sequence.Entries.Count);

    private static void AssertResult(
        PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshotProjectionResult
            expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet? cursorSet)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshotProjector
                .TryProject(cursorSet, out var snapshot));
        Assert.Null(snapshot);
    }

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshot Project(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet cursorSet)
    {
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshotProjectionResult
                .Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshotProjector
                .TryProject(cursorSet, out var snapshot));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshot>(snapshot);
    }

    private static PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet
        CursorSet(int? videoStreamIndex, int? audioStreamIndex)
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
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetCreationResult
                .Created,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetFactory
                .TryCreate(selection, out var cursorSet));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet>(cursorSet);
    }
}
