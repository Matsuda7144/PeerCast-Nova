using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversalTests
{
    [Fact]
    public void Traverse_OwnsExactCursorStepChainToExhaustion()
    {
        var segments =
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorTests
                .Segments();
        var initial = Cursor(segments, 0);

        var traversal = Traverse(initial);

        Assert.Same(initial, traversal.InitialCursor);
        Assert.Equal(segments.SegmentCount, traversal.StepCount);
        Assert.True(traversal.IsExhaustive);
        for (var index = 0; index < traversal.StepCount; index++)
        {
            Assert.Same(segments.Segments[index],
                traversal.Steps[index].Segment);
            if (index > 0)
            {
                Assert.Same(
                    traversal.Steps[index - 1].Next,
                    traversal.Steps[index].Source);
            }
        }
        Assert.Same(traversal.Steps[^1].Next, traversal.FinalCursor);
    }

    [Fact]
    public void Traverse_AcceptsResumedAndTerminalCursor()
    {
        var segments =
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorTests
                .Segments();
        var resumed = Traverse(Cursor(segments, 1));
        Assert.Single(resumed.Steps);
        Assert.Same(segments.Segments[1], resumed.Steps[0].Segment);
        Assert.True(resumed.IsExhaustive);

        var terminal = Traverse(Cursor(segments, segments.SegmentCount));
        Assert.Empty(terminal.Steps);
        Assert.Same(terminal.InitialCursor, terminal.FinalCursor);
    }

    [Fact]
    public void Traverse_EnforcesStepLimitBeforeAllocation()
    {
        var segments =
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorTests
                .Segments();
        AssertResult(
            Result.StepLimitExceeded,
            Cursor(segments, 0),
            new() { MaximumStepCount = 1 });
    }

    [Fact]
    public void Traverse_RejectsMissingInvalidConfigurationAndCursor()
    {
        AssertResult(Result.InvalidInput, null, null);
        var segments =
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorTests
                .Segments();
        var initial = Cursor(segments, 0);
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
            new(
                segments,
                segments.SegmentCount + 1),
            null);
    }

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversal
            Traverse(
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursor
                    source)
    {
        Assert.Equal(
            Result.Traversed,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraverser
                .Traverse(source, out var traversal));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraversal>(
                traversal);
    }

    private static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursor
            Cursor(
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentProjection
                    source,
                int nextSegmentIndex) =>
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorTests
            .Project(source, nextSegmentIndex);

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursor?
            source,
        PlaybackIsoBmffFragmentSampleMergeTraversalOptions? options)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentTraverser
                .Traverse(source, out var traversal, options));
        Assert.Null(traversal);
    }
}
