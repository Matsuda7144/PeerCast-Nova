namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjectionResult
{
    Projected,
    MissingIntentSourceProjection,
    SourceMismatch,
    MissingDisposition,
    IntentLimitExceeded,
    DispositionCountMismatch,
    InvalidDisposition,
    InvalidInvocationIdentity,
    InvalidRuntimeIdentity,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjection
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjection(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjection
                source,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionProjection
                admissionProjection)
    {
        Source = source;
        AdmissionProjection = admissionProjection;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjection
            Source
    { get; }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionProjection
            AdmissionProjection
    { get; }
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjectionResult
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjection?
                    source,
                IReadOnlyList<
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionDisposition>?
                    dispositions,
                out
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjectionResult
                    .MissingIntentSourceProjection;
        }

        if (!ReferenceEquals(
                source.IntentProjection.Source,
                source.Source.LookupProjection))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjectionResult
                    .SourceMismatch;
        }

        var result =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionProjector
                .Project(
                    source.IntentProjection,
                    dispositions,
                    out var admissionProjection);
        if (result !=
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionResult
                    .Projected ||
            admissionProjection is null)
        {
            return result switch
            {
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionResult.MissingDisposition => PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjectionResult.MissingDisposition,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionResult.IntentLimitExceeded => PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjectionResult.IntentLimitExceeded,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionResult.DispositionCountMismatch => PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjectionResult.DispositionCountMismatch,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionResult.InvalidDisposition => PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjectionResult.InvalidDisposition,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionResult.InvalidInvocationIdentity => PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjectionResult.InvalidInvocationIdentity,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionResult.InvalidRuntimeIdentity => PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjectionResult.InvalidRuntimeIdentity,
                _ => PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjectionResult.SourceMismatch,
            };
        }

        if (!ReferenceEquals(
                admissionProjection.Source,
                source.IntentProjection))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjectionResult
                    .SourceMismatch;
        }

        projection = new(source, admissionProjection);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjectionResult
                .Projected;
    }
}
