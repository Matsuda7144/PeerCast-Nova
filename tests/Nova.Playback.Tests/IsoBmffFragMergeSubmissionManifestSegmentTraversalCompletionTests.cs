using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalCompletionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalCompletionTests
{
    [Fact]
    public void Project_CertifiesExactExhaustiveTraversal()
    {
        var traversal = Traverse(0);

        var completion = Project(traversal);

        Assert.Same(traversal, completion.Source);
        Assert.Same(traversal.InitialCursor.Source, completion.Segments);
        Assert.Equal(0, completion.InitialSegmentIndex);
        Assert.Equal(2, completion.FinalSegmentIndex);
        Assert.Equal(2, completion.SegmentCount);
        Assert.Equal(2, completion.TraversedSegmentCount);
    }

    [Fact]
    public void Project_PreservesResumedTraversalCount()
    {
        var completion = Project(Traverse(1));

        Assert.Equal(1, completion.InitialSegmentIndex);
        Assert.Equal(2, completion.FinalSegmentIndex);
        Assert.Equal(1, completion.TraversedSegmentCount);
    }

    [Fact]
    public void Project_RejectsMissingAndIncompleteTraversal()
    {
        AssertResult(Result.InvalidInput, null);
        var segments =
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorTests
                .Segments();
        var initial =
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorTests
                .Project(segments, 0);
        var step = Advance(initial);
        var incomplete = new
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversal(
                initial,
                Array.AsReadOnly(new[] { step }),
                step.Next);
        AssertResult(Result.IncompleteTraversal, incomplete);
    }

    [Fact]
    public void Project_RejectsBrokenStepAndFinalCursorIdentity()
    {
        var traversal = Traverse(0);
        var segments = traversal.InitialCursor.Source;
        var replacedStep = new
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorStep(
                new(segments, 0),
                traversal.Steps[0].Segment,
                traversal.Steps[0].Next);
        var broken = new
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversal(
                traversal.InitialCursor,
                Array.AsReadOnly(
                    new[] { replacedStep, traversal.Steps[1] }),
                traversal.FinalCursor);
        AssertResult(Result.InvalidTraversal, broken);

        var replacedFinal = new
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversal(
                traversal.InitialCursor,
                Array.AsReadOnly(traversal.Steps.ToArray()),
                new(segments, segments.SegmentCount));
        AssertResult(Result.InvalidTraversal, replacedFinal);
    }

    private static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversal
            Traverse(int nextSegmentIndex)
    {
        var segments =
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorTests
                .Segments();
        var cursor =
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorTests
                .Project(segments, nextSegmentIndex);
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalTests
                .Traverse(cursor);
    }

    private static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorStep
            Advance(
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursor
                    source)
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorStepResult
                .Advanced,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorStepper
                .Advance(source, out var step));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorStep>(
                step);
    }

    private static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalCompletion
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversal
                    source)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalCompletionProjector
                .Project(source, out var completion));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalCompletion>(
                completion);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversal?
            source)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalCompletionProjector
                .Project(source, out var completion));
        Assert.Null(completion);
    }
}
