namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorProjectionResult
{
    Projected,
    InvalidInput,
    SegmentLimitExceeded,
    InvalidNextSegmentIndex,
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursor
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursor(
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentProjection
                source,
            int nextSegmentIndex)
    {
        Source = source;
        NextSegmentIndex = nextSegmentIndex;
    }

    public
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentProjection
            Source
    { get; }

    public int NextSegmentIndex { get; }

    public int RemainingSegmentCount => Source.SegmentCount - NextSegmentIndex;

    public bool HasCurrent => NextSegmentIndex < Source.SegmentCount;

    public
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegment?
            Current => HasCurrent ? Source.Segments[NextSegmentIndex] : null;
}

public static class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorProjector
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorProjectionResult
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentProjection?
                    source,
                int nextSegmentIndex,
                out
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursor?
                        cursor)
    {
        cursor = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorProjectionResult
                    .InvalidInput;
        }

        if (source.SegmentCount >
            PlaybackIsoBmffFragmentSampleMergeTraversalOptions
                .MaximumStepCountHardLimit)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorProjectionResult
                    .SegmentLimitExceeded;
        }

        if (nextSegmentIndex < 0 || nextSegmentIndex > source.SegmentCount)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorProjectionResult
                    .InvalidNextSegmentIndex;
        }

        cursor = new(source, nextSegmentIndex);
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorProjectionResult
                .Projected;
    }
}
