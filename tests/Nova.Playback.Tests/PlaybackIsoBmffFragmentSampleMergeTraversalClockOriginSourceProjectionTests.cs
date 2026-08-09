using System.Numerics;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginSourceProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginSourceProjectionTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(-100)]
    public void Project_BindsExactIntegralSourceAndCallerOrigin(int originTick)
    {
        var source =
            PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeSourceProjectionTests
                .Projection();

        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginSourceProjector
                .Project(source, originTick, out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginSourceProjection>(
                projection);
        Assert.Same(source, projected.Source);
        Assert.Equal(new BigInteger(originTick), projected.OriginTick);
        Assert.Same(source.Timing, projected.Timing.Source);
        Assert.Equal(source.Timing.SampleCount, projected.Timing.SampleCount);
    }

    [Fact]
    public void Project_RejectsMissingSource()
    {
        Assert.Equal(
            Result.InvalidInput,
            PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginSourceProjector
                .Project(null, 0, out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginSourceProjection
            Projection(BigInteger originTick)
    {
        var source =
            PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeSourceProjectionTests
                .Projection();
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginSourceProjector
                .Project(source, originTick, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginSourceProjection>(
                projection);
    }
}
