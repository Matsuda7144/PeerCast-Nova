using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffFragmentSampleMergeTraversalRequestProjectionResult
{
    Projected,
    InvalidInput,
    CandidateLimitExceeded,
    SourceMismatch,
    InvalidTrack,
    MediaTimescaleMismatch,
    InvalidSampleRange,
}

public sealed class PlaybackIsoBmffFragmentSampleMergeTraversalRequest
{
    internal PlaybackIsoBmffFragmentSampleMergeTraversalRequest(
        int requestIndex,
        uint trackId,
        PlaybackIsoBmffFragmentSampleMergeTraversalEligibleCandidate candidate)
    {
        RequestIndex = requestIndex;
        TrackId = trackId;
        Candidate = candidate;
    }

    public int RequestIndex { get; }

    public uint TrackId { get; }

    public PlaybackIsoBmffFragmentSampleMergeTraversalEligibleCandidate Candidate
    { get; }

    public int SourceIndex => Candidate.SourceIndex;

    public int BatchIndex => Candidate.Sample.BatchIndex;

    public uint SequenceNumber => Candidate.Sample.SequenceNumber;

    public int SourceSampleIndex => Candidate.Sample.Sample.SampleIndex;

    public uint MediaTimescale => Candidate.Sample.Sample.MediaTimescale;

    public uint SampleSize => Candidate.Sample.Sample.SampleSize;

    public uint SampleFlags => Candidate.Sample.Sample.SampleFlags;

    public BigInteger ByteOffset => Candidate.Sample.Sample.ByteOffset;

    public BigInteger ByteEndOffsetExclusive =>
        Candidate.Sample.Sample.ByteEndOffsetExclusive;

    public PlaybackIsoBmffFragmentSampleMergeTraversalDeadline Deadline =>
        Candidate.Eligibility.Observation.Deadline;
}

public sealed class PlaybackIsoBmffFragmentSampleMergeTraversalRequestProjection
{
    internal PlaybackIsoBmffFragmentSampleMergeTraversalRequestProjection(
        PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelection source,
        uint trackId,
        ReadOnlyCollection<PlaybackIsoBmffFragmentSampleMergeTraversalRequest>
            requests)
    {
        Source = source;
        TrackId = trackId;
        Requests = requests;
    }

    public PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelection Source
    { get; }

    public uint TrackId { get; }

    public IReadOnlyList<PlaybackIsoBmffFragmentSampleMergeTraversalRequest>
        Requests
    { get; }

    public int RequestCount => Requests.Count;
}

public static class PlaybackIsoBmffFragmentSampleMergeTraversalRequestProjector
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalRequestProjectionResult
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelection?
                    source,
                out PlaybackIsoBmffFragmentSampleMergeTraversalRequestProjection?
                    projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalRequestProjectionResult
                    .InvalidInput;
        }

        if (source.Candidates.Count >
            PlaybackIsoBmffFragmentSampleMergeTraversalOptions
                .MaximumStepCountHardLimit)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalRequestProjectionResult
                    .CandidateLimitExceeded;
        }

        var merge = source.Source.Source.Source.Source.Source.Source
            .AcknowledgementView.Completion.Merge;
        if (merge.Source.TrackId is not { } trackId || trackId == 0)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalRequestProjectionResult
                    .InvalidTrack;
        }

        var requests = new PlaybackIsoBmffFragmentSampleMergeTraversalRequest[
            source.Candidates.Count];
        var priorSourceIndex = -1;
        for (var index = 0; index < requests.Length; index++)
        {
            var candidate = source.Candidates[index];
            if (candidate.SourceIndex < 0 ||
                candidate.SourceIndex >= source.Source.SampleCount ||
                candidate.SourceIndex <= priorSourceIndex ||
                !ReferenceEquals(
                    candidate.Eligibility,
                    source.Source.Entries[candidate.SourceIndex]))
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalRequestProjectionResult
                        .SourceMismatch;
            }

            var sample = candidate.Sample.Sample;
            if (sample.MediaTimescale != merge.Source.MediaTimescale)
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalRequestProjectionResult
                        .MediaTimescaleMismatch;
            }

            if (sample.ByteOffset < BigInteger.Zero ||
                sample.ByteEndOffsetExclusive <= sample.ByteOffset ||
                sample.ByteEndOffsetExclusive - sample.ByteOffset !=
                    sample.SampleSize)
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalRequestProjectionResult
                        .InvalidSampleRange;
            }

            requests[index] = new(index, trackId, candidate);
            priorSourceIndex = candidate.SourceIndex;
        }

        projection = new(source, trackId, Array.AsReadOnly(requests));
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalRequestProjectionResult
                .Projected;
    }
}
