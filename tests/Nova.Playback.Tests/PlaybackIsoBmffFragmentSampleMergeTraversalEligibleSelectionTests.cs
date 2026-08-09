using Combination = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalEligibilityCombinationPolicy;
using Policy = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityPolicy;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionTests
{
    [Fact]
    public void Select_AppliesExplicitCombinationPolicy()
    {
        var source = Eligibility(
            -90,
            Policy.ReadyOnly,
            Policy.ReadyOnly);

        var both = Select(source, Combination.RequireBoth, 4);
        Assert.Empty(both.Candidates);
        Assert.Equal(0, both.EligibleCount);

        var either = Select(source, Combination.RequireEither, 4);
        var candidate = Assert.Single(either.Candidates);
        Assert.Equal(0, candidate.SourceIndex);
        Assert.Same(source.Entries[0], candidate.Eligibility);
        Assert.Equal(source.Entries[0].Observation.Deadline.Source.Source.Source.Sample,
            candidate.Sample);
    }

    [Fact]
    public void Select_RetainsSourceOrderAndCallerBound()
    {
        var source = Eligibility(
            -30,
            Policy.ReadyOrExpired,
            Policy.ReadyOrExpired);

        var selection = Select(source, Combination.RequireBoth, 2);

        Assert.Same(source, selection.Source);
        Assert.Equal(Combination.RequireBoth, selection.CombinationPolicy);
        Assert.Equal(2, selection.MaximumCandidateCount);
        Assert.Equal(3, selection.EligibleCount);
        Assert.True(selection.HasMoreEligible);
        Assert.Collection(
            selection.Candidates,
            candidate => Assert.Equal(0, candidate.SourceIndex),
            candidate => Assert.Equal(1, candidate.SourceIndex));
    }

    [Fact]
    public void Select_AcceptsEmptySource()
    {
        var integral =
            PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjectionTests
                .Project(
                    PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjectionTests
                        .Time(0, 0),
                    1_000,
                    PlaybackIsoBmffClockTickRoundingMode.Floor,
                    PlaybackIsoBmffClockTickRoundingMode.Ceiling);
        var origin =
            PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjectionTests
                .Project(integral, 0);
        var deadlines =
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjectionTests
                .Project(origin);
        var observation =
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationTests
                .Observe(deadlines, 0);
        var eligibility =
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityTests
                .Project(
                    observation,
                    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityTests
                        .Options(Policy.ReadyOnly, Policy.ReadyOnly));

        var selection = Select(eligibility, Combination.RequireBoth, 1);
        Assert.Empty(selection.Candidates);
        Assert.Equal(0, selection.EligibleCount);
    }

    [Fact]
    public void Select_RejectsMissingInvalidCombinationLimitAndOrder()
    {
        AssertResult(Result.InvalidInput, null, Combination.RequireBoth, 1);
        var source = Eligibility(-79, Policy.ReadyOnly, Policy.ReadyOnly);
        AssertResult(
            Result.InvalidCombinationPolicy,
            source,
            (Combination)99,
            1);
        AssertResult(
            Result.InvalidMaximumCandidateCount,
            source,
            Combination.RequireBoth,
            0);
        AssertResult(
            Result.InvalidMaximumCandidateCount,
            source,
            Combination.RequireBoth,
            PlaybackIsoBmffFragmentSampleMergeTraversalOptions
                .MaximumStepCountHardLimit + 1);
        var mismatched = new
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityProjection(
                source.Source,
                source.Options,
                Array.AsReadOnly(source.Entries.Reverse().ToArray()));
        AssertResult(
            Result.SourceMismatch,
            mismatched,
            Combination.RequireBoth,
            1);
    }

    internal static PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelection
        Select(
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityProjection
                source,
            Combination combinationPolicy,
            int maximumCandidateCount)
    {
        Assert.Equal(
            Result.Selected,
            PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelector.Select(
                source,
                combinationPolicy,
                maximumCandidateCount,
                out var selection));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelection>(
                selection);
    }

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityProjection
            Eligibility(
                int observationTick,
                Policy decodePolicy,
                Policy presentationPolicy) =>
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityTests
            .Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityTests
                    .Observation(observationTick),
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityTests
                    .Options(decodePolicy, presentationPolicy));

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityProjection?
            source,
        Combination combinationPolicy,
        int maximumCandidateCount)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelector.Select(
                source,
                combinationPolicy,
                maximumCandidateCount,
                out var selection));
        Assert.Null(selection);
    }
}
