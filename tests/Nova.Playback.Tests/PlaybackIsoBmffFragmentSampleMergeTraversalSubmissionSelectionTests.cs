using Disposition = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionDisposition;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelectionTests
{
    [Fact]
    public void Select_RetainsCallerBoundedIncludedSourceOrder()
    {
        var source = Classification(
            [Disposition.Include, Disposition.Include]);

        var selection = Select(source, 1);

        Assert.Same(source, selection.Source);
        Assert.Equal(1, selection.MaximumIntentCount);
        Assert.Equal(2, selection.IncludedCount);
        Assert.Single(selection.Candidates);
        Assert.True(selection.IsTruncated);
        Assert.Equal(0, selection.Candidates[0].SourceIndex);
        Assert.Same(
            source.Classifications[0],
            selection.Candidates[0].Classification);
    }

    [Fact]
    public void Select_SkipsExcludedIntentsWithoutReordering()
    {
        var source = Classification(
            [Disposition.Exclude, Disposition.Include]);

        var selection = Select(source, 2);

        Assert.Equal(1, selection.IncludedCount);
        Assert.False(selection.IsTruncated);
        Assert.Equal(1, Assert.Single(selection.Candidates).SourceIndex);
    }

    [Fact]
    public void Select_RejectsMissingAndInvalidBounds()
    {
        AssertResult(Result.InvalidInput, null, 1);
        var source = Classification(
            [Disposition.Include, Disposition.Exclude]);
        AssertResult(Result.InvalidMaximumIntentCount, source, 0);
        AssertResult(
            Result.InvalidMaximumIntentCount,
            source,
            PlaybackIsoBmffFragmentSampleMergeTraversalOptions
                .MaximumStepCountHardLimit + 1);
    }

    private static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassificationProjection
            Classification(IReadOnlyList<Disposition> dispositions)
    {
        var intent =
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjectionTests
                .Projection();
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassificationResult
                .Classified,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassifier
                .Classify(intent, dispositions, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassificationProjection>(
                projection);
    }

    private static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelection Select(
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassificationProjection
                source,
            int maximumIntentCount)
    {
        Assert.Equal(
            Result.Selected,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelector.Select(
                source,
                maximumIntentCount,
                out var selection));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelection>(
                selection);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassificationProjection?
            source,
        int maximumIntentCount)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelector.Select(
                source,
                maximumIntentCount,
                out var selection));
        Assert.Null(selection);
    }
}
