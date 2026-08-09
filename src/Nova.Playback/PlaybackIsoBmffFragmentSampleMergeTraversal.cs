using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffFragmentSampleMergeTraversalResult
{
    Traversed,
    InvalidInput,
    InvalidConfiguration,
    StepLimitExceeded,
    CursorStepFailed,
}

public sealed record PlaybackIsoBmffFragmentSampleMergeTraversalOptions
{
    public const int DefaultMaximumStepCount = 4_096;
    public const int MaximumStepCountHardLimit = 65_536;

    public int MaximumStepCount { get; init; } = DefaultMaximumStepCount;
}

public sealed class PlaybackIsoBmffFragmentSampleMergeTraversal
{
    internal PlaybackIsoBmffFragmentSampleMergeTraversal(
        PlaybackIsoBmffFragmentSampleMergeCursor initialCursor,
        ReadOnlyCollection<PlaybackIsoBmffFragmentSampleMergeCursorStep> steps,
        PlaybackIsoBmffFragmentSampleMergeCursor finalCursor)
    {
        InitialCursor = initialCursor;
        Steps = steps;
        FinalCursor = finalCursor;
    }

    public PlaybackIsoBmffFragmentSampleMergeCursor InitialCursor { get; }

    public IReadOnlyList<PlaybackIsoBmffFragmentSampleMergeCursorStep> Steps
    { get; }

    public PlaybackIsoBmffFragmentSampleMergeCursor FinalCursor { get; }

    public int StepCount => Steps.Count;

    public bool IsComplete => FinalCursor.IsComplete;
}

public static class PlaybackIsoBmffFragmentSampleMergeTraverser
{
    public static PlaybackIsoBmffFragmentSampleMergeTraversalResult Traverse(
        PlaybackIsoBmffFragmentSampleMergeCursor? initialCursor,
        out PlaybackIsoBmffFragmentSampleMergeTraversal? traversal,
        PlaybackIsoBmffFragmentSampleMergeTraversalOptions? options = null)
    {
        traversal = null;
        options ??= new PlaybackIsoBmffFragmentSampleMergeTraversalOptions();
        if (options.MaximumStepCount is < 0 or
            > PlaybackIsoBmffFragmentSampleMergeTraversalOptions
                .MaximumStepCountHardLimit)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalResult
                    .InvalidConfiguration;
        }

        if (initialCursor is null)
        {
            return PlaybackIsoBmffFragmentSampleMergeTraversalResult.InvalidInput;
        }

        if (initialCursor.NextIndex < 0 ||
            initialCursor.NextIndex > initialCursor.Source.Samples.Count)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalResult.CursorStepFailed;
        }

        if (initialCursor.RemainingCount > options.MaximumStepCount)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalResult
                    .StepLimitExceeded;
        }

        var steps = new PlaybackIsoBmffFragmentSampleMergeCursorStep[
            initialCursor.RemainingCount];
        var current = initialCursor;
        for (var index = 0; index < steps.Length; index++)
        {
            if (PlaybackIsoBmffFragmentSampleMergeCursorStepper.Step(
                    current,
                    out var step) !=
                PlaybackIsoBmffFragmentSampleMergeCursorStepResult.Stepped ||
                step is null ||
                !ReferenceEquals(step.Cursor, current) ||
                !ReferenceEquals(step.Cursor.Source, initialCursor.Source) ||
                step.Index != initialCursor.NextIndex + index ||
                step.Sample != initialCursor.Source.Samples[step.Index] ||
                !ReferenceEquals(
                    step.NextCursor.Source,
                    initialCursor.Source) ||
                step.NextCursor.NextIndex != step.Index + 1)
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalResult
                        .CursorStepFailed;
            }

            steps[index] = step;
            current = step.NextCursor;
        }

        traversal = new(initialCursor, Array.AsReadOnly(steps), current);
        return PlaybackIsoBmffFragmentSampleMergeTraversalResult.Traversed;
    }
}
