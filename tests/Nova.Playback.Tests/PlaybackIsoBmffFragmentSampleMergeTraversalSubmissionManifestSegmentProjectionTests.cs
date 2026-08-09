using System.Numerics;
using Disposition = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionDisposition;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentProjectionTests
{
    [Fact]
    public void Project_PartitionsAdjacentFragmentBatchAndSequenceRuns()
    {
        var source = Summary(
            [Disposition.Include, Disposition.Include, Disposition.Include]);
        Assert.Equal(3, source.EntryCount);
        Assert.Equal(
            [0, 1, 1],
            source.Source.Manifest.Entries
                .Select(entry => entry.FragmentBatchIndex));

        var projection = Project(source);

        Assert.Same(source, projection.Source);
        Assert.Equal(2, projection.SegmentCount);
        var first = projection.Segments[0];
        Assert.Equal(0, first.SegmentIndex);
        Assert.Equal(0, first.StartManifestIndex);
        Assert.Single(first.Entries);
        Assert.Equal(1, first.EndManifestIndexExclusive);
        Assert.Equal(0, first.FragmentBatchIndex);
        Assert.Equal(10u, first.SequenceNumber);
        Assert.Equal(
            first.Entries.Aggregate(
                BigInteger.Zero,
                (total, entry) => total + entry.SampleSize),
            first.TotalSampleBytes);
        var second = projection.Segments[1];
        Assert.Equal(1, second.StartManifestIndex);
        Assert.Equal(2, second.EntryCount);
        Assert.Equal(1, second.FragmentBatchIndex);
        Assert.Equal(11u, second.SequenceNumber);
    }

    [Fact]
    public void Project_EmptyManifestProducesNoSyntheticSegment()
    {
        var source = Summary(
            [Disposition.Exclude, Disposition.Exclude]);

        Assert.Empty(Project(source).Segments);
    }

    [Fact]
    public void Project_RejectsMissingSource()
    {
        Assert.Equal(
            Result.InvalidInput,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentProjector
                .Project(null, out var projection));
        Assert.Null(projection);
    }

    private static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSummary
            Summary(IReadOnlyList<Disposition> dispositions) =>
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSummaryTests
            .Projection(dispositions);

    private static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentProjection
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSummary
                    source)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentProjector
                .Project(source, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentProjection>(
                projection);
    }
}
