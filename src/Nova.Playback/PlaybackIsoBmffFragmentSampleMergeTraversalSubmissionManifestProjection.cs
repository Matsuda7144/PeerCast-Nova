using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestProjectionResult
{
    Projected,
    InvalidInput,
    CandidateLimitExceeded,
    SourceMismatch,
    InvalidIdentity,
    InvalidSampleRange,
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestEntry
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestEntry(
            int manifestIndex,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelectionCandidate
                candidate)
    {
        ManifestIndex = manifestIndex;
        Candidate = candidate;
    }

    public int ManifestIndex { get; }

    public
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelectionCandidate
            Candidate
    { get; }

    public int SourceIndex => Candidate.SourceIndex;

    public Guid ConsumerId => Candidate.Intent.ConsumerId;

    public Guid BatchId => Candidate.Intent.BatchId;

    public uint TrackId => Candidate.Intent.Request.TrackId;

    public int FragmentBatchIndex => Candidate.Intent.Request.BatchIndex;

    public uint SequenceNumber => Candidate.Intent.Request.SequenceNumber;

    public int SourceSampleIndex => Candidate.Intent.Request.SourceSampleIndex;

    public uint MediaTimescale => Candidate.Intent.Request.MediaTimescale;

    public uint SampleSize => Candidate.Intent.Request.SampleSize;

    public BigInteger ByteOffset => Candidate.Intent.Request.ByteOffset;

    public BigInteger ByteEndOffsetExclusive =>
        Candidate.Intent.Request.ByteEndOffsetExclusive;

    public PlaybackIsoBmffFragmentSampleMergeTraversalDeadline Deadline =>
        Candidate.Intent.Request.Deadline;
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestProjection
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestProjection(
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelection
                source,
            ReadOnlyCollection<
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestEntry>
                entries)
    {
        Source = source;
        Entries = entries;
    }

    public PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelection Source
    { get; }

    public IReadOnlyList<
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestEntry>
        Entries
    { get; }

    public int EntryCount => Entries.Count;
}

public static class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestProjector
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestProjectionResult
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionSelection?
                    source,
                out
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestProjectionResult
                    .InvalidInput;
        }

        if (source.CandidateCount >
            PlaybackIsoBmffFragmentSampleMergeTraversalOptions
                .MaximumStepCountHardLimit)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestProjectionResult
                    .CandidateLimitExceeded;
        }

        var entries =
            new PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestEntry[
                source.CandidateCount];
        var priorSourceIndex = -1;
        for (var index = 0; index < entries.Length; index++)
        {
            var candidate = source.Candidates[index];
            if (candidate.SourceIndex <= priorSourceIndex ||
                candidate.SourceIndex < 0 ||
                candidate.SourceIndex >= source.Source.Classifications.Count ||
                !ReferenceEquals(
                    candidate.Classification,
                    source.Source.Classifications[candidate.SourceIndex]) ||
                !candidate.Classification.IsIncluded)
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestProjectionResult
                        .SourceMismatch;
            }

            var intent = candidate.Intent;
            if (intent.ConsumerId == Guid.Empty || intent.BatchId == Guid.Empty ||
                intent.Request.TrackId == 0)
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestProjectionResult
                        .InvalidIdentity;
            }

            var request = intent.Request;
            if (request.ByteOffset < BigInteger.Zero ||
                request.ByteEndOffsetExclusive <= request.ByteOffset ||
                request.ByteEndOffsetExclusive - request.ByteOffset !=
                    request.SampleSize)
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestProjectionResult
                        .InvalidSampleRange;
            }

            entries[index] = new(index, candidate);
            priorSourceIndex = candidate.SourceIndex;
        }

        projection = new(source, Array.AsReadOnly(entries));
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestProjectionResult
                .Projected;
    }
}
