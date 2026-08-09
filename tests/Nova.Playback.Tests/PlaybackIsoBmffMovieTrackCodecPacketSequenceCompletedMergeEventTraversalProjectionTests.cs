using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjectionTests
{
    [Fact]
    public void Project_RetainsExactCursorSourceAndCallerBound()
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjectionTests
                .Projection();

        var projection = Project(source, 2);
        Assert.Same(source, projection.Source);
        Assert.Equal(2, projection.MaximumEventCount);
        Assert.Same(source.Cursor, projection.Traversal.InitialCursor);
        Assert.Equal(2, projection.Traversal.EventCount);
        Assert.True(projection.Traversal.IsTruncated);
        Assert.Equal(0, projection.Traversal.Acknowledgements[0].EventIndex);
        Assert.Equal(1, projection.Traversal.Acknowledgements[1].EventIndex);
    }

    [Fact]
    public void Project_RejectsMissingSourceAndInvalidCallerBound()
    {
        AssertResult(Result.MissingCursorProjection, null, 1);
        AssertResult(
            Result.InvalidEventLimit,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjectionTests
                .Projection(),
            0);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjection
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjection
                    source,
                int maximumEventCount)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjector
                .Project(source, maximumEventCount, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjection>(
                projection);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjection?
            source,
        int maximumEventCount)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjector
                .Project(source, maximumEventCount, out var projection));
        Assert.Null(projection);
    }
}
