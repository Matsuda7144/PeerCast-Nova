using Policy = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityPolicy;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionSourceProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionSourceProjectionTests
{
    [Fact]
    public void Project_RetainsEligibleSourceOrderAndCallerBound()
    {
        var source = Eligibility(1_000, Policy.ReadyOrExpired);

        var projection = Project(source, 1);
        Assert.Same(source, projection.Source);
        Assert.Equal(1, projection.MaximumCandidateCount);
        Assert.Same(
            source.EligibilityProjection,
            projection.CandidateSelection.Source);
        Assert.Equal(
            source.EligibilityProjection.Entries.Count,
            projection.CandidateSelection.EligibleCount);
        var candidate = Assert.Single(projection.CandidateSelection.Candidates);
        Assert.Equal(0, candidate.SourceIndex);
        Assert.Same(
            source.EligibilityProjection.Entries[0],
            candidate.Eligibility);
    }

    [Fact]
    public void Project_RejectsMissingSourceAndInvalidBound()
    {
        AssertResult(Result.MissingEligibilitySourceProjection, null, 1);
        AssertResult(
            Result.InvalidMaximumCandidateCount,
            Eligibility(100, Policy.ReadyOnly),
            0);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionSourceProjection
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilitySourceProjection
                    source,
                int maximumCandidateCount)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionSourceProjector
                .Project(
                    source,
                    maximumCandidateCount,
                    out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionSourceProjection>(
                projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilitySourceProjection
            Eligibility(int observationTick, Policy policy) =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilitySourceProjectionTests
            .Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilitySourceProjectionTests
                    .Observation(observationTick),
                policy);

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilitySourceProjection?
            source,
        int maximumCandidateCount)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionSourceProjector
                .Project(
                    source,
                    maximumCandidateCount,
                    out var projection));
        Assert.Null(projection);
    }
}
