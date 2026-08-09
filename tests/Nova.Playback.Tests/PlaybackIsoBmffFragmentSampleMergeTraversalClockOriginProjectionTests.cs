using System.Numerics;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjectionTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(-100)]
    public void Project_AppliesOneSignedOriginToBothTimelines(int originTick)
    {
        var source = IntegralTime();

        var projection = Project(source, originTick);

        Assert.Same(source, projection.Source);
        Assert.Equal(new BigInteger(originTick), projection.OriginTick);
        for (var index = 0; index < projection.SampleCount; index++)
        {
            var time = projection.Samples[index];
            Assert.Same(source.Samples[index], time.Source);
            Assert.Equal(
                originTick + time.Source.DecodeStartTick,
                time.DecodeStartTimelineTick);
            Assert.Equal(
                originTick + time.Source.DecodeEndTickExclusive,
                time.DecodeEndTimelineTickExclusive);
            Assert.Equal(
                originTick + time.Source.PresentationStartTick,
                time.PresentationStartTimelineTick);
            Assert.Equal(
                originTick + time.Source.PresentationEndTickExclusive,
                time.PresentationEndTimelineTickExclusive);
        }
    }

    [Fact]
    public void Project_AcceptsEmptySource()
    {
        var source =
            PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjectionTests
                .Project(
                    PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjectionTests
                        .Time(0, 0),
                    1_000,
                    PlaybackIsoBmffClockTickRoundingMode.Floor,
                    PlaybackIsoBmffClockTickRoundingMode.Ceiling);

        Assert.Empty(Project(source, 123).Samples);
    }

    [Fact]
    public void Project_RejectsMissingAndMismatchedSource()
    {
        AssertResult(Result.InvalidInput, null);
        var source = IntegralTime();
        var reversed = new
            PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjection(
                source.Source,
                source.TicksPerSecond,
                source.StartRounding,
                source.EndRounding,
                Array.AsReadOnly(source.Samples.Reverse().ToArray()));
        AssertResult(Result.SourceMismatch, reversed);
    }

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjection Project(
            PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjection
                source,
            BigInteger originTick)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjector
                .Project(source, originTick, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjection>(
                projection);
    }

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjection
            IntegralTime() =>
        PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjectionTests
            .Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjectionTests
                    .Time(4, 1),
                1_000,
                PlaybackIsoBmffClockTickRoundingMode.Floor,
                PlaybackIsoBmffClockTickRoundingMode.Ceiling);

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjection?
            source)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjector
                .Project(source, 0, out var projection));
        Assert.Null(projection);
    }
}
