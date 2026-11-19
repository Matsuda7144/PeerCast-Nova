using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationProjectionTests
{
    [Fact]
    public void Project_ContinuesExactAcknowledgementChainToExhaustion()
    {
        var source = Initial();

        var projection = Project(source, 2);
        Assert.Same(source, projection.Source);
        Assert.Equal(2, projection.AdditionalEventLimit);
        Assert.Same(
            source.Traversal.FinalCursor,
            projection.Continuation.Segment.InitialCursor);
        Assert.Equal(2, projection.Continuation.StartingEventIndex);
        Assert.Equal(4, projection.Continuation.TotalEventCount);
        Assert.True(projection.Continuation.IsExhausted);
        Assert.Same(
            source.CursorSource.Cursor,
            projection.Continuation.InitialCursor);
        Assert.Equal(
            2,
            projection.Continuation.Segment.Acknowledgements[0].EventIndex);
    }

    [Fact]
    public void Project_RejectsMissingSourceAndInvalidLimit()
    {
        AssertResult(Result.MissingSourceProjection, null, 1);
        AssertResult(Result.InvalidAdditionalEventLimit, Initial(), 0);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationProjection
            Project(
                IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjectionSource
                    source,
                int additionalEventLimit)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationProjector
                .Project(source, additionalEventLimit, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationProjection>(
                projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjection
            Initial() =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjectionTests
            .Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjectionTests
                    .Projection(),
                2);

    private static void AssertResult(
        Result expected,
        IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjectionSource?
            source,
        int additionalEventLimit)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationProjector
                .Project(source, additionalEventLimit, out var projection));
        Assert.Null(projection);
    }
}
