namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjectionResult
{
    Projected,
    MissingRegistrySourceProjection,
    SourceMismatch,
    ResolutionLimitExceeded,
    MissingRegistration,
    MediaMismatch,
    InvalidGeneration,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjection
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjection(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistrySourceProjection
                source,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupProjection
                lookupProjection)
    {
        Source = source;
        LookupProjection = lookupProjection;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistrySourceProjection
            Source
    { get; }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupProjection
            LookupProjection
    { get; }
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjectionResult
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistrySourceProjection?
                    source,
                out
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjectionResult
                    .MissingRegistrySourceProjection;
        }

        var resolutions = source.Source.ResolutionProjection;
        if (!ReferenceEquals(
                resolutions.Source,
                source.Source.Source.Source.RequestProjection))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjectionResult
                    .SourceMismatch;
        }

        var result =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupProjector
                .Project(
                    resolutions,
                    source.Registry,
                    out var lookupProjection);
        if (result !=
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupResult
                    .LookedUp ||
            lookupProjection is null)
        {
            return result switch
            {
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupResult.ResolutionLimitExceeded => PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjectionResult.ResolutionLimitExceeded,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupResult.MissingRegistration => PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjectionResult.MissingRegistration,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupResult.MediaMismatch => PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjectionResult.MediaMismatch,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupResult.InvalidGeneration => PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjectionResult.InvalidGeneration,
                _ => PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjectionResult.SourceMismatch,
            };
        }

        if (!ReferenceEquals(lookupProjection.Source, resolutions) ||
            !ReferenceEquals(lookupProjection.Registry, source.Registry))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjectionResult
                    .SourceMismatch;
        }

        projection = new(source, lookupProjection);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjectionResult
                .Projected;
    }
}
