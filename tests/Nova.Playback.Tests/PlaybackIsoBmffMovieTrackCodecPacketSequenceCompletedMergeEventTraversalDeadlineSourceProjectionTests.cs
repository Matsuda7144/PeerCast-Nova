using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineSourceProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineSourceProjectionTests
{
    [Fact]
    public void Project_RetainsExactClockOriginSourceAndDeadlines()
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginSourceProjectionTests
                .Project(
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeSourceProjectionTests
                        .Projection(),
                    -10);

        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineSourceProjector
                .Project(source, out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineSourceProjection>(
                projection);
        Assert.Same(source, projected.Source);
        Assert.Same(
            source.ClockOriginProjection,
            projected.DeadlineProjection.Source);
        Assert.Equal(
            source.ClockOriginProjection.EventCount,
            projected.DeadlineProjection.EventCount);
        Assert.Equal(
            source.ClockOriginProjection.Events[0].StartTimelineTick,
            projected.DeadlineProjection.Deadlines[0].ReadyTick);
    }

    [Fact]
    public void Project_RejectsMissingSource()
    {
        Assert.Equal(
            Result.MissingClockOriginSourceProjection,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineSourceProjector
                .Project(null, out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineSourceProjection
            Projection()
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginSourceProjectionTests
                .Project(
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeSourceProjectionTests
                        .Projection(),
                    100);
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineSourceProjector
                .Project(source, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineSourceProjection>(
                projection);
    }
}
