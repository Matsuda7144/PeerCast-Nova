namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorStepResult
{
    Advanced,
    InvalidInput,
    SourceMismatch,
    EndReached,
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorStep
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorStep(
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursor
                source,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegment
                segment,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursor
                next)
    {
        Source = source;
        Segment = segment;
        Next = next;
    }

    public
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursor
            Source
    { get; }

    public PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegment
        Segment
    { get; }

    public
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursor
            Next
    { get; }
}

public static class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorStepper
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorStepResult
            Advance(
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursor?
                    source,
                out
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorStep?
                        step)
    {
        step = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorStepResult
                    .InvalidInput;
        }

        if (source.NextSegmentIndex < 0 ||
            source.NextSegmentIndex > source.Source.SegmentCount)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorStepResult
                    .SourceMismatch;
        }

        if (!source.HasCurrent || source.Current is not { } segment)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorStepResult
                    .EndReached;
        }

        if (!ReferenceEquals(
                segment,
                source.Source.Segments[source.NextSegmentIndex]))
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorStepResult
                    .SourceMismatch;
        }

        var result =
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorProjector
                .Project(
                    source.Source,
                    source.NextSegmentIndex + 1,
                    out var next);
        if (result !=
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorProjectionResult
                    .Projected ||
            next is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorStepResult
                    .SourceMismatch;
        }

        step = new(source, segment, next);
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorStepResult
                .Advanced;
    }
}
