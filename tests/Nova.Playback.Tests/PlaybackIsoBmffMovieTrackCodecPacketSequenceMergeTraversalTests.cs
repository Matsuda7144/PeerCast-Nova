using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalResult;
using Role = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole;
using TieBreak = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalTests
{
    [Fact]
    public void TryTraverse_OwnsCompleteExactMergeToExhaustion()
    {
        var initial = InitialCursorSet();

        var traversal = Traverse(initial, TieBreak.PreferVideo, 16);
        Assert.Same(initial, traversal.InitialCursorSet);
        Assert.Equal(TieBreak.PreferVideo, traversal.TieBreak);
        Assert.Equal(16, traversal.MaximumStepCount);
        Assert.Equal(6, traversal.StepCount);
        Assert.NotNull(traversal.StepSequence);
        Assert.True(traversal.IsExhausted);
        Assert.False(traversal.IsTruncated);
        Assert.True(traversal.FinalCursorSet.VideoCursor!.IsExhausted);
        Assert.True(traversal.FinalCursorSet.AudioCursor!.IsExhausted);
        for (var index = 0; index < traversal.Steps.Count; index++)
        {
            Assert.Equal(index, traversal.Steps[index].MergeIndex);
            if (index > 0)
            {
                Assert.Same(
                    traversal.Steps[index - 1].SuccessorCursorSet,
                    traversal.Steps[index].Advance.SourceCursorSet);
            }
        }
    }

    [Fact]
    public void TryTraverse_AppliesCallerTieBreakToEqualFirstHeads()
    {
        var videoFirst = Traverse(
            InitialCursorSet(equalFirstHeads: true),
            TieBreak.PreferVideo,
            1);
        var audioFirst = Traverse(
            InitialCursorSet(equalFirstHeads: true),
            TieBreak.PreferAudio,
            1);

        Assert.Equal(Role.Video, videoFirst.Steps[0].Role);
        Assert.Equal(Role.Audio, audioFirst.Steps[0].Role);
    }

    [Fact]
    public void TryTraverse_ReportsCallerBoundedTruncation()
    {
        var traversal = Traverse(
            InitialCursorSet(),
            TieBreak.PreferVideo,
            2);

        Assert.Equal(2, traversal.StepCount);
        Assert.False(traversal.IsExhausted);
        Assert.True(traversal.IsTruncated);
        Assert.Same(
            traversal.Steps[^1].SuccessorCursorSet,
            traversal.FinalCursorSet);
    }

    [Fact]
    public void TryTraverse_AcceptsAlreadyExhaustedCursorSet()
    {
        var source = InitialCursorSet();
        var exhausted = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet(
                source.Selection,
                new(
                    source.VideoCursor!.Sequence,
                    source.VideoCursor.Sequence.Entries.Count),
                new(
                    source.AudioCursor!.Sequence,
                    source.AudioCursor.Sequence.Entries.Count));

        var traversal = Traverse(exhausted, TieBreak.PreferVideo, 4);
        Assert.Equal(0, traversal.StepCount);
        Assert.Null(traversal.StepSequence);
        Assert.Same(exhausted, traversal.FinalCursorSet);
        Assert.True(traversal.IsExhausted);
        Assert.False(traversal.IsTruncated);
    }

    [Fact]
    public void TryTraverse_RejectsMissingAndInvalidOptions()
    {
        AssertResult(Result.MissingCursorSet, null, TieBreak.PreferVideo, 1);
        var initial = InitialCursorSet();
        AssertResult(Result.InvalidTieBreak, initial, (TieBreak)99, 1);
        AssertResult(Result.InvalidStepLimit, initial, TieBreak.PreferVideo, 0);
        AssertResult(
            Result.InvalidStepLimit,
            initial,
            TieBreak.PreferVideo,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraverser
                .MaximumStepCount + 1);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet? cursorSet,
        TieBreak tieBreak,
        int maximumStepCount)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraverser
                .TryTraverse(
                    cursorSet,
                    tieBreak,
                    maximumStepCount,
                    out var traversal));
        Assert.Null(traversal);
    }

    internal static PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversal
        Traverse(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet cursorSet,
            TieBreak tieBreak,
            int maximumStepCount)
    {
        Assert.Equal(
            Result.Traversed,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraverser
                .TryTraverse(
                    cursorSet,
                    tieBreak,
                    maximumStepCount,
                    out var traversal));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversal>(
                traversal);
    }

    internal static PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet
        InitialCursorSet(bool equalFirstHeads = false)
    {
        var video = equalFirstHeads
            ? Appearance(45_000, 54_000, 90_000)
            : Appearance(10, 12, 90_000);
        var audio = equalFirstHeads
            ? Appearance(24_000, 28_800, 48_000)
            : Appearance(20, 22, 48_000);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelectionTests
                .Comparison(video, audio)
                .Snapshot.CursorSet;
    }

    private static PlaybackIsoBmffSampleEditMovieOverlap Appearance(
        Int128 start,
        Int128 end,
        uint denominator) =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelectionTests
            .Appearance(start, end, denominator);
}
