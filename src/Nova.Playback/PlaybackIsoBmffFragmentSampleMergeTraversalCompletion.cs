namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffFragmentSampleMergeTraversalCompletionResult
{
    Projected,
    InvalidInput,
    IncompleteTraversal,
    InvalidTraversal,
}

public sealed class PlaybackIsoBmffFragmentSampleMergeTraversalCompletion
{
    internal PlaybackIsoBmffFragmentSampleMergeTraversalCompletion(
        PlaybackIsoBmffFragmentSampleMergeTraversal source,
        int initialIndex,
        int finalIndex,
        int sampleCount)
    {
        Source = source;
        InitialIndex = initialIndex;
        FinalIndex = finalIndex;
        SampleCount = sampleCount;
    }

    public PlaybackIsoBmffFragmentSampleMergeTraversal Source { get; }

    public PlaybackIsoBmffFragmentSampleBatchPresentationMerge Merge =>
        Source.InitialCursor.Source;

    public int InitialIndex { get; }

    public int FinalIndex { get; }

    public int SampleCount { get; }

    public int TraversedSampleCount => FinalIndex - InitialIndex;
}

public static class
    PlaybackIsoBmffFragmentSampleMergeTraversalCompletionProjector
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalCompletionResult Project(
            PlaybackIsoBmffFragmentSampleMergeTraversal? source,
            out PlaybackIsoBmffFragmentSampleMergeTraversalCompletion?
                completion)
    {
        completion = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalCompletionResult
                    .InvalidInput;
        }

        if (!source.IsComplete)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalCompletionResult
                    .IncompleteTraversal;
        }

        var merge = source.InitialCursor.Source;
        if (!ReferenceEquals(source.FinalCursor.Source, merge) ||
            source.InitialCursor.NextIndex < 0 ||
            source.FinalCursor.NextIndex != merge.Samples.Count ||
            source.Steps.Count !=
                source.FinalCursor.NextIndex - source.InitialCursor.NextIndex)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalCompletionResult
                    .InvalidTraversal;
        }

        var expectedCursor = source.InitialCursor;
        for (var index = 0; index < source.Steps.Count; index++)
        {
            var step = source.Steps[index];
            var sampleIndex = source.InitialCursor.NextIndex + index;
            if (!ReferenceEquals(step.Cursor, expectedCursor) ||
                !ReferenceEquals(step.Cursor.Source, merge) ||
                step.Index != sampleIndex ||
                step.Sample != merge.Samples[sampleIndex] ||
                !ReferenceEquals(step.NextCursor.Source, merge) ||
                step.NextCursor.NextIndex != sampleIndex + 1)
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalCompletionResult
                        .InvalidTraversal;
            }

            expectedCursor = step.NextCursor;
        }

        if (!ReferenceEquals(expectedCursor, source.FinalCursor))
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalCompletionResult
                    .InvalidTraversal;
        }

        completion = new(
            source,
            source.InitialCursor.NextIndex,
            source.FinalCursor.NextIndex,
            merge.Samples.Count);
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalCompletionResult
                .Projected;
    }
}
