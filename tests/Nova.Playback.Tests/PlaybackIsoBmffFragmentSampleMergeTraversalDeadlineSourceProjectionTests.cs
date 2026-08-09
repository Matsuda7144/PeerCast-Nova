using System.Numerics;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineSourceProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineSourceProjectionTests
{
    [Fact]
    public void Project_BindsExactClockOriginSourceAndDeadlines()
    {
        var source =
            PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginSourceProjectionTests
                .Projection(-100);

        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineSourceProjector
                .Project(source, out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineSourceProjection>(
                projection);
        Assert.Same(source, projected.Source);
        Assert.Same(source.Timing, projected.Deadlines.Source);
        Assert.Equal(source.Timing.SampleCount, projected.Deadlines.SampleCount);
    }

    [Fact]
    public void Project_RejectsMissingSource()
    {
        Assert.Equal(
            Result.InvalidInput,
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineSourceProjector
                .Project(null, out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineSourceProjection
            Projection(BigInteger originTick)
    {
        var source =
            PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginSourceProjectionTests
                .Projection(originTick);
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineSourceProjector
                .Project(source, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineSourceProjection>(
                projection);
    }
}
