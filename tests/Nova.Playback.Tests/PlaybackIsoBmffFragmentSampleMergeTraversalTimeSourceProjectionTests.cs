using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalTimeSourceProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalTimeSourceProjectionTests
{
    [Fact]
    public void Project_BindsExactAcknowledgementSourceAndTiming()
    {
        var completion =
            PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewTests
                .Completion(4, 1);
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewSourceProjectionResult
                .Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewSourceProjector
                .Project(completion, out var source));
        var exactSource = Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewSourceProjection>(
                source);

        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalTimeSourceProjector
                .Project(exactSource, out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalTimeSourceProjection>(
                projection);
        Assert.Same(exactSource, projected.Source);
        Assert.Same(exactSource.View, projected.Timing.AcknowledgementView);
        Assert.Equal(3, projected.Timing.SampleCount);
        Assert.Equal(48_000u, projected.Timing.MediaTimescale);
    }

    [Fact]
    public void Project_RejectsMissingSource()
    {
        Assert.Equal(
            Result.InvalidInput,
            PlaybackIsoBmffFragmentSampleMergeTraversalTimeSourceProjector
                .Project(null, out var projection));
        Assert.Null(projection);
    }
}
