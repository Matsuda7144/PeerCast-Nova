namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupTests
{
    [Fact]
    public void Project_RetainsExactResolutionAndOpaqueRuntimeRegistration()
    {
        var source = ResolutionProjection();
        var registry = Registry(source);

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupResult
                .LookedUp,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupProjector
                .Project(source, registry, out var projection));
        var lookedUp = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupProjection>(
                projection);
        Assert.Same(source, lookedUp.Source);
        Assert.Same(registry, lookedUp.Registry);
        Assert.Equal(source.Resolutions.Count, lookedUp.Lookups.Count);
        foreach (var lookup in lookedUp.Lookups)
        {
            Assert.Same(
                source.Resolutions.Single(
                    resolution => resolution.Request.TargetId ==
                        lookup.Registration.TargetId),
                lookup.Resolution);
            Assert.Equal(lookup.Registration.RuntimeInstanceId,
                lookup.RuntimeInstanceId);
            Assert.Equal(lookup.Registration.Generation, lookup.Generation);
        }
    }

    [Fact]
    public void Project_RejectsMissingInputs()
    {
        var source = ResolutionProjection();
        var registry = Registry(source);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupResult
                .MissingTargetResolutionProjection,
            null,
            registry);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupResult
                .MissingRuntimeTargetRegistry,
            source,
            null);
    }

    [Fact]
    public void Project_RejectsMissingRuntimeRegistration()
    {
        var source = ResolutionProjection();
        var first = source.Resolutions[0];
        var registry = Registry([
            new(
                first.Request.TargetId,
                Guid.NewGuid(),
                first.Request.MediaKind,
                1),
        ]);

        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupResult
                .MissingRegistration,
            source,
            registry);
    }

    [Fact]
    public void Project_RejectsRuntimeMediaMismatch()
    {
        var source = ResolutionProjection();
        var declarations = source.Resolutions.Select(resolution => new
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetDeclaration(
                resolution.Request.TargetId,
                Guid.NewGuid(),
                resolution.Request.MediaKind,
                1)).ToArray();
        declarations[0] = declarations[0] with
        {
            MediaKind = declarations[0].MediaKind ==
                PlaybackIsoBmffHandlerKind.Video
                    ? PlaybackIsoBmffHandlerKind.Audio
                    : PlaybackIsoBmffHandlerKind.Video,
        };
        declarations[1] = declarations[1] with
        {
            MediaKind = declarations[0].MediaKind ==
                PlaybackIsoBmffHandlerKind.Video
                    ? PlaybackIsoBmffHandlerKind.Audio
                    : PlaybackIsoBmffHandlerKind.Video,
        };

        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupResult
                .MediaMismatch,
            source,
            Registry(declarations));
    }

    private static void AssertResult(
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupResult
            expected,
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionProjection?
            source,
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistry?
            registry)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupProjector
                .Project(source, registry, out var projection));
        Assert.Null(projection);
    }

    private static
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionProjection
        ResolutionProjection()
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
                .Resolve(requests, capabilities, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionProjection>(
                projection);
    }

    private static
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistry
        Registry(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionProjection
                source) =>
        Registry(source.Resolutions.Select(resolution => new
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetDeclaration(
                resolution.Request.TargetId,
                Guid.NewGuid(),
                resolution.Request.MediaKind,
                1)).ToArray());

    private static
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistry
        Registry(
            IReadOnlyList<
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetDeclaration>
                declarations)
    {
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryCreationResult
                .Created,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryFactory
                .TryCreate(declarations, out var registry));
        return Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistry>(
                registry);
    }
}
