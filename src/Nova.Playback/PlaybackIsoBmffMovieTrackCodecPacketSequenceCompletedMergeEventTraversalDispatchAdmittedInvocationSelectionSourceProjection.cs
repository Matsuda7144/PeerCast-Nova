namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionSourceProjectionResult
{
    Projected,
    MissingAdmissionSourceProjection,
    SourceMismatch,
    InvalidMaximumSelectionCount,
    AdmissionLimitExceeded,
    InvalidDisposition,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionSourceProjection
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionSourceProjection(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjection
                source,
            int maximumSelectionCount,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelection
                selection)
    {
        Source = source;
        MaximumSelectionCount = maximumSelectionCount;
        Selection = selection;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjection
            Source
    { get; }

    public int MaximumSelectionCount { get; }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelection
            Selection
    { get; }
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionSourceProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionSourceProjectionResult
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjection?
                    source,
                int maximumSelectionCount,
                out
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionSourceProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionSourceProjectionResult
                    .MissingAdmissionSourceProjection;
        }

        if (!ReferenceEquals(
                source.AdmissionProjection.Source,
                source.Source.IntentProjection))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionSourceProjectionResult
                    .SourceMismatch;
        }

        var result =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelector
                .Select(
                    source.AdmissionProjection,
                    maximumSelectionCount,
                    out var selection);
        if (result !=
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionResult
                    .Selected ||
            selection is null)
        {
            return result switch
            {
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionResult.InvalidMaximumSelectionCount => PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionSourceProjectionResult.InvalidMaximumSelectionCount,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionResult.AdmissionLimitExceeded => PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionSourceProjectionResult.AdmissionLimitExceeded,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionResult.InvalidDisposition => PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionSourceProjectionResult.InvalidDisposition,
                _ => PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionSourceProjectionResult.SourceMismatch,
            };
        }

        if (!ReferenceEquals(selection.Source, source.AdmissionProjection) ||
            selection.MaximumSelectionCount != maximumSelectionCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionSourceProjectionResult
                    .SourceMismatch;
        }

        projection = new(source, maximumSelectionCount, selection);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionSourceProjectionResult
                .Projected;
    }
}
