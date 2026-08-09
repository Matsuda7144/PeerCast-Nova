using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewProjectionResult
{
    Projected,
    MissingCompletion,
    UnsupportedTraversalSource,
    StepCountMismatch,
    InvalidStepChain,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStep
{
    internal PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStep(
        int globalMergeIndex,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStep step)
    {
        GlobalMergeIndex = globalMergeIndex;
        Step = step;
    }

    public int GlobalMergeIndex { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStep Step { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole Role =>
        Step.Role;

    public PlaybackIsoBmffMovieTrackCodecPacket Packet => Step.Packet;
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepView
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepView(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletion
                completion,
            ReadOnlyCollection<
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStep>
                steps)
    {
        Completion = completion;
        Steps = steps;
    }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletion
        Completion
    { get; }

    public IReadOnlyList<
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStep> Steps
    { get; }

    public int StepCount => Steps.Count;
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewProjectionResult
            TryProject(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletion?
                    completion,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepView?
                    view)
    {
        view = null;
        if (completion is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewProjectionResult
                    .MissingCompletion;
        }

        var segments = new Stack<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversal>();
        IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalSource
            source = completion.Source;
        while (source is
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuation
                continuation)
        {
            segments.Push(continuation.Segment);
            source = continuation.Source;
        }

        if (source is not
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversal initial)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewProjectionResult
                    .UnsupportedTraversalSource;
        }

        var stepCount = completion.ActualStepCount;
        var projected = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStep[
                stepCount];
        var globalIndex = 0;
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStep? prior = null;
        if (!TryAppend(initial, completion, projected, ref globalIndex,
                ref prior))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewProjectionResult
                    .InvalidStepChain;
        }

        while (segments.Count > 0)
        {
            if (!TryAppend(
                    segments.Pop(),
                    completion,
                    projected,
                    ref globalIndex,
                    ref prior))
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewProjectionResult
                        .InvalidStepChain;
            }
        }

        if (globalIndex != stepCount ||
            stepCount > 0 &&
            !ReferenceEquals(
                prior!.SuccessorCursorSet,
                completion.FinalCursorSet) ||
            stepCount == 0 &&
            !ReferenceEquals(
                completion.InitialCursorSet,
                completion.FinalCursorSet))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewProjectionResult
                    .StepCountMismatch;
        }

        view = new(completion, Array.AsReadOnly(projected));
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewProjectionResult
                .Projected;
    }

    private static bool TryAppend(
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversal segment,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletion completion,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStep[] output,
        ref int globalIndex,
        ref PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStep? prior)
    {
        if (segment.TieBreak != completion.Source.TieBreak ||
            prior is null &&
            !ReferenceEquals(
                segment.InitialCursorSet,
                completion.InitialCursorSet) ||
            prior is not null &&
            !ReferenceEquals(
                prior.SuccessorCursorSet,
                segment.InitialCursorSet))
        {
            return false;
        }

        var localMergeIndex = 0;
        foreach (var step in segment.Steps)
        {
            if (globalIndex >= output.Length ||
                step.MergeIndex != localMergeIndex ||
                prior is not null &&
                !ReferenceEquals(
                    prior.SuccessorCursorSet,
                    step.Advance.SourceCursorSet))
            {
                return false;
            }

            output[globalIndex] = new(globalIndex, step);
            globalIndex++;
            localMergeIndex++;
            prior = step;
        }

        return localMergeIndex == segment.StepCount &&
            (segment.StepCount > 0 || output.Length == 0);
    }
}
