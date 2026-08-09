using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentProjectionResult
{
    Projected,
    InvalidInput,
    EntryLimitExceeded,
    SourceMismatch,
    IdentityMismatch,
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegment
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegment(
            int segmentIndex,
            int startManifestIndex,
            int fragmentBatchIndex,
            uint sequenceNumber,
            BigInteger totalSampleBytes,
            ReadOnlyCollection<
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestEntry>
                entries)
    {
        SegmentIndex = segmentIndex;
        StartManifestIndex = startManifestIndex;
        FragmentBatchIndex = fragmentBatchIndex;
        SequenceNumber = sequenceNumber;
        TotalSampleBytes = totalSampleBytes;
        Entries = entries;
    }

    public int SegmentIndex { get; }

    public int StartManifestIndex { get; }

    public int FragmentBatchIndex { get; }

    public uint SequenceNumber { get; }

    public BigInteger TotalSampleBytes { get; }

    public IReadOnlyList<
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestEntry>
        Entries
    { get; }

    public int EntryCount => Entries.Count;

    public int EndManifestIndexExclusive => StartManifestIndex + EntryCount;
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentProjection
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentProjection(
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSummary
                source,
            ReadOnlyCollection<
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegment>
                segments)
    {
        Source = source;
        Segments = segments;
    }

    public
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSummary
            Source
    { get; }

    public IReadOnlyList<
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegment>
        Segments
    { get; }

    public int SegmentCount => Segments.Count;
}

public static class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentProjector
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentProjectionResult
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSummary?
                    source,
                out
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentProjectionResult
                    .InvalidInput;
        }

        var manifest = source.Source.Manifest;
        if (manifest.EntryCount >
            PlaybackIsoBmffFragmentSampleMergeTraversalOptions
                .MaximumStepCountHardLimit)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentProjectionResult
                    .EntryLimitExceeded;
        }

        if (source.EntryCount != manifest.EntryCount)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentProjectionResult
                    .SourceMismatch;
        }

        var segments =
            new List<
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegment>();
        var nextIndex = 0;
        while (nextIndex < manifest.EntryCount)
        {
            var startIndex = nextIndex;
            var first = manifest.Entries[startIndex];
            if (first.ManifestIndex != startIndex ||
                first.ConsumerId != source.ConsumerId ||
                first.BatchId != source.BatchId ||
                first.TrackId != source.TrackId)
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentProjectionResult
                        .IdentityMismatch;
            }

            var entries =
                new List<
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestEntry>();
            var totalSampleBytes = BigInteger.Zero;
            while (nextIndex < manifest.EntryCount)
            {
                var entry = manifest.Entries[nextIndex];
                if (entry.FragmentBatchIndex != first.FragmentBatchIndex ||
                    entry.SequenceNumber != first.SequenceNumber)
                {
                    break;
                }

                if (entry.ManifestIndex != nextIndex ||
                    entry.ConsumerId != source.ConsumerId ||
                    entry.BatchId != source.BatchId ||
                    entry.TrackId != source.TrackId)
                {
                    return
                        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentProjectionResult
                            .IdentityMismatch;
                }

                entries.Add(entry);
                totalSampleBytes += entry.SampleSize;
                nextIndex++;
            }

            segments.Add(new(
                segments.Count,
                startIndex,
                first.FragmentBatchIndex,
                first.SequenceNumber,
                totalSampleBytes,
                Array.AsReadOnly(entries.ToArray())));
        }

        projection = new(source, Array.AsReadOnly(segments.ToArray()));
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSegmentProjectionResult
                .Projected;
    }
}
