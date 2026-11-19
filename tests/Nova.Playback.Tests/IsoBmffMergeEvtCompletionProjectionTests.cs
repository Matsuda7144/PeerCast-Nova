using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionProjectionTests
{
    [Fact]
    public void Project_CertifiesExactProjectedEventTraversalCounts()
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationProjectionTests
                .Project(
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationProjectionTests
                        .Initial(),
                    2);

        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionProjector
                .Project(source, out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionProjection>(
                projection);
        Assert.Same(source, projected.Source);
        Assert.Same(source.TraversalSource, projected.Completion.Source);
        Assert.Same(
            source.CursorSource.Cursor,
            projected.Completion.InitialCursor);
        Assert.Equal(4, projected.Completion.InitialRemainingCount);
        Assert.Equal(4, projected.Completion.ExpectedEventCount);
        Assert.Equal(4, projected.Completion.ActualEventCount);
        Assert.Equal(4, projected.Completion.FinalCursor.NextEventIndex);
    }

    [Fact]
    public void Project_RejectsMissingAndTruncatedSources()
    {
        AssertResult(Result.MissingTraversalProjection, null);
        AssertResult(
            Result.SourceNotExhausted,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationProjectionTests
                .Initial());
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionProjection
            Projection()
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationProjectionTests
                .Project(
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationProjectionTests
                        .Initial(),
                    2);
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionProjector
                .Project(source, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionProjection>(
                projection);
    }

    private static void AssertResult(
        Result expected,
        IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjectionSource?
            source)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionProjector
                .Project(source, out var projection));
        Assert.Null(projection);
    }
}
