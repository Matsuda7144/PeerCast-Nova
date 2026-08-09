using System.Numerics;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSourceProjectionResult;
using State = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineState;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSourceProjectionTests
{
    [Theory]
    [InlineData(-91, State.Pending, State.Pending)]
    [InlineData(-79, State.Ready, State.Ready)]
    [InlineData(-57, State.Expired, State.Expired)]
    public void Project_BindsExactDeadlineSourceAndCallerObservation(
        int observationTick,
        State expectedDecode,
        State expectedPresentation)
    {
        var source =
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineSourceProjectionTests
                .Projection(-100);

        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSourceProjector
                .Project(source, observationTick, out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSourceProjection>(
                projection);
        Assert.Same(source, projected.Source);
        Assert.Equal(new BigInteger(observationTick), projected.ObservationTick);
        Assert.Same(source.Deadlines, projected.Snapshot.Source);
        Assert.Equal(expectedDecode, projected.Snapshot.Observations[0].DecodeState);
        Assert.Equal(
            expectedPresentation,
            projected.Snapshot.Observations[0].PresentationState);
    }

    [Fact]
    public void Project_RejectsMissingSource()
    {
        Assert.Equal(
            Result.InvalidInput,
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSourceProjector
                .Project(null, 0, out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSourceProjection
            Projection(int observationTick, int originTick)
    {
        var source =
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineSourceProjectionTests
                .Projection(originTick);
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSourceProjector
                .Project(source, observationTick, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSourceProjection>(
                projection);
    }
}
