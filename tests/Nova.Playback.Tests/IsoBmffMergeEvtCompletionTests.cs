using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionTests
{
    [Fact]
    public void TryCreate_CertifiesFullSingleTraversal()
    {
        var traversal =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationTests
                .Traversal(16);

        var completion = Complete(traversal);
        Assert.Same(traversal, completion.Source);
        Assert.Equal(6, completion.InitialRemainingCount);
        Assert.Equal(6, completion.ExpectedEventCount);
        Assert.Equal(6, completion.ActualEventCount);
        Assert.Same(traversal.InitialCursor, completion.InitialCursor);
        Assert.Same(traversal.FinalCursor, completion.FinalCursor);
    }

    [Fact]
    public void TryCreate_CertifiesExactContinuationTotal()
    {
        var initial =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationTests
                .Traversal(2);
        var first =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationTests
                .Continue(initial, 2);
        var second =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationTests
                .Continue(first, 2);

        var completion = Complete(second);
        Assert.Equal(6, completion.ActualEventCount);
        Assert.True(completion.FinalCursor.IsExhausted);
    }

    [Fact]
    public void TryCreate_RejectsMissingAndTruncatedSource()
    {
        AssertResult(Result.MissingSource, null);
        AssertResult(
            Result.SourceNotExhausted,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationTests
                .Traversal(2));
    }

    [Fact]
    public void TryCreate_RejectsFalseExhaustedCountClaim()
    {
        var full =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationTests
                .Traversal(16);
        var invalid = new Source(
            full.InitialCursor,
            full.FinalCursor,
            full.TotalEventCount - 1,
            true);

        AssertResult(Result.EventCountMismatch, invalid);
    }

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletion
            Complete(
                IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalSource
                    source)
    {
        Assert.Equal(
            Result.Certified,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionFactory
                .TryCreate(source, out var completion));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletion>(
                completion);
    }

    private static void AssertResult(
        Result expected,
        IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalSource?
            source)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionFactory
                .TryCreate(source, out var completion));
        Assert.Null(completion);
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
