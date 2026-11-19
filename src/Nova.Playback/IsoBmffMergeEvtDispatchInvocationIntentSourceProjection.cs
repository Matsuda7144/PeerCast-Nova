namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjectionResult
{
    Projected,
    MissingLookupSourceProjection,
    SourceMismatch,
    InvalidInvocationId,
    LookupLimitExceeded,
    InvalidRuntimeIdentity,
    EmptyPayload,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjection
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjection(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjection
                source,
            Guid invocationId,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentProjection
                intentProjection)
    {
        Source = source;
        InvocationId = invocationId;
        IntentProjection = intentProjection;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjection
            Source
    { get; }

    public Guid InvocationId { get; }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentProjection
            IntentProjection
    { get; }
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjectionResult
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupSourceProjection?
                    source,
                Guid invocationId,
                out
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjectionResult
                    .MissingLookupSourceProjection;
        }

        if (!ReferenceEquals(
                source.LookupProjection.Source,
                source.Source.Source.ResolutionProjection) ||
            !ReferenceEquals(
                source.LookupProjection.Registry,
                source.Source.Registry))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjectionResult
                    .SourceMismatch;
        }

        var result =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentProjector
                .Project(
                    source.LookupProjection,
                    invocationId,
                    out var intentProjection);
        if (result !=
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentResult
                    .Projected ||
            intentProjection is null)
        {
            return result switch
            {
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentResult
                    .InvalidInvocationId =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjectionResult
                        .InvalidInvocationId,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentResult
                    .LookupLimitExceeded =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjectionResult
                        .LookupLimitExceeded,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentResult
                    .InvalidRuntimeIdentity =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjectionResult
                        .InvalidRuntimeIdentity,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentResult
                    .EmptyPayload =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjectionResult
                        .EmptyPayload,
                _ =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjectionResult
                        .SourceMismatch,
            };
        }

        if (!ReferenceEquals(intentProjection.Source, source.LookupProjection) ||
            intentProjection.InvocationId != invocationId)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjectionResult
                    .SourceMismatch;
        }

        projection = new(source, invocationId, intentProjection);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjectionResult
                .Projected;
    }
}
