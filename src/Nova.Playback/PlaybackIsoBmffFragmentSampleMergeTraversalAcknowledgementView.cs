using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewResult
{
    Projected,
    InvalidInput,
    CompletionMismatch,
    InvalidStepChain,
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgement
{
    internal PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgement(
        int traversalIndex,
        PlaybackIsoBmffFragmentSampleMergeCursorStep step)
    {
        TraversalIndex = traversalIndex;
        Step = step;
    }

    public int TraversalIndex { get; }

    public PlaybackIsoBmffFragmentSampleMergeCursorStep Step { get; }

    public int SampleIndex => Step.Index;

    public PlaybackIsoBmffFragmentBatchPresentationSample Sample => Step.Sample;

    public PlaybackIsoBmffFragmentSampleMergeCursor SourceCursor => Step.Cursor;

    public PlaybackIsoBmffFragmentSampleMergeCursor SuccessorCursor =>
        Step.NextCursor;
}

public sealed class PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementView
{
    internal PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementView(
        PlaybackIsoBmffFragmentSampleMergeTraversalCompletion completion,
        ReadOnlyCollection<
            PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgement>
            acknowledgements)
    {
        Completion = completion;
        Acknowledgements = acknowledgements;
    }

    public PlaybackIsoBmffFragmentSampleMergeTraversalCompletion Completion
    { get; }

    public IReadOnlyList<
        PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgement>
        Acknowledgements
    { get; }

    public int AcknowledgementCount => Acknowledgements.Count;
}

public static class
    PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewProjector
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewResult
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalCompletion?
                    completion,
                out
                    PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementView?
                        view)
    {
        view = null;
        if (completion is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewResult
                    .InvalidInput;
        }

        var traversal = completion.Source;
        var merge = completion.Merge;
        if (!ReferenceEquals(traversal.InitialCursor.Source, merge) ||
            !ReferenceEquals(traversal.FinalCursor.Source, merge) ||
            completion.InitialIndex != traversal.InitialCursor.NextIndex ||
            completion.FinalIndex != traversal.FinalCursor.NextIndex ||
            completion.SampleCount != merge.Samples.Count ||
            completion.TraversedSampleCount != traversal.Steps.Count ||
            !traversal.IsComplete)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewResult
                    .CompletionMismatch;
        }

        var acknowledgements = new
            PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgement[
                traversal.Steps.Count];
        var expectedCursor = traversal.InitialCursor;
        for (var index = 0; index < acknowledgements.Length; index++)
        {
            var step = traversal.Steps[index];
            var sampleIndex = completion.InitialIndex + index;
            if (!ReferenceEquals(step.Cursor, expectedCursor) ||
                step.Index != sampleIndex ||
                step.Sample != merge.Samples[sampleIndex] ||
                !ReferenceEquals(step.NextCursor.Source, merge) ||
                step.NextCursor.NextIndex != sampleIndex + 1)
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewResult
                        .InvalidStepChain;
            }

            acknowledgements[index] = new(index, step);
            expectedCursor = step.NextCursor;
        }

        if (!ReferenceEquals(expectedCursor, traversal.FinalCursor))
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewResult
                    .InvalidStepChain;
        }

        view = new(completion, Array.AsReadOnly(acknowledgements));
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewResult
                .Projected;
    }
}
