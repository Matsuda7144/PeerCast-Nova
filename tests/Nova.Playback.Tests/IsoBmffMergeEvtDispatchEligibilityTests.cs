using Kind = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityKind;
using Policy = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityPolicy;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityTests
{
    [Theory]
    [InlineData(99, Policy.ReadyOnly, Kind.IneligiblePending)]
    [InlineData(100, Policy.ReadyOnly, Kind.EligibleReady)]
    [InlineData(101, Policy.ReadyOnly, Kind.IneligibleExpired)]
    [InlineData(101, Policy.ReadyOrExpired, Kind.EligibleExpired)]
    public void TryProject_AppliesCallerExplicitLatePolicy(
        int observationTick,
        Policy policy,
        Kind expected)
    {
        var source = Observation(observationTick);

        var projection = Project(source, policy);
        Assert.Same(source, projection.Source);
        Assert.Equal(policy, projection.Policy);
        Assert.Same(source.Observations[0],
            projection.Entries[0].Observation);
        Assert.Equal(expected, projection.Entries[0].Kind);
        Assert.Equal(
            expected is Kind.EligibleReady or Kind.EligibleExpired,
            projection.Entries[0].IsEligible);
    }

    [Fact]
    public void TryProject_RejectsMissingAndInvalidPolicy()
    {
        AssertResult(Result.MissingObservationSnapshot, null, Policy.ReadyOnly);
        AssertResult(Result.InvalidPolicy, Observation(100), (Policy)99);
    }

    [Fact]
    public void TryProject_RejectsMismatchedObservationOrder()
    {
        var source = Observation(100);
        var mismatched = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationSnapshot(
                source.Source,
                source.ObservationTick,
                Array.AsReadOnly(source.Observations.Reverse().ToArray()));

        AssertResult(Result.SourceMismatch, mismatched, Policy.ReadyOnly);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationSnapshot?
            source,
        Policy policy)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityProjector
                .TryProject(source, policy, out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityProjection
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationSnapshot
                    source,
                Policy policy)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityProjector
                .TryProject(source, policy, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityProjection>(
                projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationSnapshot
            Observation(int observationTick) =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationTests
            .Observe(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationTests
                    .DeadlineProjection(),
                observationTick);
}
