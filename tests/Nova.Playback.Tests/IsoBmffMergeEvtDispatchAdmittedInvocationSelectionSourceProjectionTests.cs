using Disposition = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionDisposition;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionSourceProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionSourceProjectionTests
{
    [Fact]
    public void Project_RetainsAdmittedSourceOrderAndCallerBound()
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjectionTests
                .Projection(_ => Disposition.Admitted);

        var projection = Project(source, 1);
        Assert.Same(source, projection.Source);
        Assert.Equal(1, projection.MaximumSelectionCount);
        Assert.Same(
            source.AdmissionProjection,
            projection.Selection.Source);
        Assert.Equal(
            source.AdmissionProjection.Admissions.Count,
            projection.Selection.TotalAdmittedCount);
        Assert.Single(projection.Selection.Admissions);
        Assert.True(projection.Selection.IsTruncated);
    }

    [Fact]
    public void Project_RejectsMissingSourceAndInvalidBound()
    {
        AssertResult(Result.MissingAdmissionSourceProjection, null, 1);
        AssertResult(
            Result.InvalidMaximumSelectionCount,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjectionTests
                .Projection(_ => Disposition.Admitted),
            0);
    }

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionSourceProjection
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjection
                    source,
                int maximumSelectionCount)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionSourceProjector
                .Project(
                    source,
                    maximumSelectionCount,
                    out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionSourceProjection>(
                projection);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjection?
            source,
        int maximumSelectionCount)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionSourceProjector
                .Project(
                    source,
                    maximumSelectionCount,
                    out var projection));
        Assert.Null(projection);
    }
}
