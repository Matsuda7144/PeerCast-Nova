using Combination = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalEligibilityCombinationPolicy;
using Policy = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityPolicy;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalRequestSourceProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalRequestSourceProjectionTests
{
    [Fact]
    public void Project_BindsExactSelectionSourceAndRequests()
    {
        var source =
            PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionSourceProjectionTests
                .Projection(
                    -30,
                    -100,
                    Policy.ReadyOrExpired,
                    Policy.ReadyOrExpired,
                    Combination.RequireBoth,
                    2);

        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalRequestSourceProjector
                .Project(source, out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalRequestSourceProjection>(
                projection);
        Assert.Same(source, projected.Source);
        Assert.Same(source.Selection, projected.Requests.Source);
        Assert.Equal(source.Selection.Candidates.Count,
            projected.Requests.RequestCount);
        Assert.Equal(7u, projected.Requests.TrackId);
    }

    [Fact]
    public void Project_RejectsMissingSource()
    {
        Assert.Equal(
            Result.InvalidInput,
            PlaybackIsoBmffFragmentSampleMergeTraversalRequestSourceProjector
                .Project(null, out var projection));
        Assert.Null(projection);
    }
}
