using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepSequenceCreationResult;
using TieBreak = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepSequenceTests
{
    [Fact]
    public void TryCreate_CopiesExactContiguousCursorChain()
    {
        var steps = Steps(TieBreak.PreferVideo);

        Assert.Equal(
            Result.Created,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepSequenceFactory
                .TryCreate(steps, out var sequence));
        steps[0] = steps[1];
        var created = Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepSequence>(
                sequence);
        Assert.Equal(TieBreak.PreferVideo, created.TieBreak);
        Assert.Equal(2, created.StepCount);
        Assert.Equal(0, created.Steps[0].MergeIndex);
        Assert.Equal(1, created.Steps[1].MergeIndex);
        Assert.Same(created.Steps[0].SuccessorCursorSet,
            created.Steps[1].Advance.SourceCursorSet);
        Assert.Same(created.Steps[0].Advance.SourceCursorSet,
            created.InitialCursorSet);
        Assert.Same(created.Steps[1].SuccessorCursorSet,
            created.FinalCursorSet);
    }

    [Fact]
    public void TryCreate_RejectsMissingEmptyAndExcessiveLists()
    {
        AssertResult(Result.MissingSteps, null);
        AssertResult(Result.EmptyStepSequence, []);
        var step = Steps(TieBreak.PreferVideo)[0];
        AssertResult(
            Result.StepLimitExceeded,
            Enumerable.Repeat(
                    step,
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepSequenceFactory
                        .MaximumStepCount + 1)
                .ToArray());
    }

    [Fact]
    public void TryCreate_RejectsMissingAndNonContiguousSteps()
    {
        var steps = Steps(TieBreak.PreferVideo);
        AssertResult(Result.MissingStep, [steps[0], null!]);
        AssertResult(
            Result.NonContiguousMergeIndex,
            [steps[0], steps[0]]);
    }

    [Fact]
    public void TryCreate_RejectsChangedTieBreak()
    {
        var first = Steps(TieBreak.PreferVideo)[0];
        var secondAdvance = NextAdvance(
            first.SuccessorCursorSet,
            TieBreak.PreferAudio);
        var second =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepTests.Project(
                secondAdvance,
                1);

        AssertResult(Result.TieBreakMismatch, [first, second]);
    }

    [Fact]
    public void TryCreate_RejectsDisconnectedCursorState()
    {
        var firstChain = Steps(TieBreak.PreferVideo);
        var foreignChain = Steps(TieBreak.PreferVideo);

        AssertResult(
            Result.CursorStateMismatch,
            [firstChain[0], foreignChain[1]]);
    }

    private static void AssertResult(
        Result expected,
        IReadOnlyList<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStep>? steps)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepSequenceFactory
                .TryCreate(steps, out var sequence));
        Assert.Null(sequence);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStep[] Steps(
            TieBreak tieBreak)
    {
        var firstAdvance =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvanceTests
                .Advance(
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvanceTests
                        .RoleSelection(
                            Appearance(10, 12, 90_000),
                            Appearance(20, 22, 48_000),
                            tieBreak));
        var first =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepTests.Project(
                firstAdvance,
                0);
        var secondAdvance = NextAdvance(first.SuccessorCursorSet, tieBreak);
        var second =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepTests.Project(
                secondAdvance,
                1);
        return [first, second];
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvance NextAdvance(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet cursorSet,
            TieBreak tieBreak)
    {
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshotProjectionResult
                .Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshotProjector
                .TryProject(cursorSet, out var snapshot));
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparisonResult
                .Compared,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparer
                .Compare(snapshot, out var comparison));
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelectionResult
                .Selected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelector
                .Select(comparison, tieBreak, out var roleSelection));
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvanceResult
                .Advanced,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvancer
                .TryAdvance(roleSelection, out var advance));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvance>(
                advance);
    }

    private static PlaybackIsoBmffSampleEditMovieOverlap Appearance(
        Int128 start,
        Int128 end,
        uint denominator) =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvanceTests
            .Appearance(start, end, denominator);
}
