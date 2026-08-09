using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelectionResult
{
    Selected,
    MissingEligibilityProjection,
    EventLimitExceeded,
    InvalidMaximumCandidateCount,
    SourceMismatch,
}

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidate
{
    internal PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidate(
        int sourceIndex,
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibility
            eligibility)
    {
        SourceIndex = sourceIndex;
        Eligibility = eligibility;
    }

    public int SourceIndex { get; }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibility
        Eligibility
    { get; }

    public PlaybackIsoBmffMovieCodecPacketEventQueueEntry Entry =>
        Eligibility.Observation.Deadline.Time.Time.Time.Entry;
}

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelection
{
    internal
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelection(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityProjection
                source,
            int maximumCandidateCount,
            int eligibleCount,
            ReadOnlyCollection<
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidate>
                candidates)
    {
        Source = source;
        MaximumCandidateCount = maximumCandidateCount;
        EligibleCount = eligibleCount;
        Candidates = candidates;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityProjection
        Source
    { get; }

    public int MaximumCandidateCount { get; }

    public int EligibleCount { get; }

    public IReadOnlyList<
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidate> Candidates
    {
        get;
    }

    public bool HasMoreEligible => EligibleCount > Candidates.Count;
}

public static class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelector
{
    public static
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelectionResult
        Select(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityProjection?
                source,
            int maximumCandidateCount,
            out PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelection?
                selection)
    {
        selection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelectionResult
                    .MissingEligibilityProjection;
        }

        if (source.Entries.Count >
            PlaybackIsoBmffMovieCodecPacketEventProjector.MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelectionResult
                    .EventLimitExceeded;
        }

        if (maximumCandidateCount is < 1 or >
            PlaybackIsoBmffMovieCodecPacketEventProjector.MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelectionResult
                    .InvalidMaximumCandidateCount;
        }

        if (source.Entries.Count != source.Source.Observations.Count)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelectionResult
                    .SourceMismatch;
        }

        var candidates = new List<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidate>(
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
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelectionResult
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
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelectionResult
                .Selected;
    }
}
