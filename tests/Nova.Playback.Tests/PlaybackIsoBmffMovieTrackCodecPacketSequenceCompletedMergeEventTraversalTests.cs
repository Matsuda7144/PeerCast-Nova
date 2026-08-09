using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTests
{
    [Fact]
    public void TryTraverse_OwnsExactAcknowledgementChainToExhaustion()
    {
        var initial = InitialCursor();

        var traversal = Traverse(initial, 16);
        Assert.Same(initial, traversal.InitialCursor);
        Assert.Equal(initial.RemainingCount, traversal.EventCount);
        Assert.True(traversal.IsExhausted);
        Assert.False(traversal.IsTruncated);
        Assert.True(traversal.FinalCursor.IsExhausted);
        for (var index = 0;
            index < traversal.Acknowledgements.Count;
            index++)
        {
            var acknowledgement = traversal.Acknowledgements[index];
            Assert.Equal(index, acknowledgement.EventIndex);
            if (index > 0)
            {
                Assert.Same(
                    traversal.Acknowledgements[index - 1].SuccessorCursor,
                    acknowledgement.SourceCursor);
            }
        }
    }

    [Fact]
    public void TryTraverse_ReportsCallerBoundedTruncation()
    {
        var traversal = Traverse(InitialCursor(), 2);

        Assert.Equal(2, traversal.EventCount);
        Assert.False(traversal.IsExhausted);
        Assert.True(traversal.IsTruncated);
        Assert.Same(
            traversal.Acknowledgements[^1].SuccessorCursor,
            traversal.FinalCursor);
    }

    [Fact]
    public void TryTraverse_AcceptsAlreadyExhaustedCursor()
    {
        var full = Traverse(InitialCursor(), 16);

        var traversal = Traverse(full.FinalCursor, 1);
        Assert.Equal(0, traversal.EventCount);
        Assert.Empty(traversal.Acknowledgements);
        Assert.Same(full.FinalCursor, traversal.FinalCursor);
        Assert.True(traversal.IsExhausted);
    }

    [Fact]
    public void TryTraverse_RejectsMissingInvalidLimitAndCursor()
    {
        AssertResult(Result.MissingCursor, null, 1);
        var cursor = InitialCursor();
        AssertResult(Result.InvalidEventLimit, cursor, 0);
        AssertResult(
            Result.InvalidEventLimit,
            cursor,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraverser
                .MaximumEventCount + 1);
        AssertResult(
            Result.InvalidCursor,
            new(cursor.Projection, cursor.Projection.EventCount + 1),
            1);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor?
            cursor,
        int maximumEventCount)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraverser
                .TryTraverse(cursor, maximumEventCount, out var traversal));
        Assert.Null(traversal);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversal
            Traverse(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor
                    cursor,
                int maximumEventCount)
    {
        Assert.Equal(
            Result.Traversed,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraverser
                .TryTraverse(cursor, maximumEventCount, out var traversal));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversal>(
                traversal);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor
            InitialCursor() =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorTests
            .Cursor(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorTests
                    .Projection());
}
