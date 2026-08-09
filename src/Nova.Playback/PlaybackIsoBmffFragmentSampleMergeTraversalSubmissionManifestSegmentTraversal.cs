using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalResult
{
    Traversed,
    InvalidInput,
    InvalidConfiguration,
    StepLimitExceeded,
    CursorStepFailed,
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversal
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversal(
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursor
                initialCursor,
            ReadOnlyCollection<
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorStep>
                steps,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursor
                finalCursor)
    {
        InitialCursor = initialCursor;
        Steps = steps;
        FinalCursor = finalCursor;
    }

    public
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursor
            InitialCursor
    { get; }

    public IReadOnlyList<
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorStep>
        Steps
    { get; }

    public
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursor
            FinalCursor
    { get; }

    public int StepCount => Steps.Count;

    public bool IsExhaustive => !FinalCursor.HasCurrent;
}

public static class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraverser
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalResult
            Traverse(
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursor?
                    initialCursor,
                out
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversal?
                        traversal,
                PlaybackIsoBmffFragmentSampleMergeTraversalOptions? options =
                    null)
    {
        traversal = null;
        options ??= new PlaybackIsoBmffFragmentSampleMergeTraversalOptions();
        if (options.MaximumStepCount is < 0 or
            > PlaybackIsoBmffFragmentSampleMergeTraversalOptions
                .MaximumStepCountHardLimit)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalResult
                    .InvalidConfiguration;
        }

        if (initialCursor is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalResult
                    .InvalidInput;
        }

        if (initialCursor.NextSegmentIndex < 0 ||
            initialCursor.NextSegmentIndex >
                initialCursor.Source.SegmentCount)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalResult
                    .CursorStepFailed;
        }

        if (initialCursor.RemainingSegmentCount > options.MaximumStepCount)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalResult
                    .StepLimitExceeded;
        }

        var steps =
            new PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorStep[
                initialCursor.RemainingSegmentCount];
        var current = initialCursor;
        for (var index = 0; index < steps.Length; index++)
        {
            var expectedSegmentIndex = initialCursor.NextSegmentIndex + index;
            if (PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorStepper
                    .Advance(current, out var step) !=
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorStepResult
                    .Advanced ||
                step is null ||
                !ReferenceEquals(step.Source, current) ||
                !ReferenceEquals(step.Source.Source, initialCursor.Source) ||
                !ReferenceEquals(
                    step.Segment,
                    initialCursor.Source.Segments[expectedSegmentIndex]) ||
                !ReferenceEquals(step.Next.Source, initialCursor.Source) ||
                step.Next.NextSegmentIndex != expectedSegmentIndex + 1)
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalResult
                        .CursorStepFailed;
            }

            steps[index] = step;
            current = step.Next;
        }

        traversal = new(initialCursor, Array.AsReadOnly(steps), current);
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalResult
                .Traversed;
    }
}
