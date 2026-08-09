namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionSourceProjectionResult
{
    Projected,
    MissingEligibilitySourceProjection,
    SourceMismatch,
    EventLimitExceeded,
    InvalidMaximumCandidateCount,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionSourceProjection
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionSourceProjection(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilitySourceProjection
                source,
            int maximumCandidateCount,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelection
                candidateSelection)
    {
        Source = source;
        MaximumCandidateCount = maximumCandidateCount;
        CandidateSelection = candidateSelection;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilitySourceProjection
            Source
    { get; }

    public int MaximumCandidateCount { get; }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelection
            CandidateSelection
    { get; }
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionSourceProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionSourceProjectionResult
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilitySourceProjection?
                    source,
                int maximumCandidateCount,
                out
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionSourceProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionSourceProjectionResult
                    .MissingEligibilitySourceProjection;
        }

        if (!ReferenceEquals(
                source.EligibilityProjection.Source,
                source.Source.ObservationSnapshot))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionSourceProjectionResult
                    .SourceMismatch;
        }

        var result =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelector
                .Select(
                    source.EligibilityProjection,
                    maximumCandidateCount,
                    out var selection);
        if (result !=
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionResult
                    .Selected ||
            selection is null)
        {
            return result switch
            {
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionResult
                    .EventLimitExceeded =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionSourceProjectionResult
                        .EventLimitExceeded,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionResult
                    .InvalidMaximumCandidateCount =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionSourceProjectionResult
                        .InvalidMaximumCandidateCount,
                _ =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionSourceProjectionResult
                        .SourceMismatch,
            };
        }

        if (!ReferenceEquals(
                selection.Source,
                source.EligibilityProjection) ||
            selection.MaximumCandidateCount != maximumCandidateCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionSourceProjectionResult
                    .SourceMismatch;
        }

        projection = new(source, maximumCandidateCount, selection);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionSourceProjectionResult
                .Projected;
    }
}
