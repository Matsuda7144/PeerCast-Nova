namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffFragmentSampleMergeTraversalRequestSourceProjectionResult
{
    Projected,
    InvalidInput,
    CandidateLimitExceeded,
    SourceMismatch,
    InvalidTrack,
    MediaTimescaleMismatch,
    InvalidSampleRange,
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalRequestSourceProjection
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalRequestSourceProjection(
            PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionSourceProjection
                source,
            PlaybackIsoBmffFragmentSampleMergeTraversalRequestProjection requests)
    {
        Source = source;
        Requests = requests;
    }

    public
        PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionSourceProjection
            Source
    { get; }

    public PlaybackIsoBmffFragmentSampleMergeTraversalRequestProjection Requests
    { get; }
}

public static class
    PlaybackIsoBmffFragmentSampleMergeTraversalRequestSourceProjector
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalRequestSourceProjectionResult
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionSourceProjection?
                    source,
                out
                    PlaybackIsoBmffFragmentSampleMergeTraversalRequestSourceProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalRequestSourceProjectionResult
                    .InvalidInput;
        }

        if (!ReferenceEquals(source.Selection.Source, source.Source.Eligibility))
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalRequestSourceProjectionResult
                    .SourceMismatch;
        }

        var result =
            PlaybackIsoBmffFragmentSampleMergeTraversalRequestProjector.Project(
                source.Selection,
                out var requests);
        if (result !=
                PlaybackIsoBmffFragmentSampleMergeTraversalRequestProjectionResult
                    .Projected ||
            requests is null)
        {
            return result switch
            {
                PlaybackIsoBmffFragmentSampleMergeTraversalRequestProjectionResult
                    .CandidateLimitExceeded =>
                    PlaybackIsoBmffFragmentSampleMergeTraversalRequestSourceProjectionResult
                        .CandidateLimitExceeded,
                PlaybackIsoBmffFragmentSampleMergeTraversalRequestProjectionResult
                    .InvalidTrack =>
                    PlaybackIsoBmffFragmentSampleMergeTraversalRequestSourceProjectionResult
                        .InvalidTrack,
                PlaybackIsoBmffFragmentSampleMergeTraversalRequestProjectionResult
                    .MediaTimescaleMismatch =>
                    PlaybackIsoBmffFragmentSampleMergeTraversalRequestSourceProjectionResult
                        .MediaTimescaleMismatch,
                PlaybackIsoBmffFragmentSampleMergeTraversalRequestProjectionResult
                    .InvalidSampleRange =>
                    PlaybackIsoBmffFragmentSampleMergeTraversalRequestSourceProjectionResult
                        .InvalidSampleRange,
                _ =>
                    PlaybackIsoBmffFragmentSampleMergeTraversalRequestSourceProjectionResult
                        .SourceMismatch,
            };
        }

        if (!ReferenceEquals(requests.Source, source.Selection))
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalRequestSourceProjectionResult
                    .SourceMismatch;
        }

        projection = new(source, requests);
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalRequestSourceProjectionResult
                .Projected;
    }
}
