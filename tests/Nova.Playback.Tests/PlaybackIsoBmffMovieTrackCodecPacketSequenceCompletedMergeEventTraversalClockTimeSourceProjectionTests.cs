using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeSourceProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeSourceProjectionTests
{
    [Fact]
    public void Project_RetainsExactTimeSourceAndCallerClockRate()
    {
        var source = TimeSource();

        var projection = Project(source, 1_000);
        Assert.Same(source, projection.Source);
        Assert.Equal(1_000u, projection.TicksPerSecond);
        Assert.Same(
            source.TimeProjection,
            projection.ClockTimeProjection.Source);
        Assert.Equal(
            source.TimeProjection.EventCount,
            projection.ClockTimeProjection.EventCount);
    }

    [Fact]
    public void Project_RejectsMissingSourceAndInvalidRate()
    {
        AssertResult(Result.MissingTimeSourceProjection, null, 1);
        AssertResult(Result.InvalidTicksPerSecond, TimeSource(), 0);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeSourceProjection
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeSourceProjection
                    source,
                uint ticksPerSecond)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeSourceProjector
                .Project(source, ticksPerSecond, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeSourceProjection>(
                projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeSourceProjection
            TimeSource() =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeSourceProjectionTests
            .Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewSourceProjectionTests
                    .Projection());

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeSourceProjection?
            source,
        uint ticksPerSecond)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeSourceProjector
                .Project(source, ticksPerSecond, out var projection));
        Assert.Null(projection);
    }
}
