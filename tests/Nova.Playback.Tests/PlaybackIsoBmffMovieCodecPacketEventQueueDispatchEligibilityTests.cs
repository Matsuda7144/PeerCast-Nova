namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityTests
{
    [Theory]
    [InlineData(
        99,
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityPolicy.ReadyOnly,
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityKind.IneligiblePending)]
    [InlineData(
        100,
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityPolicy.ReadyOnly,
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityKind.EligibleReady)]
    [InlineData(
        101,
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityPolicy.ReadyOnly,
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityKind.IneligibleExpired)]
    [InlineData(
        101,
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityPolicy.ReadyOrExpired,
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityKind.EligibleExpired)]
    public void Project_AppliesCallerExplicitEligibilityPolicy(
        int observationTick,
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityPolicy
            policy,
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityKind expected)
    {
        var source = Observation(observationTick);

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityResult
                .Projected,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityProjector
                .Project(source, policy, out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityProjection>(
                projection);
        Assert.Same(source, projected.Source);
        Assert.Equal(policy, projected.Policy);
        Assert.Same(source.Observations[0], projected.Entries[0].Observation);
        Assert.Equal(expected, projected.Entries[0].Kind);
        Assert.Equal(
            expected is
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityKind
                    .EligibleReady or
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityKind
                    .EligibleExpired,
            projected.Entries[0].IsEligible);
    }

    [Fact]
    public void Project_RejectsMissingSourceAndInvalidPolicy()
    {
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityResult
                .MissingObservationSnapshot,
            null,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityPolicy
                .ReadyOnly);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityResult
                .InvalidPolicy,
            Observation(100),
            (PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityPolicy)
                int.MaxValue);
    }

    [Fact]
    public void Project_RejectsMismatchedSourceObservations()
    {
        var source = Observation(100);
        var observations = source.Observations.Reverse().ToArray();
        var mismatched = new
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservationSnapshot(
                source.Source,
                source.ObservationTick,
                Array.AsReadOnly(observations));

        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityResult
                .SourceMismatch,
            mismatched,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityPolicy
                .ReadyOnly);
    }

    private static void AssertResult(
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityResult
            expected,
        PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservationSnapshot?
            source,
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityPolicy
            policy)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityProjector
                .Project(source, policy, out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservationSnapshot
        Observation(int observationTick)
    {
        var clockOrigin =
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjectionTests
                .ClockOriginProjection(100);
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjectionResult
                .Projected,
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjector.Project(
                clockOrigin,
                out var deadlines));
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservationResult
                .Observed,
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObserver.Observe(
                deadlines,
                observationTick,
                out var observation));
        return Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservationSnapshot>(
                observation);
    }
}
