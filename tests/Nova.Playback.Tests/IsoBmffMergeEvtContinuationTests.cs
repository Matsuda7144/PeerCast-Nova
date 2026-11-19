using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationTests
{
    [Fact]
    public void TryContinue_ChainsFiniteSegmentsWithoutPriorReEmission()
    {
        var initial = Traversal(2);

        var first = Continue(initial, 2);
        Assert.Same(initial, first.Source);
        Assert.Equal(2, first.StartingEventIndex);
        Assert.Equal(2, first.SegmentEventCount);
        Assert.Equal(4, first.TotalEventCount);
        Assert.Same(initial.FinalCursor, first.Segment.InitialCursor);
        var second = Continue(first, 2);
        Assert.Equal(4, second.StartingEventIndex);
        Assert.Equal(6, second.TotalEventCount);
        Assert.True(second.IsExhausted);
        Assert.Same(first.FinalCursor, second.Segment.InitialCursor);
    }

    [Fact]
    public void TryContinue_RejectsMissingExhaustedAndInvalidBounds()
    {
        AssertResult(Result.MissingSource, null, 1);
        var full = Traversal(16);
        AssertResult(Result.SourceExhausted, full, 1);
        var truncated = Traversal(2);
        AssertResult(Result.InvalidAdditionalEventLimit, truncated, 0);
        AssertResult(
            Result.InvalidAdditionalEventLimit,
            truncated,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraverser
                .MaximumEventCount);
    }

    [Fact]
    public void TryContinue_RejectsInconsistentSourceCursorCount()
    {
        var traversal = Traversal(2);
        var invalid = new Source(
            traversal.InitialCursor,
            traversal.FinalCursor,
            1,
            false);

        AssertResult(Result.InvalidSource, invalid, 1);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuation
            Continue(
                IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalSource
                    source,
                int additionalEventLimit)
    {
        Assert.Equal(
            Result.Continued,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationFactory
                .TryContinue(
                    source,
                    additionalEventLimit,
                    out var continuation));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuation>(
                continuation);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversal
            Traversal(int maximumEventCount) =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTests
            .Traverse(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTests
                    .InitialCursor(),
                maximumEventCount);

    private static void AssertResult(
        Result expected,
        IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalSource?
            source,
        int additionalEventLimit)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationFactory
                .TryContinue(
                    source,
                    additionalEventLimit,
                    out var continuation));
        Assert.Null(continuation);
    }

    private sealed record Source(
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor
            InitialCursor,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor
            FinalCursor,
        int TotalEventCount,
        bool IsExhausted) :
        IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalSource;
}
