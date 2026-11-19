using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupTests
{
    [Fact]
    public void Project_RetainsExactResolutionAndOpaqueRuntimeRegistration()
    {
        var source = ResolutionProjection();
        var registry = Registry(source);

        Assert.Equal(
            Result.LookedUp,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupProjector
                .Project(source, registry, out var projection));
        var lookedUp = Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupProjection>(
                projection);
        Assert.Same(source, lookedUp.Source);
        Assert.Same(registry, lookedUp.Registry);
        Assert.Equal(source.Resolutions.Count, lookedUp.Lookups.Count);
        for (var index = 0; index < lookedUp.Lookups.Count; index++)
        {
            var lookup = lookedUp.Lookups[index];
            Assert.Same(source.Resolutions[index], lookup.Resolution);
            Assert.Equal(
                lookup.Resolution.Request.TargetId,
                lookup.Registration.TargetId);
            Assert.Equal(
                lookup.Registration.RuntimeInstanceId,
                lookup.RuntimeInstanceId);
            Assert.Equal(lookup.Registration.Generation, lookup.Generation);
        }
    }

    [Fact]
    public void Project_RejectsMissingInputs()
    {
        var source = ResolutionProjection();
        var registry = Registry(source);
        AssertResult(Result.MissingTargetResolutionProjection, null, registry);
        AssertResult(Result.MissingRuntimeTargetRegistry, source, null);
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

        AssertResult(Result.MissingRegistration, source, registry);
    }

    [Fact]
    public void Project_RejectsRuntimeMediaMismatch()
    {
        var source = ResolutionProjection();
        var declarations = RuntimeDeclarations(source);
        for (var index = 0; index < declarations.Length; index++)
        {
            declarations[index] = declarations[index] with
            {
                MediaKind = declarations[index].MediaKind ==
                    PlaybackIsoBmffHandlerKind.Video
                        ? PlaybackIsoBmffHandlerKind.Audio
                        : PlaybackIsoBmffHandlerKind.Video,
            };
        }

        AssertResult(Result.MediaMismatch, source, Registry(declarations));
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionProjection?
            source,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistry?
            registry)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupProjector
                .Project(source, registry, out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupProjection
            LookupProjection()
    {
        var source = ResolutionProjection();
        var registry = Registry(source);
        Assert.Equal(
            Result.LookedUp,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupProjector
                .Project(source, registry, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupProjection>(
                projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionProjection
            ResolutionProjection()
    {
        var requests =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionTests
                .RequestProjection();
        var capabilities =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionTests
                .CapabilitySet(requests);
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionResult
                .Resolved,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolver
                .Resolve(requests, capabilities, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionProjection>(
                projection);
    }

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistry
            Registry(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionProjection
                    source) =>
        Registry(RuntimeDeclarations(source));

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetDeclaration[]
            RuntimeDeclarations(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionProjection
                    source) =>
        source.Resolutions
            .GroupBy(resolution => new
            {
                resolution.Request.TargetId,
                resolution.Request.MediaKind,
            })
            .Select(group => new
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetDeclaration(
                    group.Key.TargetId,
                    Guid.NewGuid(),
                    group.Key.MediaKind,
                    1))
            .ToArray();

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistry
            Registry(
                IReadOnlyList<
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetDeclaration>
                    declarations)
    {
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistryCreationResult
                .Created,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistryFactory
                .TryCreate(declarations, out var registry));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistry>(
                registry);
    }
}
