using Kind = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityKind;
using Policy = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityPolicy;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityTests
{
    [Fact]
    public void Project_ClassifiesDecodeAndPresentationIndependently()
    {
        var source = Observation(-90);
        var options = Options(Policy.ReadyOnly, Policy.ReadyOnly);

        var projection = Project(source, options);

        Assert.Same(source, projection.Source);
        Assert.Same(options, projection.Options);
        var first = projection.Entries[0];
        Assert.Same(source.Observations[0], first.Observation);
        Assert.Equal(Kind.IneligiblePending, first.DecodeKind);
        Assert.Equal(Kind.EligibleReady, first.PresentationKind);
        Assert.False(first.IsDecodeEligible);
        Assert.True(first.IsPresentationEligible);
        Assert.False(first.AreBothEligible);
    }

    [Theory]
    [InlineData(Policy.ReadyOnly, Policy.ReadyOnly, false, false)]
    [InlineData(Policy.ReadyOrExpired, Policy.ReadyOnly, true, false)]
    [InlineData(Policy.ReadyOnly, Policy.ReadyOrExpired, false, true)]
    [InlineData(Policy.ReadyOrExpired, Policy.ReadyOrExpired, true, true)]
    public void Project_AppliesCallerExpiredPoliciesPerDimension(
        Policy decodePolicy,
        Policy presentationPolicy,
        bool expectedDecode,
        bool expectedPresentation)
    {
        var projection = Project(
            Observation(-57),
            Options(decodePolicy, presentationPolicy));

        Assert.Equal(expectedDecode, projection.Entries[0].IsDecodeEligible);
        Assert.Equal(
            expectedPresentation,
            projection.Entries[0].IsPresentationEligible);
        Assert.Equal(
            expectedDecode && expectedPresentation,
            projection.Entries[0].AreBothEligible);
    }

    [Fact]
    public void Project_AcceptsEmptySource()
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

        Assert.Empty(Project(
            observation,
            Options(Policy.ReadyOnly, Policy.ReadyOnly)).Entries);
    }

    [Fact]
    public void Project_RejectsMissingInputAndInvalidPolicies()
    {
        var source = Observation(-79);
        AssertResult(Result.InvalidInput, null, Options(
            Policy.ReadyOnly,
            Policy.ReadyOnly));
        AssertResult(Result.InvalidInput, source, null);
        AssertResult(Result.InvalidPolicy, source, Options(
            (Policy)99,
            Policy.ReadyOnly));
        AssertResult(Result.InvalidPolicy, source, Options(
            Policy.ReadyOnly,
            (Policy)99));
    }

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityProjection
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSnapshot
                    source,
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityOptions
                    options)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityProjector
                .Project(source, options, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityProjection>(
                projection);
    }

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSnapshot
            Observation(int observationTick) =>
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationTests
            .Observe(
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationTests
                    .Deadlines(),
                observationTick);

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityOptions
            Options(Policy decodePolicy, Policy presentationPolicy) =>
        new()
        {
            DecodePolicy = decodePolicy,
            PresentationPolicy = presentationPolicy,
        };

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSnapshot?
            source,
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityOptions?
            options)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityProjector
                .Project(source, options, out var projection));
        Assert.Null(projection);
    }
}
