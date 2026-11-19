using System.Numerics;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationSourceProjectionResult;
using State = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineState;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationSourceProjectionTests
{
    [Theory]
    [InlineData(99, State.Pending)]
    [InlineData(100, State.Ready)]
    [InlineData(1_000, State.Expired)]
    public void Project_RetainsExactDeadlineSourceAndCallerObservation(
        int observationTick,
        State expectedState)
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineSourceProjectionTests
                .Projection();

        var projection = Project(source, observationTick);
        Assert.Same(source, projection.Source);
        Assert.Equal(new BigInteger(observationTick), projection.ObservationTick);
        Assert.Same(
            source.DeadlineProjection,
            projection.ObservationSnapshot.Source);
        Assert.Equal(
            expectedState,
            projection.ObservationSnapshot.Observations[0].State);
    }

    [Fact]
    public void Project_RejectsMissingSource()
    {
        Assert.Equal(
            Result.MissingDeadlineSourceProjection,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationSourceProjector
                .Project(null, 0, out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationSourceProjection
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineSourceProjection
                    source,
                BigInteger observationTick)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationSourceProjector
                .Project(source, observationTick, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationSourceProjection>(
                projection);
    }
}
