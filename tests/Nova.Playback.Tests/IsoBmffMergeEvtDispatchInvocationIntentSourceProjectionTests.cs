using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjectionTests
{
    [Fact]
    public void Project_RetainsLookupSourceAndCallerInvocationIdentity()
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjectionTests
                .Projection();
        var invocationId = Guid.NewGuid();

        var projection = Project(source, invocationId);
        Assert.Same(source, projection.Source);
        Assert.Equal(invocationId, projection.InvocationId);
        Assert.Same(
            source.LookupProjection,
            projection.IntentProjection.Source);
        Assert.Equal(
            source.LookupProjection.Lookups.Count,
            projection.IntentProjection.Intents.Count);
    }

    [Fact]
    public void Project_RejectsMissingSourceAndEmptyIdentity()
    {
        AssertResult(Result.MissingLookupSourceProjection, null, Guid.NewGuid());
        AssertResult(
            Result.InvalidInvocationId,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjectionTests
                .Projection(),
            Guid.Empty);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjection
            Projection() =>
        Project(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjectionTests
                .Projection(),
            Guid.NewGuid());

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjection
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjection
                    source,
                Guid invocationId)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjector
                .Project(source, invocationId, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjection>(
                projection);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjection?
            source,
        Guid invocationId)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjector
                .Project(source, invocationId, out var projection));
        Assert.Null(projection);
    }
}
