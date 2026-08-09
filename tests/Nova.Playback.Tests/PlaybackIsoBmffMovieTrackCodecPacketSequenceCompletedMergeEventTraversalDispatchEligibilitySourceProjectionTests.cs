using Kind = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityKind;
using Policy = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityPolicy;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilitySourceProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilitySourceProjectionTests
{
    [Theory]
    [InlineData(99, Policy.ReadyOnly, Kind.IneligiblePending)]
    [InlineData(100, Policy.ReadyOnly, Kind.EligibleReady)]
    [InlineData(1_000, Policy.ReadyOnly, Kind.IneligibleExpired)]
    [InlineData(1_000, Policy.ReadyOrExpired, Kind.EligibleExpired)]
    public void Project_RetainsObservationSourceAndCallerPolicy(
        int observationTick,
        Policy policy,
        Kind expectedKind)
    {
        var source = Observation(observationTick);

        var projection = Project(source, policy);
        Assert.Same(source, projection.Source);
        Assert.Equal(policy, projection.Policy);
        Assert.Same(
            source.ObservationSnapshot,
            projection.EligibilityProjection.Source);
        Assert.Equal(
            expectedKind,
            projection.EligibilityProjection.Entries[0].Kind);
    }

    [Fact]
    public void Project_RejectsMissingSourceAndInvalidPolicy()
    {
        AssertResult(Result.MissingObservationSourceProjection, null, Policy.ReadyOnly);
        AssertResult(Result.InvalidPolicy, Observation(100), (Policy)99);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilitySourceProjection
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationSourceProjection
                    source,
                Policy policy)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilitySourceProjector
                .Project(source, policy, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilitySourceProjection>(
                projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationSourceProjection
            Observation(int observationTick) =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationSourceProjectionTests
            .Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineSourceProjectionTests
                    .Projection(),
                observationTick);

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationSourceProjection?
            source,
        Policy policy)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilitySourceProjector
                .Project(source, policy, out var projection));
        Assert.Null(projection);
    }
}
