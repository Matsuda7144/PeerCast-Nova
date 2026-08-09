using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffFragmentSampleMergeTraversalTests
{
    [Fact]
    public void Traverse_OwnsExactCursorStepChainToCompletion()
    {
        var source = PlaybackIsoBmffFragmentSampleMergeCursorTests.Merge(4);
        var initial = PlaybackIsoBmffFragmentSampleMergeCursorTests.Project(
            source,
            0);

        var traversal = Traverse(initial);

        Assert.Same(initial, traversal.InitialCursor);
        Assert.Equal(4, traversal.StepCount);
        Assert.True(traversal.IsComplete);
        Assert.True(traversal.FinalCursor.IsComplete);
        for (var index = 0; index < traversal.Steps.Count; index++)
        {
            var step = traversal.Steps[index];
            Assert.Equal(index, step.Index);
            Assert.Equal(source.Samples[index], step.Sample);
            if (index > 0)
            {
                Assert.Same(
                    traversal.Steps[index - 1].NextCursor,
                    step.Cursor);
            }
        }

        Assert.Same(traversal.Steps[^1].NextCursor, traversal.FinalCursor);
    }

    [Fact]
    public void Traverse_AcceptsResumedCursor()
    {
        var source = PlaybackIsoBmffFragmentSampleMergeCursorTests.Merge(5);
        var initial = PlaybackIsoBmffFragmentSampleMergeCursorTests.Project(
            source,
            3);

        var traversal = Traverse(initial);

        Assert.Equal(2, traversal.StepCount);
        Assert.Equal(3, traversal.Steps[0].Index);
        Assert.Equal(4, traversal.Steps[1].Index);
        Assert.True(traversal.IsComplete);
    }

    [Fact]
    public void Traverse_AcceptsAlreadyCompleteAndEmptyCursor()
    {
        foreach (var source in new[]
                 {
                     PlaybackIsoBmffFragmentSampleMergeCursorTests.Merge(2),
                     PlaybackIsoBmffFragmentSampleMergeCursorTests.Merge(0),
                 })
        {
            var initial = PlaybackIsoBmffFragmentSampleMergeCursorTests.Project(
                source,
                source.Samples.Count);

            var traversal = Traverse(initial);

            Assert.Empty(traversal.Steps);
            Assert.Same(initial, traversal.FinalCursor);
            Assert.True(traversal.IsComplete);
        }
    }

    [Fact]
    public void Traverse_EnforcesStepLimitBeforeAllocation()
    {
        var initial = PlaybackIsoBmffFragmentSampleMergeCursorTests.Project(
            PlaybackIsoBmffFragmentSampleMergeCursorTests.Merge(3),
            0);

        AssertResult(
            Result.StepLimitExceeded,
            initial,
            new() { MaximumStepCount = 2 });
    }

    [Fact]
    public void Traverse_RejectsMissingInvalidConfigurationAndCursor()
    {
        AssertResult(Result.InvalidInput, null, null);
        var source = PlaybackIsoBmffFragmentSampleMergeCursorTests.Merge(1);
        var initial = PlaybackIsoBmffFragmentSampleMergeCursorTests.Project(
            source,
            0);
        AssertResult(
            Result.InvalidConfiguration,
            initial,
            new() { MaximumStepCount = -1 });
        AssertResult(
            Result.InvalidConfiguration,
            initial,
            new()
            {
                MaximumStepCount =
                    PlaybackIsoBmffFragmentSampleMergeTraversalOptions
                        .MaximumStepCountHardLimit + 1,
            });
        AssertResult(
            Result.CursorStepFailed,
            new PlaybackIsoBmffFragmentSampleMergeCursor(source, 2),
            null);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffFragmentSampleMergeCursor? cursor,
        PlaybackIsoBmffFragmentSampleMergeTraversalOptions? options)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffFragmentSampleMergeTraverser.Traverse(
                cursor,
                out var traversal,
                options));
        Assert.Null(traversal);
    }

    private static PlaybackIsoBmffFragmentSampleMergeTraversal Traverse(
        PlaybackIsoBmffFragmentSampleMergeCursor cursor)
    {
        Assert.Equal(
            Result.Traversed,
            PlaybackIsoBmffFragmentSampleMergeTraverser.Traverse(
                cursor,
                out var traversal));
        return Assert.IsType<PlaybackIsoBmffFragmentSampleMergeTraversal>(
            traversal);
    }
}
