using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuationResult;
using TieBreak = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuationTests
{
    [Fact]
    public void TryContinue_StartsAtExactFinalStateWithoutPriorSteps()
    {
        var source = InitialTraversal(2);

        var continuation = Continue(source, 2);
        Assert.Same(source, continuation.Source);
        Assert.Same(source.InitialCursorSet, continuation.InitialCursorSet);
        Assert.Same(source.FinalCursorSet,
            continuation.Segment.InitialCursorSet);
        Assert.Equal(source.StepCount, continuation.StartingMergeIndex);
        Assert.Equal(2, continuation.SegmentStepCount);
        Assert.Equal(4, continuation.TotalStepCount);
        Assert.Equal(2, continuation.Steps.Count);
        Assert.Equal(0, continuation.Steps[0].MergeIndex);
        Assert.DoesNotContain(source.Steps[0], continuation.Steps);
        Assert.Equal(source.TieBreak, continuation.TieBreak);
        Assert.False(continuation.IsExhausted);
        Assert.True(continuation.IsTruncated);
    }

    [Fact]
    public void TryContinue_AllowsFiniteContinuationChainingToExhaustion()
    {
        var first = Continue(InitialTraversal(2), 2);
        var second = Continue(first, 2);

        Assert.Same(first, second.Source);
        Assert.Equal(4, second.StartingMergeIndex);
        Assert.Equal(2, second.SegmentStepCount);
        Assert.Equal(6, second.TotalStepCount);
        Assert.True(second.IsExhausted);
        Assert.False(second.IsTruncated);
        Assert.True(second.FinalCursorSet.VideoCursor!.IsExhausted);
        Assert.True(second.FinalCursorSet.AudioCursor!.IsExhausted);
    }

    [Fact]
    public void TryContinue_PreservesCallerTiePolicy()
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalTests
                .Traverse(
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalTests
                        .InitialCursorSet(equalFirstHeads: true),
                    TieBreak.PreferAudio,
                    1);

        var continuation = Continue(source, 1);
        Assert.Equal(TieBreak.PreferAudio, continuation.TieBreak);
        Assert.Equal(TieBreak.PreferAudio, continuation.Segment.TieBreak);
    }

    [Fact]
    public void TryContinue_RejectsMissingAndExhaustedSources()
    {
        AssertResult(Result.MissingSource, null, 1);
        var exhausted =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalTests
                .Traverse(
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalTests
                        .InitialCursorSet(),
                    TieBreak.PreferVideo,
                    16);
        Assert.True(exhausted.IsExhausted);
        AssertResult(Result.SourceExhausted, exhausted, 1);
    }

    [Fact]
    public void TryContinue_RejectsInvalidAdditionalLimits()
    {
        var source = InitialTraversal(1);
        AssertResult(Result.InvalidAdditionalStepLimit, source, 0);
        AssertResult(
            Result.InvalidAdditionalStepLimit,
            source,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraverser
                .MaximumStepCount);
    }

    private static void AssertResult(
        Result expected,
        IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalSource?
            source,
        int additionalStepLimit)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuationFactory
                .TryContinue(source, additionalStepLimit, out var continuation));
        Assert.Null(continuation);
    }

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuation
            Continue(
                IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalSource
                    source,
                int additionalStepLimit)
    {
        Assert.Equal(
            Result.Continued,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuationFactory
                .TryContinue(
                    source,
                    additionalStepLimit,
                    out var continuation));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuation>(
                continuation);
    }

    private static PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversal
        InitialTraversal(int maximumStepCount) =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalTests
            .Traverse(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalTests
                    .InitialCursorSet(),
                TieBreak.PreferVideo,
                maximumStepCount);
}
