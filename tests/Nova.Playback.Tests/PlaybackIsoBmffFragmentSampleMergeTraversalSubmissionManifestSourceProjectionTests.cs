using Disposition = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionDisposition;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSourceProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSourceProjectionTests
{
    [Fact]
    public void Project_BindsManifestToExactConsumerBatchSource()
    {
        var source = Batch();
        var manifest = Manifest(source);

        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSourceProjector
                .Project(source, manifest, out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSourceProjection>(
                projection);
        Assert.Same(source, projected.Source);
        Assert.Same(manifest, projected.Manifest);
    }

    [Fact]
    public void Project_RejectsMissingAndForeignSources()
    {
        var source = Batch();
        var manifest = Manifest(source);
        AssertResult(Result.MissingBatchProjection, null, manifest);
        AssertResult(Result.MissingManifestProjection, source, null);
        AssertResult(Result.SourceMismatch, source, Manifest(Batch()));
    }

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSourceProjection
            Projection(IReadOnlyList<Disposition>? dispositions = null)
    {
        var effectiveDispositions =
            dispositions ?? [Disposition.Include, Disposition.Exclude];
        var source = Batch(Math.Max(1, effectiveDispositions.Count), 10_000);
        var manifest = Manifest(
            source,
            effectiveDispositions);
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSourceProjector
                .Project(source, manifest, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSourceProjection>(
                projection);
    }

    private static
        PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjection
            Batch(int maximumCount = 2, int observationTick = -30) =>
        PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjectionTests
            .Projection(observationTick, maximumCount);

    private static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestProjection
            Manifest(
                PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjection
                    source,
                IReadOnlyList<Disposition>? dispositions = null)
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjectionResult
                .Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjector
                .Project(source, out var intent));
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassificationResult
                .Classified,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassifier
                .Classify(
                    intent,
                    dispositions ??
                        [Disposition.Include, Disposition.Exclude],
                    out var classification));
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelectionResult
                .Selected,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelector.Select(
                classification,
                Math.Max(1, dispositions?.Count ?? 2),
                out var selection));
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestProjectionResult
                .Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestProjector
                .Project(selection, out var manifest));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestProjection>(
                manifest);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjection?
            source,
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestProjection?
            manifest)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSourceProjector
                .Project(source, manifest, out var projection));
        Assert.Null(projection);
    }
}
