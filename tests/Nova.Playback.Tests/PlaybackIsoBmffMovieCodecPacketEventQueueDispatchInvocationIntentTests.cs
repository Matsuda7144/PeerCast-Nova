namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentTests
{
    [Fact]
    public void Project_RetainsExactLookupAndCallerInvocationIdentity()
    {
        var source = LookupProjection();
        var invocationId = Guid.NewGuid();

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentResult
                .Projected,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentProjector
                .Project(source, invocationId, out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentProjection>(
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
            Assert.Same(source.Lookups[index].Resolution.Request,
                intent.Request);
            Assert.Equal(source.Lookups[index].RuntimeInstanceId,
                intent.RuntimeInstanceId);
            Assert.Equal(source.Lookups[index].Generation,
                intent.RuntimeGeneration);
        }
    }

    [Fact]
    public void Project_RejectsMissingSourceAndEmptyInvocationId()
    {
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentResult
                .MissingRuntimeTargetLookupProjection,
            null,
            Guid.NewGuid());
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentResult
                .InvalidInvocationId,
            LookupProjection(),
            Guid.Empty);
    }

    [Fact]
    public void Project_RejectsMismatchedLookupOrder()
    {
        var source = LookupProjection();
        var lookups = source.Lookups.Reverse().ToArray();
        var mismatched = new
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupProjection(
                source.Source,
                source.Registry,
                Array.AsReadOnly(lookups));

        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentResult
                .SourceMismatch,
            mismatched,
            Guid.NewGuid());
    }

    [Fact]
    public void Project_RejectsInvalidRuntimeGeneration()
    {
        var source = LookupProjection();
        var first = source.Lookups[0];
        var invalidRegistration = new
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistration(
                first.Registration.Declaration with { Generation = 0 });
        var lookups = source.Lookups.ToArray();
        lookups[0] = new
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookup(
                first.Resolution,
                invalidRegistration);
        var invalid = new
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupProjection(
                source.Source,
                source.Registry,
                Array.AsReadOnly(lookups));

        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentResult
                .InvalidRuntimeIdentity,
            invalid,
            Guid.NewGuid());
    }

    private static void AssertResult(
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentResult
            expected,
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupProjection?
            source,
        Guid invocationId)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentProjector
                .Project(source, invocationId, out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentProjection
        IntentProjection()
    {
        var source = LookupProjection();
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentResult
                .Projected,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentProjector
                .Project(source, Guid.NewGuid(), out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentProjection>(
                projection);
    }

    private static
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupProjection
        LookupProjection()
    {
        var requests =
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionTests
                .RequestProjection();
        var capabilities =
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionTests
                .CapabilitySet(requests);
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionResult
                .Resolved,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolver
                .Resolve(requests, capabilities, out var resolutions));
        var declarations = requests.Requests.Select(request => new
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetDeclaration(
                request.TargetId,
                Guid.NewGuid(),
                request.MediaKind,
                1)).ToArray();
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryCreationResult
                .Created,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryFactory
                .TryCreate(declarations, out var registry));
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupResult
                .LookedUp,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupProjector
                .Project(resolutions, registry, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupProjection>(
                projection);
    }
}
