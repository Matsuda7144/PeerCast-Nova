using Policy = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityPolicy;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilitySourceProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilitySourceProjectionTests
{
    [Fact]
    public void Project_BindsExactObservationSourceAndPolicies()
    {
        var source =
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSourceProjectionTests
                .Projection(-90, -100);
        var options =
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityTests
                .Options(Policy.ReadyOnly, Policy.ReadyOrExpired);

        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilitySourceProjector
                .Project(source, options, out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilitySourceProjection>(
                projection);
        Assert.Same(source, projected.Source);
        Assert.Same(source.Snapshot, projected.Eligibility.Source);
        Assert.Same(options, projected.Eligibility.Options);
        Assert.Equal(source.Snapshot.SampleCount, projected.Eligibility.SampleCount);
    }

    [Fact]
    public void Project_RejectsMissingSourceAndInvalidPolicy()
    {
        var options =
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityTests
                .Options(Policy.ReadyOnly, Policy.ReadyOnly);
        Assert.Equal(
            Result.InvalidInput,
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilitySourceProjector
                .Project(null, options, out var missing));
        Assert.Null(missing);

        var source =
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSourceProjectionTests
                .Projection(-79, -100);
        Assert.Equal(
            Result.InvalidPolicy,
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilitySourceProjector
                .Project(
                    source,
                    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityTests
                        .Options((Policy)99, Policy.ReadyOnly),
                    out var invalid));
        Assert.Null(invalid);
    }

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilitySourceProjection
            Projection(
                int observationTick,
                int originTick,
                Policy decodePolicy,
                Policy presentationPolicy)
    {
        var source =
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSourceProjectionTests
                .Projection(observationTick, originTick);
        var options =
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityTests
                .Options(decodePolicy, presentationPolicy);
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilitySourceProjector
                .Project(source, options, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilitySourceProjection>(
                projection);
    }
}
