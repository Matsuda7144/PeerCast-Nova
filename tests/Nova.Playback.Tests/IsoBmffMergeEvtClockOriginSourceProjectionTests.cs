using System.Numerics;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginSourceProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginSourceProjectionTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(-100)]
    public void Project_RetainsExactIntegralSourceAndCallerOrigin(
        int originTick)
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeSourceProjectionTests
                .Projection();

        var projection = Project(source, originTick);
        Assert.Same(source, projection.Source);
        Assert.Equal(new BigInteger(originTick), projection.OriginTick);
        Assert.Same(
            source.IntegralClockTimeProjection,
            projection.ClockOriginProjection.Source);
        Assert.Equal(
            source.IntegralClockTimeProjection.EventCount,
            projection.ClockOriginProjection.EventCount);
    }

    [Fact]
    public void Project_RejectsMissingSource()
    {
        Assert.Equal(
            Result.MissingIntegralClockTimeSourceProjection,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginSourceProjector
                .Project(null, 0, out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginSourceProjection
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeSourceProjection
                    source,
                BigInteger originTick)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginSourceProjector
                .Project(source, originTick, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginSourceProjection>(
                projection);
    }
}
