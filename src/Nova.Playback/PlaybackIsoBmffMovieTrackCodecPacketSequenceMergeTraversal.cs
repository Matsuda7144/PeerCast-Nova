namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalResult
{
    Traversed,
    MissingCursorSet,
    InvalidTieBreak,
    InvalidStepLimit,
    InvalidCursorState,
    StepAdvanceFailed,
    StepProjectionFailed,
    StepSequenceValidationFailed,
}

public sealed class PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversal :
    IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalSource
{
    internal PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversal(
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet initialCursorSet,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak
            tieBreak,
        int maximumStepCount,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepSequence?
            stepSequence,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet finalCursorSet,
        bool isExhausted)
    {
        InitialCursorSet = initialCursorSet;
        TieBreak = tieBreak;
        MaximumStepCount = maximumStepCount;
        StepSequence = stepSequence;
        FinalCursorSet = finalCursorSet;
        IsExhausted = isExhausted;
    }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet
        InitialCursorSet
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak
        TieBreak
    { get; }

    public int MaximumStepCount { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepSequence?
        StepSequence
    { get; }

    public IReadOnlyList<
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStep> Steps =>
        StepSequence?.Steps ??
        Array.Empty<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStep>();

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet FinalCursorSet
    { get; }

    public int StepCount => Steps.Count;

    public int TotalStepCount => StepCount;

    public bool IsExhausted { get; }

    public bool IsTruncated => !IsExhausted;
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraverser
{
    public const int MaximumStepCount =
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepSequenceFactory
            .MaximumStepCount;

    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalResult
            TryTraverse(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet?
                    cursorSet,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak
                    tieBreak,
                int maximumStepCount,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversal?
                    traversal)
    {
        traversal = null;
        if (cursorSet is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalResult
                    .MissingCursorSet;
        }

        if (!Enum.IsDefined(tieBreak))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalResult
                    .InvalidTieBreak;
        }

        if (maximumStepCount <= 0 || maximumStepCount > MaximumStepCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalResult
                    .InvalidStepLimit;
        }

        var steps = new List<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStep>(
                Math.Min(maximumStepCount, 4_096));
        var current = cursorSet;
        while (steps.Count < maximumStepCount)
        {
            if (!TrySelect(current, tieBreak, out var roleSelection))
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalResult
                        .InvalidCursorState;
            }

            if (roleSelection!.IsExhausted)
            {
                return Complete(
                    cursorSet,
                    tieBreak,
                    maximumStepCount,
                    steps,
                    current,
                    true,
                    out traversal);
            }

            if (PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvancer
                    .TryAdvance(roleSelection, out var advance) !=
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvanceResult
                    .Advanced ||
                advance is null)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalResult
                        .StepAdvanceFailed;
            }

            if (PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepProjector
                    .TryProject(advance, steps.Count, out var step) !=
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepProjectionResult
                    .Projected ||
                step is null)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalResult
                        .StepProjectionFailed;
            }

            steps.Add(step);
            current = advance.SuccessorCursorSet;
        }

        if (!TrySelect(current, tieBreak, out var finalSelection))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalResult
                    .InvalidCursorState;
        }

        return Complete(
            cursorSet,
            tieBreak,
            maximumStepCount,
            steps,
            current,
            finalSelection!.IsExhausted,
            out traversal);
    }

    private static bool TrySelect(
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet cursorSet,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak
            tieBreak,
        out PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelection?
            roleSelection)
    {
        roleSelection = null;
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshotProjector
                .TryProject(cursorSet, out var snapshot) ==
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshotProjectionResult
                .Projected &&
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparer
                .Compare(snapshot, out var comparison) ==
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparisonResult
                .Compared &&
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelector
                .Select(comparison, tieBreak, out roleSelection) ==
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelectionResult
                .Selected;
    }

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalResult
            Complete(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet
                    initialCursorSet,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak
                    tieBreak,
                int maximumStepCount,
                List<
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStep>
                    steps,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet
                    finalCursorSet,
                bool isExhausted,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversal?
                    traversal)
    {
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepSequence?
            stepSequence = null;
        if (steps.Count > 0 &&
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepSequenceFactory
                .TryCreate(steps, out stepSequence) !=
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepSequenceCreationResult
                .Created)
        {
            traversal = null;
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalResult
                    .StepSequenceValidationFailed;
        }

        traversal = new(
            initialCursorSet,
            tieBreak,
            maximumStepCount,
            stepSequence,
            finalCursorSet,
            isExhausted);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalResult
                .Traversed;
    }
}
