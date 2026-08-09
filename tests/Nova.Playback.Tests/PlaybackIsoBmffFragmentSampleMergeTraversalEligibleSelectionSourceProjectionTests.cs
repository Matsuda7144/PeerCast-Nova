using Combination = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalEligibilityCombinationPolicy;
using Policy = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityPolicy;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionSourceProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionSourceProjectionTests
{
    [Fact]
    public void Project_BindsExactEligibilitySourcePolicyAndBound()
    {
        var source =
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilitySourceProjectionTests
                .Projection(
                    -30,
                    -100,
                    Policy.ReadyOrExpired,
                    Policy.ReadyOrExpired);

        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionSourceProjector
                .Project(
                    source,
                    Combination.RequireBoth,
                    2,
                    out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionSourceProjection>(
                projection);
        Assert.Same(source, projected.Source);
        Assert.Same(source.Eligibility, projected.Selection.Source);
        Assert.Equal(Combination.RequireBoth,
            projected.Selection.CombinationPolicy);
        Assert.Equal(2, projected.Selection.MaximumCandidateCount);
        Assert.Equal(3, projected.Selection.EligibleCount);
    }

    [Fact]
    public void Project_RejectsMissingSourceAndInvalidBound()
    {
        Assert.Equal(
            Result.InvalidInput,
            PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionSourceProjector
                .Project(
                    null,
                    Combination.RequireBoth,
                    1,
                    out var missing));
        Assert.Null(missing);

        var source =
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilitySourceProjectionTests
                .Projection(
                    -79,
                    -100,
                    Policy.ReadyOnly,
                    Policy.ReadyOnly);
        Assert.Equal(
            Result.InvalidMaximumCandidateCount,
            PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionSourceProjector
                .Project(
                    source,
                    Combination.RequireBoth,
                    0,
                    out var invalid));
        Assert.Null(invalid);
    }

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionSourceProjection
            Projection(
                int observationTick,
                int originTick,
                Policy decodePolicy,
                Policy presentationPolicy,
                Combination combinationPolicy,
                int maximumCandidateCount)
    {
        var source =
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilitySourceProjectionTests
                .Projection(
                    observationTick,
                    originTick,
                    decodePolicy,
                    presentationPolicy);
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionSourceProjector
                .Project(
                    source,
                    combinationPolicy,
                    maximumCandidateCount,
                    out var projection));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionSourceProjection>(
                projection);
    }
}
