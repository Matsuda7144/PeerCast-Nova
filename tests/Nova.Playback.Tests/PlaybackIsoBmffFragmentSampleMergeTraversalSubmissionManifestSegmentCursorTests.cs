using Disposition = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionDisposition;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorTests
{
    [Fact]
    public void Project_RetainsExactPositionAndCurrentSegment()
    {
        var source = Segments();

        var cursor = Project(source, 1);

        Assert.Same(source, cursor.Source);
        Assert.Equal(1, cursor.NextSegmentIndex);
        Assert.Equal(1, cursor.RemainingSegmentCount);
        Assert.True(cursor.HasCurrent);
        Assert.Same(source.Segments[1], cursor.Current);
    }

    [Fact]
    public void Project_EndPositionHasNoCurrentSegment()
    {
        var source = Segments();

        var cursor = Project(source, source.SegmentCount);

        Assert.False(cursor.HasCurrent);
        Assert.Null(cursor.Current);
        Assert.Equal(0, cursor.RemainingSegmentCount);
    }

    [Fact]
    public void Project_RejectsMissingAndOutOfRangePositions()
    {
        AssertResult(Result.InvalidInput, null, 0);
        var source = Segments();
        AssertResult(Result.InvalidNextSegmentIndex, source, -1);
        AssertResult(
            Result.InvalidNextSegmentIndex,
            source,
            source.SegmentCount + 1);
    }

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursor
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentProjection
                    source,
                int nextSegmentIndex)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorProjector
                .Project(source, nextSegmentIndex, out var cursor));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursor>(
                cursor);
    }

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentProjection
            Segments()
    {
        var summary =
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSummaryTests
                .Projection(
                    [Disposition.Include, Disposition.Include,
                        Disposition.Include]);
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentProjectionResult
                .Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentProjector
                .Project(summary, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentProjection>(
                projection);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentProjection?
            source,
        int nextSegmentIndex)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorProjector
                .Project(source, nextSegmentIndex, out var cursor));
        Assert.Null(cursor);
    }
}
