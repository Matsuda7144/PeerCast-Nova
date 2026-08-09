using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentTests
{
    [Fact]
    public void Project_RetainsExactLookupAndCallerInvocationIdentity()
    {
        var source = LookupProjection();
        var invocationId = Guid.NewGuid();

        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentProjector
                .Project(source, invocationId, out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentProjection>(
                projection);
        Assert.Same(source, projected.Source);
        Assert.Equal(invocationId, projected.InvocationId);
        Assert.Equal(source.Lookups.Count, projected.Intents.Count);
        for (var index = 0; index < projected.Intents.Count; index++)
        {
            var intent = projected.Intents[index];
            Assert.Equal(invocationId, intent.InvocationId);
            Assert.Equal(index, intent.SequenceIndex);
            Assert.Same(source.Lookups[index], intent.Lookup);
            Assert.Same(source.Lookups[index].Resolution.Request, intent.Request);
            Assert.Equal(
                source.Lookups[index].RuntimeInstanceId,
                intent.RuntimeInstanceId);
            Assert.Equal(
                source.Lookups[index].Generation,
                intent.RuntimeGeneration);
        }
    }

    [Fact]
    public void Project_RejectsMissingSourceAndEmptyInvocationId()
    {
        AssertResult(Result.MissingRuntimeTargetLookupProjection, null, Guid.NewGuid());
        AssertResult(Result.InvalidInvocationId, LookupProjection(), Guid.Empty);
    }

    [Fact]
    public void Project_RejectsMismatchedLookupOrder()
    {
        var source = LookupProjection();
        var mismatched = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupProjection(
                source.Source,
                source.Registry,
                Array.AsReadOnly(source.Lookups.Reverse().ToArray()));

        AssertResult(Result.SourceMismatch, mismatched, Guid.NewGuid());
    }

    [Fact]
    public void Project_RejectsInvalidRuntimeGeneration()
    {
        var source = LookupProjection();
        var first = source.Lookups[0];
        var invalidRegistration = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistration(
                first.Registration.Declaration with { Generation = 0 });
        var lookups = source.Lookups.ToArray();
        lookups[0] = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookup(
                first.Resolution,
                invalidRegistration);
        var invalid = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupProjection(
                source.Source,
                source.Registry,
                Array.AsReadOnly(lookups));

        AssertResult(Result.InvalidRuntimeIdentity, invalid, Guid.NewGuid());
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupProjection?
            source,
        Guid invocationId)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentProjector
                .Project(source, invocationId, out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentProjection
            IntentProjection()
    {
        var source = LookupProjection();
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentProjector
                .Project(source, Guid.NewGuid(), out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentProjection>(
                projection);
    }

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupProjection
            LookupProjection() =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupTests
            .LookupProjection();
}
