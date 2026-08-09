using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjectionTests
{
    [Fact]
    public void Project_RetainsExactEventProjectionAtIndexZero()
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventSourceProjectionTests
                .Project(
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventSourceProjectionTests
                        .StepView(),
                    8);

        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjector
                .Project(source, out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjection>(
                projection);
        Assert.Same(source, projected.Source);
        Assert.Same(source.EventProjection, projected.Cursor.Projection);
        Assert.Equal(0, projected.Cursor.NextEventIndex);
        Assert.Equal(4, projected.Cursor.RemainingCount);
    }

    [Fact]
    public void Project_RejectsMissingSource()
    {
        Assert.Equal(
            Result.MissingEventProjection,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjector
                .Project(null, out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjection
            Projection()
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventSourceProjectionTests
                .Project(
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventSourceProjectionTests
                        .StepView(),
                    8);
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjector
                .Project(source, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjection>(
                projection);
    }
}
