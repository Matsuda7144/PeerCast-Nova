using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistrySourceProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistrySourceProjectionTests
{
    [Fact]
    public void Project_RetainsResolutionSourceAndOpaqueRegistrations()
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionSourceProjectionTests
                .Projection();
        var declarations = Declarations(source);

        var projection = Project(source, declarations);
        Assert.Same(source, projection.Source);
        Assert.Equal(declarations.Length, projection.Registry.Registrations.Count);
        for (var index = 0; index < declarations.Length; index++)
        {
            Assert.Equal(
                declarations[index],
                projection.Registry.Registrations[index].Declaration);
        }
    }

    [Fact]
    public void Project_RejectsMissingSourceAndDeclarations()
    {
        AssertResult(Result.MissingResolutionSourceProjection, null, null);
        AssertResult(
            Result.MissingDeclaration,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionSourceProjectionTests
                .Projection(),
            null);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistrySourceProjection
            Projection()
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionSourceProjectionTests
                .Projection();
        return Project(source, Declarations(source));
    }

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistrySourceProjection
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionSourceProjection
                    source,
                IReadOnlyList<
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetDeclaration>
                    declarations)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistrySourceProjector
                .Project(source, declarations, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistrySourceProjection>(
                projection);
    }

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetDeclaration[]
            Declarations(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionSourceProjection
                    source) =>
        source.ResolutionProjection.Resolutions
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

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionSourceProjection?
            source,
        IReadOnlyList<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetDeclaration>?
            declarations)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistrySourceProjector
                .Project(source, declarations, out var projection));
        Assert.Null(projection);
    }
}
