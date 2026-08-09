using System.Numerics;
using Disposition = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionDisposition;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestProjectionTests
{
    [Fact]
    public void Project_FlattensExactSelectedIntentMetadata()
    {
        var source = Selection(
            [Disposition.Exclude, Disposition.Include],
            2);

        var projection = Project(source);

        Assert.Same(source, projection.Source);
        var entry = Assert.Single(projection.Entries);
        var candidate = source.Candidates[0];
        Assert.Equal(0, entry.ManifestIndex);
        Assert.Same(candidate, entry.Candidate);
        Assert.Equal(1, entry.SourceIndex);
        Assert.Equal(candidate.Intent.ConsumerId, entry.ConsumerId);
        Assert.Equal(candidate.Intent.BatchId, entry.BatchId);
        Assert.Equal(7u, entry.TrackId);
        Assert.Equal(candidate.Intent.Request.BatchIndex,
            entry.FragmentBatchIndex);
        Assert.Equal(candidate.Intent.Request.SequenceNumber,
            entry.SequenceNumber);
        Assert.Equal(candidate.Intent.Request.SourceSampleIndex,
            entry.SourceSampleIndex);
        Assert.Equal(48_000u, entry.MediaTimescale);
        Assert.Equal(
            new BigInteger(entry.SampleSize),
            entry.ByteEndOffsetExclusive - entry.ByteOffset);
        Assert.Same(candidate.Intent.Request.Deadline, entry.Deadline);
    }

    [Fact]
    public void Project_AcceptsEmptySelection()
    {
        var source = Selection(
            [Disposition.Exclude, Disposition.Exclude],
            2);

        Assert.Empty(Project(source).Entries);
    }

    [Fact]
    public void Project_RejectsMissingAndReorderedCandidates()
    {
        AssertResult(Result.InvalidInput, null);
        var source = Selection(
            [Disposition.Include, Disposition.Include],
            2);
        var reordered = new
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelection(
                source.Source,
                source.MaximumIntentCount,
                source.IncludedCount,
                Array.AsReadOnly(source.Candidates.Reverse().ToArray()));
        AssertResult(Result.SourceMismatch, reordered);
    }

    private static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestProjection
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelection
                    source)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestProjector
                .Project(source, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestProjection>(
                projection);
    }

    private static PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelection
        Selection(
            IReadOnlyList<Disposition> dispositions,
            int maximumIntentCount)
    {
        var intent =
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjectionTests
                .Projection();
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassificationResult
                .Classified,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassifier
                .Classify(intent, dispositions, out var classification));
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelectionResult
                .Selected,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelector.Select(
                classification,
                maximumIntentCount,
                out var selection));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelection>(
                selection);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelection? source)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestProjector
                .Project(source, out var projection));
        Assert.Null(projection);
    }
}
