using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewTests
{
    [Fact]
    public void Project_IndexesExactSourceStepChainWithoutDeliveryClaim()
    {
        var completion = Completion(4, 0);

        var view = Project(completion);

        Assert.Same(completion, view.Completion);
        Assert.Equal(4, view.AcknowledgementCount);
        for (var index = 0; index < view.Acknowledgements.Count; index++)
        {
            var acknowledgement = view.Acknowledgements[index];
            Assert.Equal(index, acknowledgement.TraversalIndex);
            Assert.Same(completion.Source.Steps[index], acknowledgement.Step);
            Assert.Equal(index, acknowledgement.SampleIndex);
            Assert.Equal(
                completion.Merge.Samples[index],
                acknowledgement.Sample);
            if (index > 0)
            {
                Assert.Same(
                    view.Acknowledgements[index - 1].SuccessorCursor,
                    acknowledgement.SourceCursor);
            }
        }
    }

    [Fact]
    public void Project_PreservesResumedSourceIndexes()
    {
        var view = Project(Completion(5, 3));

        Assert.Equal(2, view.AcknowledgementCount);
        Assert.Equal(3, view.Acknowledgements[0].SampleIndex);
        Assert.Equal(4, view.Acknowledgements[1].SampleIndex);
        Assert.Equal(0, view.Acknowledgements[0].TraversalIndex);
        Assert.Equal(1, view.Acknowledgements[1].TraversalIndex);
    }

    [Fact]
    public void Project_AcceptsEmptyCompletedTraversal()
    {
        var view = Project(Completion(0, 0));

        Assert.Empty(view.Acknowledgements);
        Assert.Equal(0, view.AcknowledgementCount);
    }

    [Fact]
    public void Project_RejectsMissingAndMismatchedCompletion()
    {
        AssertResult(Result.InvalidInput, null);

        var completion = Completion(2, 0);
        var mismatched = new
            PlaybackIsoBmffFragmentSampleMergeTraversalCompletion(
                completion.Source,
                completion.InitialIndex,
                completion.FinalIndex,
                completion.SampleCount + 1);
        AssertResult(Result.CompletionMismatch, mismatched);
    }

    [Fact]
    public void Project_RejectsBrokenStepChain()
    {
        var completion = Completion(2, 0);
        var traversal = completion.Source;
        var wrongCursor = new PlaybackIsoBmffFragmentSampleMergeCursor(
            completion.Merge,
            0);
        var brokenStep = new PlaybackIsoBmffFragmentSampleMergeCursorStep(
            wrongCursor,
            completion.Merge.Samples[0],
            traversal.Steps[0].NextCursor);
        var brokenTraversal = new PlaybackIsoBmffFragmentSampleMergeTraversal(
            traversal.InitialCursor,
            Array.AsReadOnly(new[] { brokenStep, traversal.Steps[1] }),
            traversal.FinalCursor);
        var brokenCompletion = new
            PlaybackIsoBmffFragmentSampleMergeTraversalCompletion(
                brokenTraversal,
                0,
                2,
                2);

        AssertResult(Result.InvalidStepChain, brokenCompletion);
    }

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementView Project(
            PlaybackIsoBmffFragmentSampleMergeTraversalCompletion completion)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewProjector
                .Project(completion, out var view));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementView>(view);
    }

    internal static PlaybackIsoBmffFragmentSampleMergeTraversalCompletion
        Completion(int sampleCount, int nextIndex)
    {
        var source = PlaybackIsoBmffFragmentSampleMergeCursorTests.Merge(
            sampleCount);
        var cursor = PlaybackIsoBmffFragmentSampleMergeCursorTests.Project(
            source,
            nextIndex);
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleMergeTraversalResult.Traversed,
            PlaybackIsoBmffFragmentSampleMergeTraverser.Traverse(
                cursor,
                out var traversal));
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleMergeTraversalCompletionResult
                .Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalCompletionProjector
                .Project(traversal, out var completion));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalCompletion>(completion);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffFragmentSampleMergeTraversalCompletion? completion)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewProjector
                .Project(completion, out var view));
        Assert.Null(view);
    }
}
