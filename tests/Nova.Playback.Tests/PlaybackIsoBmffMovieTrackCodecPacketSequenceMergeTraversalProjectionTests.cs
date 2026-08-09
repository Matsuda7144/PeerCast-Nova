using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjectionResult;
using TieBreak = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjectionTests
{
    [Fact]
    public void Project_RetainsExactCursorSourceAndCallerBounds()
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjectionTests
                .Projection();

        var projection = Project(source, TieBreak.PreferVideo, 2);
        Assert.Same(source, projection.Source);
        Assert.Equal(TieBreak.PreferVideo, projection.TieBreak);
        Assert.Equal(2, projection.MaximumStepCount);
        Assert.Same(source.CursorSet, projection.Traversal.InitialCursorSet);
        Assert.Equal(2, projection.Traversal.StepCount);
        Assert.True(projection.Traversal.IsTruncated);
    }

    [Fact]
    public void Project_RejectsMissingSourceAndInvalidCallerBounds()
    {
        AssertResult(
            Result.MissingCursorSetProjection,
            null,
            TieBreak.PreferVideo,
            1);
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjectionTests
                .Projection();
        AssertResult(Result.InvalidTieBreak, source, (TieBreak)99, 1);
        AssertResult(Result.InvalidStepLimit, source, TieBreak.PreferVideo, 0);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjection
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjection
                    source,
                TieBreak tieBreak,
                int maximumStepCount)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjector
                .Project(
                    source,
                    tieBreak,
                    maximumStepCount,
                    out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjection>(
                projection);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjection? source,
        TieBreak tieBreak,
        int maximumStepCount)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjector
                .Project(
                    source,
                    tieBreak,
                    maximumStepCount,
                    out var projection));
        Assert.Null(projection);
    }
}
