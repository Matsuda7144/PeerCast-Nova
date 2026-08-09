using System.Numerics;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservationTests
{
    [Theory]
    [InlineData(99, PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineState.Pending)]
    [InlineData(100, PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineState.Ready)]
    [InlineData(101, PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineState.Expired)]
    public void Observe_ClassifiesExactDeadlineBoundaries(
        int observationTick,
        PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineState expected)
    {
        var source = DeadlineProjection();

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservationResult
                .Observed,
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObserver.Observe(
                source,
                observationTick,
                out var snapshot));
        var observed = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservationSnapshot>(
                snapshot);
        Assert.Same(source, observed.Source);
        Assert.Equal(new BigInteger(observationTick),
            observed.ObservationTick);
        var first = observed.Observations[0];
        Assert.Same(source.Deadlines[0], first.Deadline);
        Assert.Equal(expected, first.State);
        Assert.Equal(source.Deadlines[0].ReadyTick - observationTick,
            first.TicksUntilReady);
        Assert.Equal(
            source.Deadlines[0].ExpirationTickExclusive - observationTick,
            first.TicksUntilExpiration);
    }

    [Fact]
    public void Observe_AppliesOneSignedBigIntegerTickToAllDeadlines()
    {
        var source = DeadlineProjection();
        var tick = -BigInteger.Pow(2, 130);

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservationResult
                .Observed,
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObserver.Observe(
                source,
                tick,
                out var snapshot));
        var observed = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservationSnapshot>(
                snapshot);
        Assert.All(observed.Observations, observation =>
        {
            Assert.Equal(tick, observation.ObservationTick);
            Assert.Equal(
                PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineState.Pending,
                observation.State);
        });
    }

    [Fact]
    public void Observe_RejectsMissingSource()
    {
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservationResult
                .MissingDeadlineProjection,
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObserver.Observe(
                null,
                0,
                out var snapshot));
        Assert.Null(snapshot);
    }

    [Fact]
    public void Observe_RejectsMismatchedSourceDeadlines()
    {
        var source = DeadlineProjection();
        var deadlines = source.Deadlines.Reverse().ToArray();
        var mismatched = new
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjection(
                source.Source,
                Array.AsReadOnly(deadlines));

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservationResult
                .SourceMismatch,
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObserver.Observe(
                mismatched,
                100,
                out var snapshot));
        Assert.Null(snapshot);
    }

    private static PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjection
        DeadlineProjection()
    {
        var clockOrigin =
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjectionTests
                .ClockOriginProjection(100);
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjectionResult
                .Projected,
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjector.Project(
                clockOrigin,
                out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjection>(
                projection);
    }
}
