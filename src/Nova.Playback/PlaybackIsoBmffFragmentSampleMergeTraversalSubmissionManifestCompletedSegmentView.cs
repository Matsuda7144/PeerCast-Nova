using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestCompletedSegmentViewResult
{
    Projected,
    InvalidInput,
    SegmentLimitExceeded,
    SourceMismatch,
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestCompletedSegment
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestCompletedSegment(
            int completedIndex,
            int sourceSegmentIndex,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorStep
                step)
    {
        CompletedIndex = completedIndex;
        SourceSegmentIndex = sourceSegmentIndex;
        Step = step;
    }

    public int CompletedIndex { get; }

    public int SourceSegmentIndex { get; }

    public
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorStep
            Step
    { get; }

    public PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegment
        Segment => Step.Segment;
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestCompletedSegmentView
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestCompletedSegmentView(
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalCompletion
                source,
            ReadOnlyCollection<
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestCompletedSegment>
                segments)
    {
        Source = source;
        Segments = segments;
    }

    public
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalCompletion
            Source
    { get; }

    public IReadOnlyList<
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestCompletedSegment>
        Segments
    { get; }

    public int SegmentCount => Segments.Count;
}

public static class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestCompletedSegmentViewProjector
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestCompletedSegmentViewResult
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalCompletion?
                    source,
                out
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestCompletedSegmentView?
                        view)
    {
        view = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestCompletedSegmentViewResult
                    .InvalidInput;
        }

        if (source.TraversedSegmentCount >
            PlaybackIsoBmffFragmentSampleMergeTraversalOptions
                .MaximumStepCountHardLimit)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestCompletedSegmentViewResult
                    .SegmentLimitExceeded;
        }

        var traversal = source.Source;
        if (traversal.Steps.Count != source.TraversedSegmentCount ||
            source.InitialSegmentIndex !=
                traversal.InitialCursor.NextSegmentIndex ||
            source.FinalSegmentIndex != traversal.FinalCursor.NextSegmentIndex ||
            source.SegmentCount != traversal.InitialCursor.Source.SegmentCount)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestCompletedSegmentViewResult
                    .SourceMismatch;
        }

        var segments =
            new PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestCompletedSegment[
                traversal.StepCount];
        for (var index = 0; index < segments.Length; index++)
        {
            var sourceSegmentIndex = source.InitialSegmentIndex + index;
            var step = traversal.Steps[index];
            if (!ReferenceEquals(
                    step.Segment,
                    source.Segments.Segments[sourceSegmentIndex]) ||
                step.Source.NextSegmentIndex != sourceSegmentIndex ||
                step.Next.NextSegmentIndex != sourceSegmentIndex + 1)
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestCompletedSegmentViewResult
                        .SourceMismatch;
            }

            segments[index] = new(index, sourceSegmentIndex, step);
        }

        view = new(source, Array.AsReadOnly(segments));
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestCompletedSegmentViewResult
                .Projected;
    }
}
