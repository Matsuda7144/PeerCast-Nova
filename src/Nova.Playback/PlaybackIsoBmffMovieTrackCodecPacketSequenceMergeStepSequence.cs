using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepSequenceCreationResult
{
    Created,
    MissingSteps,
    EmptyStepSequence,
    StepLimitExceeded,
    MissingStep,
    InvalidStep,
    NonContiguousMergeIndex,
    TieBreakMismatch,
    CursorStateMismatch,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepSequence
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepSequence(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak
                tieBreak,
            ReadOnlyCollection<
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStep> steps)
    {
        TieBreak = tieBreak;
        Steps = steps;
    }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak
        TieBreak
    { get; }

    public IReadOnlyList<
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStep> Steps
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet
        InitialCursorSet => Steps[0].Advance.SourceCursorSet;

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet FinalCursorSet
        => Steps[^1].SuccessorCursorSet;

    public int StepCount => Steps.Count;
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepSequenceFactory
{
    public const int MaximumStepCount =
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepProjector
            .MaximumMergeStepCount;

    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepSequenceCreationResult
            TryCreate(
                IReadOnlyList<
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStep>?
                    steps,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepSequence?
                    sequence)
    {
        sequence = null;
        if (steps is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepSequenceCreationResult
                    .MissingSteps;
        }

        if (steps.Count == 0)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepSequenceCreationResult
                    .EmptyStepSequence;
        }

        if (steps.Count > MaximumStepCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepSequenceCreationResult
                    .StepLimitExceeded;
        }

        var copied = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStep[steps.Count];
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStep? prior = null;
        PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak
            tieBreak = default;
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            if (step is null)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepSequenceCreationResult
                        .MissingStep;
            }

            if (step.MergeIndex != index)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepSequenceCreationResult
                        .NonContiguousMergeIndex;
            }

            if (PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepProjector
                    .TryProject(step.Advance, index, out var exact) !=
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepProjectionResult
                    .Projected ||
                exact is null ||
                !ReferenceEquals(exact.Stream, step.Stream) ||
                exact.LeadingAppearance != step.LeadingAppearance)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepSequenceCreationResult
                        .InvalidStep;
            }

            var stepTieBreak = step.Advance.RoleSelection.TieBreak;
            if (index == 0)
            {
                tieBreak = stepTieBreak;
            }
            else
            {
                if (stepTieBreak != tieBreak)
                {
                    return
                        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepSequenceCreationResult
                            .TieBreakMismatch;
                }

                if (!ReferenceEquals(
                        prior!.SuccessorCursorSet,
                        step.Advance.SourceCursorSet))
                {
                    return
                        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepSequenceCreationResult
                            .CursorStateMismatch;
                }
            }

            copied[index] = step;
            prior = step;
        }

        sequence = new(tieBreak, Array.AsReadOnly(copied));
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepSequenceCreationResult
                .Created;
    }
}
