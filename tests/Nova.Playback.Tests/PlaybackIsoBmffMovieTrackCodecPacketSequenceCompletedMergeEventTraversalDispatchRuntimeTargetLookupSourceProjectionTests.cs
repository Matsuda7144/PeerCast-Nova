using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjectionTests
{
    [Fact]
    public void Project_RetainsExactResolutionsAndOpaqueRegistrations()
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistrySourceProjectionTests
                .Projection();

        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjector
                .Project(source, out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjection>(
                projection);
        Assert.Same(source, projected.Source);
        Assert.Same(
            source.Source.ResolutionProjection,
            projected.LookupProjection.Source);
        Assert.Same(source.Registry, projected.LookupProjection.Registry);
        Assert.Equal(
            source.Source.ResolutionProjection.Resolutions.Count,
            projected.LookupProjection.Lookups.Count);
    }

    [Fact]
    public void Project_RejectsMissingSource()
    {
        Assert.Equal(
            Result.MissingRegistrySourceProjection,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjector
                .Project(null, out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjection
            Projection()
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistrySourceProjectionTests
                .Projection();
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjector
                .Project(source, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjection>(
                projection);
    }
}
