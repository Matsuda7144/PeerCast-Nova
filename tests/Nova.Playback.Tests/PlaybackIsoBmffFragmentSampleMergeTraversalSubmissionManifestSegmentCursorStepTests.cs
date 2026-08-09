using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorStepResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorStepTests
{
    [Fact]
    public void Advance_RetainsExactSegmentAndReturnsNextCursor()
    {
        var segments =
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorTests
                .Segments();
        var cursor =
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorTests
                .Project(segments, 0);

        var step = Advance(cursor);

        Assert.Same(cursor, step.Source);
        Assert.Same(segments.Segments[0], step.Segment);
        Assert.Same(segments, step.Next.Source);
        Assert.Equal(1, step.Next.NextSegmentIndex);
        Assert.Equal(0, cursor.NextSegmentIndex);
    }

    [Fact]
    public void Advance_RejectsMissingAndEndCursor()
    {
        AssertResult(Result.InvalidInput, null);
        var segments =
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorTests
                .Segments();
        var end =
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorTests
                .Project(segments, segments.SegmentCount);
        AssertResult(Result.EndReached, end);
    }

    private static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorStep
            Advance(
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursor
                    source)
    {
        Assert.Equal(
            Result.Advanced,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorStepper
                .Advance(source, out var step));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorStep>(
                step);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursor?
            source)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentCursorStepper
                .Advance(source, out var step));
        Assert.Null(step);
    }
}
