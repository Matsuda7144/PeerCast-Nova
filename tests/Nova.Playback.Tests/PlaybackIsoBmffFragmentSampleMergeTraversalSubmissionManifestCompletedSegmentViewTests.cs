using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestCompletedSegmentViewResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestCompletedSegmentViewTests
{
    [Fact]
    public void Project_ReconstructsExactTraversedSegmentsWithoutAcknowledgement()
    {
        var completion = Completion(0);

        var view = Project(completion);

        Assert.Same(completion, view.Source);
        Assert.Equal(2, view.SegmentCount);
        for (var index = 0; index < view.SegmentCount; index++)
        {
            var segment = view.Segments[index];
            Assert.Equal(index, segment.CompletedIndex);
            Assert.Equal(index, segment.SourceSegmentIndex);
            Assert.Same(completion.Source.Steps[index], segment.Step);
            Assert.Same(completion.Segments.Segments[index], segment.Segment);
        }
    }

    [Fact]
    public void Project_PreservesResumedSourceIndexes()
    {
        var completion = Completion(1);

        var segment = Assert.Single(Project(completion).Segments);

        Assert.Equal(0, segment.CompletedIndex);
        Assert.Equal(1, segment.SourceSegmentIndex);
        Assert.Same(completion.Segments.Segments[1], segment.Segment);
    }

    [Fact]
    public void Project_RejectsMissingSource()
    {
        Assert.Equal(
            Result.InvalidInput,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestCompletedSegmentViewProjector
                .Project(null, out var view));
        Assert.Null(view);
    }

    private static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalCompletion
            Completion(int nextSegmentIndex)
    {
        var segments =
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorTests
                .Segments();
        var cursor =
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorTests
                .Project(segments, nextSegmentIndex);
        var traversal =
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalTests
                .Traverse(cursor);
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalCompletionResult
                .Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalCompletionProjector
                .Project(traversal, out var completion));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalCompletion>(
                completion);
    }

    private static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestCompletedSegmentView
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalCompletion
                    source)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestCompletedSegmentViewProjector
                .Project(source, out var view));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestCompletedSegmentView>(
                view);
    }
}
