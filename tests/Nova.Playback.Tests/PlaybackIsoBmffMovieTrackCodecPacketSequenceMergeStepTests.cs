using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepProjectionResult;
using Role = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole;
using TieBreak = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepTests
{
    [Fact]
    public void TryProject_RetainsExactVideoAdvanceAndMetadata()
    {
        var advance = Advance(
            Appearance(12_000, 16_800, 90_000),
            Appearance(24_000, 28_800, 48_000));

        var step = Project(advance, 7);
        Assert.Equal(7, step.MergeIndex);
        Assert.Same(advance, step.Advance);
        Assert.Equal(Role.Video, step.Role);
        Assert.Same(advance.SourceCursorSet.Selection.VideoStream,
            step.Stream);
        Assert.Equal(step.Stream.StreamIndex, step.StreamIndex);
        Assert.Equal(step.Stream.TrackId, step.TrackId);
        Assert.Same(advance.Entry, step.Entry);
        Assert.Equal(advance.Entry.SequenceIndex, step.SequenceIndex);
        Assert.Equal(advance.Entry.SampleNumber, step.SampleNumber);
        Assert.Same(advance.Entry.Packet, step.Packet);
        Assert.Equal(advance.RoleSelection.LeadingAppearance,
            step.LeadingAppearance);
        Assert.Same(advance.SuccessorCursorSet, step.SuccessorCursorSet);
    }

    [Fact]
    public void TryProject_RetainsExactAudioAdvanceAndMetadata()
    {
        var advance = Advance(
            Appearance(45_000, 54_000, 90_000),
            Appearance(12_000, 16_800, 48_000));

        var step = Project(advance, 0);
        Assert.Equal(Role.Audio, step.Role);
        Assert.Same(advance.SourceCursorSet.Selection.AudioStream,
            step.Stream);
        Assert.Same(advance.Entry, step.Entry);
    }

    [Fact]
    public void TryProject_AcceptsExactMaximumMergeIndex()
    {
        var advance = Advance(
            Appearance(10, 12, 90_000),
            Appearance(20, 22, 48_000));

        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepProjector
                .MaximumMergeStepCount - 1,
            Project(
                advance,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepProjector
                    .MaximumMergeStepCount - 1).MergeIndex);
    }

    [Fact]
    public void TryProject_RejectsMissingAdvanceAndInvalidIndexes()
    {
        AssertResult(Result.MissingAdvance, null, 0);
        var advance = Advance(
            Appearance(10, 12, 90_000),
            Appearance(20, 22, 48_000));
        AssertResult(Result.InvalidMergeIndex, advance, -1);
        AssertResult(
            Result.InvalidMergeIndex,
            advance,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepProjector
                .MaximumMergeStepCount);
    }

    [Fact]
    public void TryProject_RejectsAdvanceWithChangedNonSelectedCursor()
    {
        var valid = Advance(
            Appearance(10, 12, 90_000),
            Appearance(20, 22, 48_000));
        var invalidSuccessor = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet(
                valid.SuccessorCursorSet.Selection,
                valid.SuccessorCursorSet.VideoCursor,
                new PlaybackIsoBmffTrackCodecPacketSequenceCursor(
                    valid.SourceCursorSet.AudioCursor!.Sequence,
                    1));
        var invalid = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvance(
                valid.RoleSelection,
                valid.Transition,
                invalidSuccessor);

        AssertResult(Result.InvalidAdvance, invalid, 0);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvance? advance,
        int mergeIndex)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepProjector
                .TryProject(advance, mergeIndex, out var step));
        Assert.Null(step);
    }

    internal static PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStep
        Project(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvance
                advance,
            int mergeIndex)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepProjector
                .TryProject(advance, mergeIndex, out var step));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStep>(step);
    }

    private static PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvance
        Advance(
            PlaybackIsoBmffSampleEditMovieOverlap video,
            PlaybackIsoBmffSampleEditMovieOverlap audio) =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvanceTests
            .Advance(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvanceTests
                    .RoleSelection(video, audio, TieBreak.PreferVideo));

    private static PlaybackIsoBmffSampleEditMovieOverlap Appearance(
        Int128 start,
        Int128 end,
        uint denominator) =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvanceTests
            .Appearance(start, end, denominator);
}
