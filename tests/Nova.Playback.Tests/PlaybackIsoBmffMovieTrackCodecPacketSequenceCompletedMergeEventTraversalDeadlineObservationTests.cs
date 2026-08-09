using System.Numerics;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationResult;
using State = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineState;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationTests
{
    [Theory]
    [InlineData(99, State.Pending)]
    [InlineData(100, State.Ready)]
    [InlineData(101, State.Expired)]
    public void Observe_ClassifiesExactDeadlineBoundaries(
        int observationTick,
        State expected)
    {
        var source = DeadlineProjection();

        var snapshot = Observe(source, observationTick);
        Assert.Same(source, snapshot.Source);
        Assert.Equal(new BigInteger(observationTick),
            snapshot.ObservationTick);
        var first = snapshot.Observations[0];
        Assert.Same(source.Deadlines[0], first.Deadline);
        Assert.Equal(expected, first.State);
        Assert.Equal(first.Deadline.ReadyTick - observationTick,
            first.TicksUntilReady);
        Assert.Equal(
            first.Deadline.ExpirationTickExclusive - observationTick,
            first.TicksUntilExpiration);
    }

    [Fact]
    public void Observe_AppliesOneArbitrarySignedTickToAllDeadlines()
    {
        var tick = -BigInteger.Pow(2, 130);

        var snapshot = Observe(DeadlineProjection(), tick);
        Assert.All(snapshot.Observations, observation =>
        {
            Assert.Equal(tick, observation.ObservationTick);
            Assert.Equal(State.Pending, observation.State);
        });
    }

    [Fact]
    public void Observe_RejectsMissingSource()
    {
        Assert.Equal(
            Result.MissingDeadlineProjection,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObserver
                .Observe(null, 0, out var snapshot));
        Assert.Null(snapshot);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationSnapshot
            Observe(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineProjection
                    source,
                BigInteger observationTick)
    {
        Assert.Equal(
            Result.Observed,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObserver
                .Observe(source, observationTick, out var snapshot));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationSnapshot>(
                snapshot);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineProjection
            DeadlineProjection() =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineProjectionTests
            .Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineProjectionTests
                    .ClockOrigin(100));
}
