using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalCompletionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffFragmentSampleMergeTraversalCompletionTests
{
    [Fact]
    public void Project_BindsExactCompletedTraversal()
    {
        var traversal = Traverse(4, 0);

        var completion = Project(traversal);

        Assert.Same(traversal, completion.Source);
        Assert.Same(traversal.InitialCursor.Source, completion.Merge);
        Assert.Equal(0, completion.InitialIndex);
        Assert.Equal(4, completion.FinalIndex);
        Assert.Equal(4, completion.SampleCount);
        Assert.Equal(4, completion.TraversedSampleCount);
    }

    [Fact]
    public void Project_PreservesResumedTraversalCount()
    {
        var completion = Project(Traverse(5, 3));

        Assert.Equal(3, completion.InitialIndex);
        Assert.Equal(5, completion.FinalIndex);
        Assert.Equal(5, completion.SampleCount);
        Assert.Equal(2, completion.TraversedSampleCount);
    }

    [Fact]
    public void Project_AcceptsEmptyCompletedTraversal()
    {
        var completion = Project(Traverse(0, 0));

        Assert.Equal(0, completion.TraversedSampleCount);
        Assert.Equal(0, completion.SampleCount);
    }

    [Fact]
    public void Project_RejectsMissingAndIncompleteTraversal()
    {
        AssertResult(Result.InvalidInput, null);

        var source = PlaybackIsoBmffFragmentSampleMergeCursorTests.Merge(2);
        var initial = PlaybackIsoBmffFragmentSampleMergeCursorTests.Project(
            source,
            0);
        var step = PlaybackIsoBmffFragmentSampleMergeCursorStepTests.Step(initial);
        var incomplete = new PlaybackIsoBmffFragmentSampleMergeTraversal(
            initial,
            Array.AsReadOnly(new[] { step }),
            step.NextCursor);
        AssertResult(Result.IncompleteTraversal, incomplete);
    }

    [Fact]
    public void Project_RejectsBrokenStepChainAndFinalCursorIdentity()
    {
        var traversal = Traverse(2, 0);
        var source = traversal.InitialCursor.Source;
        var wrongCursor = new PlaybackIsoBmffFragmentSampleMergeCursor(source, 0);
        var brokenStep = new PlaybackIsoBmffFragmentSampleMergeCursorStep(
            wrongCursor,
            source.Samples[0],
            traversal.Steps[0].NextCursor);
        var broken = new PlaybackIsoBmffFragmentSampleMergeTraversal(
            traversal.InitialCursor,
            Array.AsReadOnly(new[] { brokenStep, traversal.Steps[1] }),
            traversal.FinalCursor);
        AssertResult(Result.InvalidTraversal, broken);

        var replacedFinal = new PlaybackIsoBmffFragmentSampleMergeTraversal(
            traversal.InitialCursor,
            Array.AsReadOnly(traversal.Steps.ToArray()),
            new PlaybackIsoBmffFragmentSampleMergeCursor(source, 2));
        AssertResult(Result.InvalidTraversal, replacedFinal);
    }

    private static PlaybackIsoBmffFragmentSampleMergeTraversal Traverse(
        int sampleCount,
        int nextIndex)
    {
        var cursor = PlaybackIsoBmffFragmentSampleMergeCursorTests.Project(
            PlaybackIsoBmffFragmentSampleMergeCursorTests.Merge(sampleCount),
            nextIndex);
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleMergeTraversalResult.Traversed,
            PlaybackIsoBmffFragmentSampleMergeTraverser.Traverse(
                cursor,
                out var traversal));
        return Assert.IsType<PlaybackIsoBmffFragmentSampleMergeTraversal>(
            traversal);
    }

    private static PlaybackIsoBmffFragmentSampleMergeTraversalCompletion Project(
        PlaybackIsoBmffFragmentSampleMergeTraversal source)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalCompletionProjector
                .Project(source, out var completion));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalCompletion>(completion);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffFragmentSampleMergeTraversal? source)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffFragmentSampleMergeTraversalCompletionProjector
                .Project(source, out var completion));
        Assert.Null(completion);
    }
}
