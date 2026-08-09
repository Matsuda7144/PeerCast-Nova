using Disposition = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionDisposition;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassificationResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassificationTests
{
    [Fact]
    public void Classify_RetainsExactIntentsAndCallerDispositions()
    {
        var source = Intent();
        Disposition[] dispositions =
            [Disposition.Include, Disposition.Exclude];

        var projection = Classify(source, dispositions);

        Assert.Same(source, projection.Source);
        Assert.Equal(1, projection.IncludedCount);
        Assert.Equal(1, projection.ExcludedCount);
        for (var index = 0; index < dispositions.Length; index++)
        {
            Assert.Same(
                source.Intents[index],
                projection.Classifications[index].Intent);
            Assert.Equal(
                dispositions[index],
                projection.Classifications[index].Disposition);
        }
    }

    [Fact]
    public void Classify_AcceptsEmptyIntentSet()
    {
        var source = Intent(-91, 1);

        var projection = Classify(source, []);

        Assert.Empty(projection.Classifications);
        Assert.Equal(0, projection.IncludedCount);
        Assert.Equal(0, projection.ExcludedCount);
    }

    [Fact]
    public void Classify_RejectsMissingMismatchedAndInvalidDispositions()
    {
        var source = Intent();
        AssertResult(Result.MissingIntentProjection, null, []);
        AssertResult(Result.MissingDispositions, source, null);
        AssertResult(Result.DispositionCountMismatch, source, []);
        AssertResult(
            Result.InvalidDisposition,
            source,
            [Disposition.Include, (Disposition)99]);
    }

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassificationProjection
            Projection() => Classify(
                Intent(),
                [Disposition.Include, Disposition.Exclude]);

    private static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjection
            Intent(int observedTick = -30, int maximumCount = 2)
    {
        var batch =
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjectionTests
                .Projection(observedTick, maximumCount);
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjectionResult
                .Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjector
                .Project(batch, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjection>(
                projection);
    }

    private static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassificationProjection
            Classify(
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjection
                    source,
                IReadOnlyList<Disposition> dispositions)
    {
        Assert.Equal(
            Result.Classified,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassifier
                .Classify(source, dispositions, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassificationProjection>(
                projection);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjection?
            source,
        IReadOnlyList<Disposition>? dispositions)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassifier
                .Classify(source, dispositions, out var projection));
        Assert.Null(projection);
    }
}
