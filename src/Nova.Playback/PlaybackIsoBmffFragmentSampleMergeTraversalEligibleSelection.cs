using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffFragmentSampleMergeTraversalEligibilityCombinationPolicy
{
    RequireBoth,
    RequireEither,
}

public enum PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionResult
{
    Selected,
    InvalidInput,
    InvalidCombinationPolicy,
    InvalidMaximumCandidateCount,
    SourceMismatch,
}

public sealed class PlaybackIsoBmffFragmentSampleMergeTraversalEligibleCandidate
{
    internal PlaybackIsoBmffFragmentSampleMergeTraversalEligibleCandidate(
        int sourceIndex,
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibility
            eligibility)
    {
        SourceIndex = sourceIndex;
        Eligibility = eligibility;
    }

    public int SourceIndex { get; }

    public PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibility
        Eligibility
    { get; }

    public PlaybackIsoBmffFragmentBatchPresentationSample Sample =>
        Eligibility.Observation.Deadline.Source.Source.Source.Sample;
}

public sealed class PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelection
{
    internal PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelection(
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityProjection
            source,
        PlaybackIsoBmffFragmentSampleMergeTraversalEligibilityCombinationPolicy
            combinationPolicy,
        int maximumCandidateCount,
        int eligibleCount,
        ReadOnlyCollection<
            PlaybackIsoBmffFragmentSampleMergeTraversalEligibleCandidate>
            candidates)
    {
        Source = source;
        CombinationPolicy = combinationPolicy;
        MaximumCandidateCount = maximumCandidateCount;
        EligibleCount = eligibleCount;
        Candidates = candidates;
    }

    public
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityProjection
            Source
    { get; }

    public
        PlaybackIsoBmffFragmentSampleMergeTraversalEligibilityCombinationPolicy
            CombinationPolicy
    { get; }

    public int MaximumCandidateCount { get; }

    public int EligibleCount { get; }

    public IReadOnlyList<
        PlaybackIsoBmffFragmentSampleMergeTraversalEligibleCandidate> Candidates
    { get; }

    public bool HasMoreEligible => EligibleCount > Candidates.Count;
}

public static class PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelector
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionResult Select(
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityProjection?
                source,
            PlaybackIsoBmffFragmentSampleMergeTraversalEligibilityCombinationPolicy
                combinationPolicy,
            int maximumCandidateCount,
            out PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelection?
                selection)
    {
        selection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionResult
                    .InvalidInput;
        }

        if (!Enum.IsDefined(combinationPolicy))
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionResult
                    .InvalidCombinationPolicy;
        }

        if (maximumCandidateCount is < 1 or
            > PlaybackIsoBmffFragmentSampleMergeTraversalOptions
                .MaximumStepCountHardLimit)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionResult
                    .InvalidMaximumCandidateCount;
        }

        if (source.SampleCount != source.Source.SampleCount)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionResult
                    .SourceMismatch;
        }

        var candidates = new List<
            PlaybackIsoBmffFragmentSampleMergeTraversalEligibleCandidate>(
                Math.Min(maximumCandidateCount, source.SampleCount));
        var eligibleCount = 0;
        for (var index = 0; index < source.SampleCount; index++)
        {
            var eligibility = source.Entries[index];
            if (!ReferenceEquals(
                    eligibility.Observation,
                    source.Source.Observations[index]))
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionResult
                        .SourceMismatch;
            }

            var isEligible = combinationPolicy switch
            {
                PlaybackIsoBmffFragmentSampleMergeTraversalEligibilityCombinationPolicy
                    .RequireBoth => eligibility.AreBothEligible,
                PlaybackIsoBmffFragmentSampleMergeTraversalEligibilityCombinationPolicy
                    .RequireEither =>
                    eligibility.IsDecodeEligible ||
                    eligibility.IsPresentationEligible,
                _ => false,
            };
            if (!isEligible)
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
            combinationPolicy,
            maximumCandidateCount,
            eligibleCount,
            candidates.AsReadOnly());
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionResult
                .Selected;
    }
}
