using System.Numerics;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjectionTests
{
    [Fact]
    public void Project_RetainsExactSignedDecodeAndPresentationIntervals()
    {
        var source = ClockOrigin(-100);

        var projection = Project(source);

        Assert.Same(source, projection.Source);
        Assert.Equal(source.SampleCount, projection.SampleCount);
        var first = projection.Deadlines[0];
        Assert.Same(source.Samples[0], first.Source);
        Assert.Equal(new BigInteger(-79), first.DecodeReadyTick);
        Assert.Equal(new BigInteger(-57),
            first.DecodeExpirationTickExclusive);
        Assert.Equal(new BigInteger(22), first.DecodeDurationTicks);
        Assert.Equal(new BigInteger(-90), first.PresentationReadyTick);
        Assert.Equal(new BigInteger(-68),
            first.PresentationExpirationTickExclusive);
        Assert.Equal(new BigInteger(22), first.PresentationDurationTicks);
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

        Assert.Empty(Project(origin).Deadlines);
    }

    [Fact]
    public void Project_RejectsMissingAndMismatchedSource()
    {
        AssertResult(Result.InvalidInput, null);
        var source = ClockOrigin(0);
        var mismatched = new
            PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjection(
                source.Source,
                source.OriginTick,
                Array.AsReadOnly(source.Samples.Reverse().ToArray()));
        AssertResult(Result.SourceMismatch, mismatched);
    }

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjection Project(
            PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjection
                source)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjector.Project(
                source,
                out var projection));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjection>(
                projection);
    }

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjection
            ClockOrigin(BigInteger originTick) =>
        PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjectionTests
            .Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjectionTests
                    .IntegralTime(),
                originTick);

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjection?
            source)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjector.Project(
                source,
                out var projection));
        Assert.Null(projection);
    }
}
