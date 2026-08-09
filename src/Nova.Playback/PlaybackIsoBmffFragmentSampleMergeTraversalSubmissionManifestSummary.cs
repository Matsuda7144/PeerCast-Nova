using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSummaryResult
{
    Projected,
    InvalidInput,
    EntryLimitExceeded,
    SourceMismatch,
    IdentityMismatch,
    InvalidSampleRange,
    InvalidDeadlineInterval,
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSummary
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSummary(
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSourceProjection
                source,
            Guid? consumerId,
            Guid? batchId,
            uint? trackId,
            BigInteger totalSampleBytes,
            BigInteger? minimumByteOffset,
            BigInteger? maximumByteEndOffsetExclusive,
            BigInteger? earliestDecodeReadyTick,
            BigInteger? latestDecodeExpirationTickExclusive,
            BigInteger? earliestPresentationReadyTick,
            BigInteger? latestPresentationExpirationTickExclusive)
    {
        Source = source;
        ConsumerId = consumerId;
        BatchId = batchId;
        TrackId = trackId;
        TotalSampleBytes = totalSampleBytes;
        MinimumByteOffset = minimumByteOffset;
        MaximumByteEndOffsetExclusive = maximumByteEndOffsetExclusive;
        EarliestDecodeReadyTick = earliestDecodeReadyTick;
        LatestDecodeExpirationTickExclusive =
            latestDecodeExpirationTickExclusive;
        EarliestPresentationReadyTick = earliestPresentationReadyTick;
        LatestPresentationExpirationTickExclusive =
            latestPresentationExpirationTickExclusive;
    }

    public
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSourceProjection
            Source
    { get; }

    public int EntryCount => Source.Manifest.EntryCount;

    public bool IsEmpty => EntryCount == 0;

    public Guid? ConsumerId { get; }

    public Guid? BatchId { get; }

    public uint? TrackId { get; }

    public BigInteger TotalSampleBytes { get; }

    public BigInteger? MinimumByteOffset { get; }

    public BigInteger? MaximumByteEndOffsetExclusive { get; }

    public BigInteger? EarliestDecodeReadyTick { get; }

    public BigInteger? LatestDecodeExpirationTickExclusive { get; }

    public BigInteger? EarliestPresentationReadyTick { get; }

    public BigInteger? LatestPresentationExpirationTickExclusive { get; }
}

public static class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSummaryProjector
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSummaryResult
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSourceProjection?
                    source,
                out
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSummary?
                        summary)
    {
        summary = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSummaryResult
                    .InvalidInput;
        }

        var manifest = source.Manifest;
        if (manifest.EntryCount >
            PlaybackIsoBmffFragmentSampleMergeTraversalOptions
                .MaximumStepCountHardLimit)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSummaryResult
                    .EntryLimitExceeded;
        }

        if (!ReferenceEquals(
                manifest.Source.Source.Source.Source,
                source.Source))
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSummaryResult
                    .SourceMismatch;
        }

        Guid? consumerId = null;
        Guid? batchId = null;
        uint? trackId = null;
        var totalSampleBytes = BigInteger.Zero;
        BigInteger? minimumByteOffset = null;
        BigInteger? maximumByteEndOffsetExclusive = null;
        BigInteger? earliestDecodeReadyTick = null;
        BigInteger? latestDecodeExpirationTickExclusive = null;
        BigInteger? earliestPresentationReadyTick = null;
        BigInteger? latestPresentationExpirationTickExclusive = null;
        for (var index = 0; index < manifest.EntryCount; index++)
        {
            var entry = manifest.Entries[index];
            if (entry.ManifestIndex != index ||
                !ReferenceEquals(entry.Candidate, manifest.Source.Candidates[index]))
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSummaryResult
                        .SourceMismatch;
            }

            if (entry.ConsumerId == Guid.Empty || entry.BatchId == Guid.Empty ||
                entry.TrackId == 0 ||
                consumerId is not null && entry.ConsumerId != consumerId ||
                batchId is not null && entry.BatchId != batchId ||
                trackId is not null && entry.TrackId != trackId)
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSummaryResult
                        .IdentityMismatch;
            }

            if (entry.ByteOffset < BigInteger.Zero ||
                entry.ByteEndOffsetExclusive <= entry.ByteOffset ||
                entry.ByteEndOffsetExclusive - entry.ByteOffset !=
                    entry.SampleSize)
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSummaryResult
                        .InvalidSampleRange;
            }

            var deadline = entry.Deadline;
            if (deadline.DecodeReadyTick >=
                    deadline.DecodeExpirationTickExclusive ||
                deadline.PresentationReadyTick >=
                    deadline.PresentationExpirationTickExclusive)
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSummaryResult
                        .InvalidDeadlineInterval;
            }

            consumerId ??= entry.ConsumerId;
            batchId ??= entry.BatchId;
            trackId ??= entry.TrackId;
            totalSampleBytes += entry.SampleSize;
            minimumByteOffset = Minimum(minimumByteOffset, entry.ByteOffset);
            maximumByteEndOffsetExclusive = Maximum(
                maximumByteEndOffsetExclusive,
                entry.ByteEndOffsetExclusive);
            earliestDecodeReadyTick = Minimum(
                earliestDecodeReadyTick,
                deadline.DecodeReadyTick);
            latestDecodeExpirationTickExclusive = Maximum(
                latestDecodeExpirationTickExclusive,
                deadline.DecodeExpirationTickExclusive);
            earliestPresentationReadyTick = Minimum(
                earliestPresentationReadyTick,
                deadline.PresentationReadyTick);
            latestPresentationExpirationTickExclusive = Maximum(
                latestPresentationExpirationTickExclusive,
                deadline.PresentationExpirationTickExclusive);
        }

        summary = new(
            source,
            consumerId,
            batchId,
            trackId,
            totalSampleBytes,
            minimumByteOffset,
            maximumByteEndOffsetExclusive,
            earliestDecodeReadyTick,
            latestDecodeExpirationTickExclusive,
            earliestPresentationReadyTick,
            latestPresentationExpirationTickExclusive);
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSummaryResult
                .Projected;
    }

    private static BigInteger Minimum(BigInteger? current, BigInteger value) =>
        current is null || value < current.Value ? value : current.Value;

    private static BigInteger Maximum(BigInteger? current, BigInteger value) =>
        current is null || value > current.Value ? value : current.Value;
}
