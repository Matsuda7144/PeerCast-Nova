using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeSourceProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeSourceProjectionTests
{
    [Fact]
    public void Project_BindsExactTimingSourceAndCallerPolicy()
    {
        var source = Source();

        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeSourceProjector
                .Project(
                    source,
                    1_000,
                    PlaybackIsoBmffClockTickRoundingMode.Floor,
                    PlaybackIsoBmffClockTickRoundingMode.Ceiling,
                    out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeSourceProjection>(
                projection);
        Assert.Same(source, projected.Source);
        Assert.Same(source.Timing, projected.Timing.Source);
        Assert.Equal(1_000u, projected.Timing.TicksPerSecond);
        Assert.Equal(3, projected.Timing.SampleCount);
    }

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeSourceProjection
            Projection()
    {
        var source = Source();
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeSourceProjector
                .Project(
                    source,
                    1_000,
                    PlaybackIsoBmffClockTickRoundingMode.Floor,
                    PlaybackIsoBmffClockTickRoundingMode.Ceiling,
                    out var projection));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeSourceProjection>(
                projection);
    }

    [Fact]
    public void Project_RejectsMissingSource()
    {
        Assert.Equal(
            Result.InvalidInput,
            PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeSourceProjector
                .Project(
                    null,
                    1_000,
                    PlaybackIsoBmffClockTickRoundingMode.Floor,
                    PlaybackIsoBmffClockTickRoundingMode.Ceiling,
                    out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalTimeSourceProjection Source()
    {
        var completion =
            PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewTests
                .Completion(4, 1);
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewSourceProjectionResult
                .Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewSourceProjector
                .Project(completion, out var acknowledgementSource));
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleMergeTraversalTimeSourceProjectionResult
                .Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalTimeSourceProjector
                .Project(
                    Assert.IsType<
                        PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewSourceProjection>(
                            acknowledgementSource),
                    out var source));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalTimeSourceProjection>(
                source);
    }
}
