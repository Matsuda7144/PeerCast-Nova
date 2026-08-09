using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelectionResult
{
    Selected,
    InvalidInput,
    InvalidMaximumIntentCount,
    ClassificationLimitExceeded,
    SourceMismatch,
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelectionCandidate
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelectionCandidate(
            int sourceIndex,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassification
                classification)
    {
        SourceIndex = sourceIndex;
        Classification = classification;
    }

    public int SourceIndex { get; }

    public PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassification
        Classification
    { get; }

    public PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntent Intent =>
        Classification.Intent;
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelection
{
    internal PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelection(
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassificationProjection
            source,
        int maximumIntentCount,
        int includedCount,
        ReadOnlyCollection<
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelectionCandidate>
            candidates)
    {
        Source = source;
        MaximumIntentCount = maximumIntentCount;
        IncludedCount = includedCount;
        Candidates = candidates;
    }

    public
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassificationProjection
            Source
    { get; }

    public int MaximumIntentCount { get; }

    public int IncludedCount { get; }

    public IReadOnlyList<
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelectionCandidate>
        Candidates
    { get; }

    public int CandidateCount => Candidates.Count;

    public bool IsTruncated => IncludedCount > CandidateCount;
}

public static class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelector
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelectionResult
            Select(
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassificationProjection?
                    source,
                int maximumIntentCount,
                out
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelection?
                        selection)
    {
        selection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelectionResult
                    .InvalidInput;
        }

        if (maximumIntentCount <= 0 ||
            maximumIntentCount >
            PlaybackIsoBmffFragmentSampleMergeTraversalOptions
                .MaximumStepCountHardLimit)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelectionResult
                    .InvalidMaximumIntentCount;
        }

        if (source.Classifications.Count >
            PlaybackIsoBmffFragmentSampleMergeTraversalOptions
                .MaximumStepCountHardLimit)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelectionResult
                    .ClassificationLimitExceeded;
        }

        var candidates =
            new List<
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelectionCandidate>(
                    Math.Min(maximumIntentCount, source.IncludedCount));
        var includedCount = 0;
        for (var index = 0; index < source.Classifications.Count; index++)
        {
            var classification = source.Classifications[index];
            if (!ReferenceEquals(
                    classification.Intent,
                    source.Source.Intents[index]))
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelectionResult
                        .SourceMismatch;
            }

            if (!classification.IsIncluded)
            {
                continue;
            }

            includedCount++;
            if (candidates.Count < maximumIntentCount)
            {
                candidates.Add(new(index, classification));
            }
        }

        if (includedCount != source.IncludedCount)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelectionResult
                    .SourceMismatch;
        }

        selection = new(
            source,
            maximumIntentCount,
            includedCount,
            Array.AsReadOnly(candidates.ToArray()));
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelectionResult
                .Selected;
    }
}
