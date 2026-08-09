using System.Numerics;
using Disposition = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionDisposition;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSummaryResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSummaryTests
{
    [Fact]
    public void Project_AggregatesExactIdentityRangeAndDeadlineFacts()
    {
        var source =
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSourceProjectionTests
                .Projection([Disposition.Include, Disposition.Include]);

        var summary = Project(source);

        Assert.Same(source, summary.Source);
        Assert.Equal(2, summary.EntryCount);
        Assert.False(summary.IsEmpty);
        Assert.Equal(source.Manifest.Entries[0].ConsumerId, summary.ConsumerId);
        Assert.Equal(source.Manifest.Entries[0].BatchId, summary.BatchId);
        Assert.Equal(7u, summary.TrackId);
        Assert.Equal(
            source.Manifest.Entries.Aggregate(
                BigInteger.Zero,
                (total, entry) => total + entry.SampleSize),
            summary.TotalSampleBytes);
        Assert.Equal(
            source.Manifest.Entries.Min(entry => entry.ByteOffset),
            summary.MinimumByteOffset);
        Assert.Equal(
            source.Manifest.Entries.Max(
                entry => entry.ByteEndOffsetExclusive),
            summary.MaximumByteEndOffsetExclusive);
        Assert.Equal(
            source.Manifest.Entries.Min(
                entry => entry.Deadline.DecodeReadyTick),
            summary.EarliestDecodeReadyTick);
        Assert.Equal(
            source.Manifest.Entries.Max(
                entry => entry.Deadline
                    .PresentationExpirationTickExclusive),
            summary.LatestPresentationExpirationTickExclusive);
    }

    [Fact]
    public void Project_EmptyManifestHasZeroAndNoSyntheticIdentityOrRange()
    {
        var source =
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSourceProjectionTests
                .Projection([Disposition.Exclude, Disposition.Exclude]);

        var summary = Project(source);

        Assert.True(summary.IsEmpty);
        Assert.Equal(BigInteger.Zero, summary.TotalSampleBytes);
        Assert.Null(summary.ConsumerId);
        Assert.Null(summary.BatchId);
        Assert.Null(summary.TrackId);
        Assert.Null(summary.MinimumByteOffset);
        Assert.Null(summary.MaximumByteEndOffsetExclusive);
        Assert.Null(summary.EarliestDecodeReadyTick);
        Assert.Null(summary.LatestPresentationExpirationTickExclusive);
    }

    [Fact]
    public void Project_RejectsMissingSource()
    {
        Assert.Equal(
            Result.InvalidInput,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSummaryProjector
                .Project(null, out var summary));
        Assert.Null(summary);
    }

    private static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSummary
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSourceProjection
                    source)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSummaryProjector
                .Project(source, out var summary));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSummary>(
                summary);
    }

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSummary
            Projection(IReadOnlyList<Disposition> dispositions) =>
        Project(
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSourceProjectionTests
                .Projection(dispositions));
}
