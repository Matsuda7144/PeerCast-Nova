using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewSourceProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewSourceProjectionTests
{
    [Fact]
    public void Project_BindsExactCompletionToReconstructedView()
    {
        var source =
            PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewTests
                .Completion(4, 1);

        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewSourceProjector
                .Project(source, out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewSourceProjection>(
                projection);
        Assert.Same(source, projected.Source);
        Assert.Same(source, projected.View.Completion);
        Assert.Equal(3, projected.View.AcknowledgementCount);
        Assert.Equal(1, projected.View.Acknowledgements[0].SampleIndex);
        Assert.Equal(3, projected.View.Acknowledgements[^1].SampleIndex);
    }

    [Fact]
    public void Project_AcceptsEmptyCompletion()
    {
        var source =
            PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewTests
                .Completion(0, 0);

        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewSourceProjector
                .Project(source, out var projection));
        Assert.Empty(Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewSourceProjection>(
                projection).View.Acknowledgements);
    }

    [Fact]
    public void Project_RejectsMissingSource()
    {
        Assert.Equal(
            Result.InvalidInput,
            PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewSourceProjector
                .Project(null, out var projection));
        Assert.Null(projection);
    }
}
