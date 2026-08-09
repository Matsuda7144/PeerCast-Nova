using System.Numerics;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginProjectionTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(-100)]
    public void TryProject_AppliesOneSignedOriginAndRetainsExactTimes(
        int originTick)
    {
        var source = IntegralTime();

        var projection = Project(source, originTick);
        Assert.Same(source, projection.Source);
        Assert.Equal(new BigInteger(originTick), projection.OriginTick);
        for (var index = 0; index < projection.EventCount; index++)
        {
            var time = projection.Events[index];
            Assert.Same(source.Events[index], time.Time);
            Assert.Equal(originTick + time.Time.StartTick,
                time.StartTimelineTick);
            Assert.Equal(originTick + time.Time.EndTickExclusive,
                time.EndTimelineTickExclusive);
        }
    }

    [Fact]
    public void TryProject_RejectsMissingAndMismatchedSource()
    {
        AssertResult(Result.MissingIntegralClockTimeProjection, null);
        var source = IntegralTime();
        var reversed = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjection(
                source.Source,
                source.StartRounding,
                source.EndRounding,
                Array.AsReadOnly(source.Events.Reverse().ToArray()));
        AssertResult(Result.SourceMismatch, reversed);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjection?
            source)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginProjector
                .TryProject(source, 0, out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginProjection
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjection
                    source,
                BigInteger originTick)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginProjector
                .TryProject(source, originTick, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginProjection>(
                projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjection
            IntegralTime() =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjectionTests
            .Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjectionTests
                    .ClockTime(1_000),
                PlaybackIsoBmffClockTickRoundingMode.Floor,
                PlaybackIsoBmffClockTickRoundingMode.Ceiling);
}
