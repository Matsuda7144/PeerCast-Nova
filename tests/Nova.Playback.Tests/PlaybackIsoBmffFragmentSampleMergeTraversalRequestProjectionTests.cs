using System.Numerics;
using Combination = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalEligibilityCombinationPolicy;
using Policy = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityPolicy;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalRequestProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalRequestProjectionTests
{
    [Fact]
    public void Project_RetainsExactCandidateAndSampleMetadata()
    {
        var source = Selection();

        var projection = Project(source);

        Assert.Same(source, projection.Source);
        Assert.Equal(7u, projection.TrackId);
        Assert.Equal(2, projection.RequestCount);
        for (var index = 0; index < projection.RequestCount; index++)
        {
            var request = projection.Requests[index];
            var candidate = source.Candidates[index];
            Assert.Equal(index, request.RequestIndex);
            Assert.Same(candidate, request.Candidate);
            Assert.Equal(candidate.SourceIndex, request.SourceIndex);
            Assert.Equal(candidate.Sample.BatchIndex, request.BatchIndex);
            Assert.Equal(candidate.Sample.SequenceNumber, request.SequenceNumber);
            Assert.Equal(
                candidate.Sample.Sample.SampleIndex,
                request.SourceSampleIndex);
            Assert.Equal(48_000u, request.MediaTimescale);
            Assert.Equal(
                new BigInteger(request.SampleSize),
                request.ByteEndOffsetExclusive - request.ByteOffset);
            Assert.Same(candidate.Eligibility.Observation.Deadline,
                request.Deadline);
        }
    }

    [Fact]
    public void Project_AcceptsEmptySelection()
    {
        var eligibility =
            PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionTests
                .Eligibility(-91, Policy.ReadyOnly, Policy.ReadyOnly);
        var selection =
            PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionTests
                .Select(eligibility, Combination.RequireBoth, 1);

        var projection = Project(selection);
        Assert.Empty(projection.Requests);
        Assert.Equal(7u, projection.TrackId);
    }

    [Fact]
    public void Project_RejectsMissingAndMismatchedCandidateOrder()
    {
        AssertResult(Result.InvalidInput, null);
        var source = Selection();
        var mismatched = new
            PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelection(
                source.Source,
                source.CombinationPolicy,
                source.MaximumCandidateCount,
                source.EligibleCount,
                Array.AsReadOnly(source.Candidates.Reverse().ToArray()));
        AssertResult(Result.SourceMismatch, mismatched);
    }

    internal static PlaybackIsoBmffFragmentSampleMergeTraversalRequestProjection
        Project(
            PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelection source)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalRequestProjector.Project(
                source,
                out var projection));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalRequestProjection>(
                projection);
    }

    internal static PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelection
        Selection()
    {
        var eligibility =
            PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionTests
                .Eligibility(
                    -30,
                    Policy.ReadyOrExpired,
                    Policy.ReadyOrExpired);
        return PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionTests
            .Select(eligibility, Combination.RequireBoth, 2);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelection? source)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffFragmentSampleMergeTraversalRequestProjector.Project(
                source,
                out var projection));
        Assert.Null(projection);
    }
}
