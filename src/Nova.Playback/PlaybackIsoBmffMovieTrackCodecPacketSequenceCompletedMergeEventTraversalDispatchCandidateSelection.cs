using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionResult
{
    Selected,
    MissingEligibilityProjection,
    EventLimitExceeded,
    InvalidMaximumCandidateCount,
    SourceMismatch,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidate
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidate(
            int sourceIndex,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibility
                eligibility)
    {
        SourceIndex = sourceIndex;
        Eligibility = eligibility;
    }

    public int SourceIndex { get; }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibility
            Eligibility
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEvent Event
        => Eligibility.Observation.Deadline.Time.Time.Time.Time.Event;

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgement
            Acknowledgement =>
        Eligibility.Observation.Deadline.Time.Time.Time.Time.Acknowledgement;
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelection
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelection(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityProjection
                source,
            int maximumCandidateCount,
            int eligibleCount,
            ReadOnlyCollection<
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidate>
                candidates)
    {
        Source = source;
        MaximumCandidateCount = maximumCandidateCount;
        EligibleCount = eligibleCount;
        Candidates = candidates;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityProjection
            Source
    { get; }

    public int MaximumCandidateCount { get; }

    public int EligibleCount { get; }

    public IReadOnlyList<
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidate>
        Candidates
    { get; }

    public bool HasMoreEligible => EligibleCount > Candidates.Count;
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionResult
            Select(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityProjection?
                    source,
                int maximumCandidateCount,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelection?
                    selection)
    {
        selection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionResult
                    .MissingEligibilityProjection;
        }

        if (source.Entries.Count >
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjector
                .MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionResult
                    .EventLimitExceeded;
        }

        if (maximumCandidateCount is < 1 or >
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjector
                .MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionResult
                    .InvalidMaximumCandidateCount;
        }

        if (source.Entries.Count != source.Source.Observations.Count)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionResult
                    .SourceMismatch;
        }

        var candidates = new List<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidate>(
                Math.Min(maximumCandidateCount, source.Entries.Count));
        var eligibleCount = 0;
        for (var index = 0; index < source.Entries.Count; index++)
        {
            var eligibility = source.Entries[index];
            if (!ReferenceEquals(
                    eligibility.Observation,
                    source.Source.Observations[index]))
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionResult
                        .SourceMismatch;
            }

            if (!eligibility.IsEligible)
            {
                continue;
            }

            eligibleCount++;
            if (candidates.Count < maximumCandidateCount)
            {
                candidates.Add(new(index, eligibility));
            }
        }

        selection = new(
            source,
            maximumCandidateCount,
            eligibleCount,
            candidates.AsReadOnly());
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionResult
                .Selected;
    }
}
