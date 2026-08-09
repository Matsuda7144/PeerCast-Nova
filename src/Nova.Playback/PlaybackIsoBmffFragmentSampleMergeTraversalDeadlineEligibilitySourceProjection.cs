namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilitySourceProjectionResult
{
    Projected,
    InvalidInput,
    InvalidPolicy,
    SourceMismatch,
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilitySourceProjection
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilitySourceProjection(
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSourceProjection
                source,
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityProjection
                eligibility)
    {
        Source = source;
        Eligibility = eligibility;
    }

    public
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSourceProjection
            Source
    { get; }

    public
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityProjection
            Eligibility
    { get; }
}

public static class
    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilitySourceProjector
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilitySourceProjectionResult
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSourceProjection?
                    source,
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityOptions?
                    options,
                out
                    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilitySourceProjection?
                        projection)
    {
        projection = null;
        if (source is null || options is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilitySourceProjectionResult
                    .InvalidInput;
        }

        if (!ReferenceEquals(source.Snapshot.Source, source.Source.Deadlines))
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilitySourceProjectionResult
                    .SourceMismatch;
        }

        var result =
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityProjector
                .Project(source.Snapshot, options, out var eligibility);
        if (result !=
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityResult
                    .Projected ||
            eligibility is null)
        {
            return result switch
            {
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityResult
                    .InvalidPolicy =>
                    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilitySourceProjectionResult
                        .InvalidPolicy,
                _ =>
                    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilitySourceProjectionResult
                        .SourceMismatch,
            };
        }

        if (!ReferenceEquals(eligibility.Source, source.Snapshot) ||
            !ReferenceEquals(eligibility.Options, options))
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilitySourceProjectionResult
                    .SourceMismatch;
        }

        projection = new(source, eligibility);
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilitySourceProjectionResult
                .Projected;
    }
}
