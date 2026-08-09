namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionSourceProjectionResult
{
    Projected,
    InvalidInput,
    InvalidCombinationPolicy,
    InvalidMaximumCandidateCount,
    SourceMismatch,
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionSourceProjection
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionSourceProjection(
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilitySourceProjection
                source,
            PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelection selection)
    {
        Source = source;
        Selection = selection;
    }

    public
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilitySourceProjection
            Source
    { get; }

    public PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelection Selection
    { get; }
}

public static class
    PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionSourceProjector
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionSourceProjectionResult
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilitySourceProjection?
                    source,
                PlaybackIsoBmffFragmentSampleMergeTraversalEligibilityCombinationPolicy
                    combinationPolicy,
                int maximumCandidateCount,
                out
                    PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionSourceProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionSourceProjectionResult
                    .InvalidInput;
        }

        if (!ReferenceEquals(source.Eligibility.Source, source.Source.Snapshot))
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionSourceProjectionResult
                    .SourceMismatch;
        }

        var result =
            PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelector.Select(
                source.Eligibility,
                combinationPolicy,
                maximumCandidateCount,
                out var selection);
        if (result !=
                PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionResult
                    .Selected ||
            selection is null)
        {
            return result switch
            {
                PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionResult
                    .InvalidCombinationPolicy =>
                    PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionSourceProjectionResult
                        .InvalidCombinationPolicy,
                PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionResult
                    .InvalidMaximumCandidateCount =>
                    PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionSourceProjectionResult
                        .InvalidMaximumCandidateCount,
                _ =>
                    PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionSourceProjectionResult
                        .SourceMismatch,
            };
        }

        if (!ReferenceEquals(selection.Source, source.Eligibility) ||
            selection.CombinationPolicy != combinationPolicy ||
            selection.MaximumCandidateCount != maximumCandidateCount)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionSourceProjectionResult
                    .SourceMismatch;
        }

        projection = new(source, selection);
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionSourceProjectionResult
                .Projected;
    }
}
