using System.Numerics;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationResult;
using State = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineState;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationTests
{
    [Theory]
    [InlineData(-91, State.Pending, State.Pending)]
    [InlineData(-90, State.Pending, State.Ready)]
    [InlineData(-79, State.Ready, State.Ready)]
    [InlineData(-68, State.Ready, State.Expired)]
    [InlineData(-57, State.Expired, State.Expired)]
    public void Observe_ClassifiesExactDecodeAndPresentationBoundaries(
        int observationTick,
        State expectedDecode,
        State expectedPresentation)
    {
        var source = Deadlines();

        var snapshot = Observe(source, observationTick);

        Assert.Same(source, snapshot.Source);
        Assert.Equal(new BigInteger(observationTick), snapshot.ObservationTick);
        var first = snapshot.Observations[0];
        Assert.Same(source.Deadlines[0], first.Deadline);
        Assert.Equal(expectedDecode, first.DecodeState);
        Assert.Equal(expectedPresentation, first.PresentationState);
        Assert.Equal(
            first.Deadline.DecodeReadyTick - observationTick,
            first.TicksUntilDecodeReady);
        Assert.Equal(
            first.Deadline.PresentationExpirationTickExclusive -
                observationTick,
            first.TicksUntilPresentationExpiration);
    }

    [Fact]
    public void Observe_AppliesOneArbitrarySignedTickToAllDeadlines()
    {
        var tick = -BigInteger.Pow(2, 130);

        var snapshot = Observe(Deadlines(), tick);

        Assert.All(snapshot.Observations, observation =>
        {
            Assert.Equal(tick, observation.ObservationTick);
            Assert.Equal(State.Pending, observation.DecodeState);
            Assert.Equal(State.Pending, observation.PresentationState);
        });
    }

    [Fact]
    public void Observe_AcceptsEmptySource()
    {
        var integral =
            PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjectionTests
                .Project(
                    PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjectionTests
                        .Time(0, 0),
                    1_000,
                    PlaybackIsoBmffClockTickRoundingMode.Floor,
                    PlaybackIsoBmffClockTickRoundingMode.Ceiling);
        var origin =
            PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjectionTests
                .Project(integral, 0);
        var deadlines =
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjectionTests
                .Project(origin);

        Assert.Empty(Observe(deadlines, 0).Observations);
    }

    [Fact]
    public void Observe_RejectsMissingSource()
    {
        Assert.Equal(
            Result.InvalidInput,
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObserver.Observe(
                null,
                0,
                out var snapshot));
        Assert.Null(snapshot);
    }

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSnapshot
            Observe(
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjection
                    source,
                BigInteger observationTick)
    {
        Assert.Equal(
            Result.Observed,
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObserver.Observe(
                source,
                observationTick,
                out var snapshot));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSnapshot>(
                snapshot);
    }

    internal static PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjection
        Deadlines() =>
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjectionTests
            .Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjectionTests
                    .ClockOrigin(-100));
}
