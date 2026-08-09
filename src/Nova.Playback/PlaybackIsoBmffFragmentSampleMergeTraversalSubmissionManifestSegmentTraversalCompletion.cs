namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalCompletionResult
{
    Projected,
    InvalidInput,
    IncompleteTraversal,
    InvalidTraversal,
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalCompletion
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalCompletion(
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversal
                source,
            int initialSegmentIndex,
            int finalSegmentIndex,
            int segmentCount)
    {
        Source = source;
        InitialSegmentIndex = initialSegmentIndex;
        FinalSegmentIndex = finalSegmentIndex;
        SegmentCount = segmentCount;
    }

    public
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversal
            Source
    { get; }

    public
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentProjection
            Segments => Source.InitialCursor.Source;

    public int InitialSegmentIndex { get; }

    public int FinalSegmentIndex { get; }

    public int SegmentCount { get; }

    public int TraversedSegmentCount =>
        FinalSegmentIndex - InitialSegmentIndex;
}

public static class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalCompletionProjector
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalCompletionResult
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversal?
                    source,
                out
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalCompletion?
                        completion)
    {
        completion = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalCompletionResult
                    .InvalidInput;
        }

        if (!source.IsExhaustive)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalCompletionResult
                    .IncompleteTraversal;
        }

        var segments = source.InitialCursor.Source;
        if (!ReferenceEquals(source.FinalCursor.Source, segments) ||
            source.InitialCursor.NextSegmentIndex < 0 ||
            source.FinalCursor.NextSegmentIndex != segments.SegmentCount ||
            source.Steps.Count !=
                source.FinalCursor.NextSegmentIndex -
                source.InitialCursor.NextSegmentIndex)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalCompletionResult
                    .InvalidTraversal;
        }

        var expectedCursor = source.InitialCursor;
        for (var index = 0; index < source.Steps.Count; index++)
        {
            var step = source.Steps[index];
            var segmentIndex =
                source.InitialCursor.NextSegmentIndex + index;
            if (!ReferenceEquals(step.Source, expectedCursor) ||
                !ReferenceEquals(step.Source.Source, segments) ||
                !ReferenceEquals(step.Segment, segments.Segments[segmentIndex]) ||
                !ReferenceEquals(step.Next.Source, segments) ||
                step.Next.NextSegmentIndex != segmentIndex + 1)
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalCompletionResult
                        .InvalidTraversal;
            }

            expectedCursor = step.Next;
        }

        if (!ReferenceEquals(expectedCursor, source.FinalCursor))
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalCompletionResult
                    .InvalidTraversal;
        }

        completion = new(
            source,
            source.InitialCursor.NextSegmentIndex,
            source.FinalCursor.NextSegmentIndex,
            segments.SegmentCount);
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalCompletionResult
                .Projected;
    }
}
