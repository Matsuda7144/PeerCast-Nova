using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjectionTests
{
    [Fact]
    public void Project_RetainsExactSelectionAndInitialCursors()
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjectionTests
                .Projection();

        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjector
                .Project(source, out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjection>(
                projection);
        Assert.Same(source, projected.Source);
        Assert.Same(source.Selection, projected.CursorSet.Selection);
        Assert.Same(
            source.VideoSource!.Sequence,
            projected.CursorSet.VideoCursor!.Sequence);
        Assert.Same(
            source.AudioSource!.Sequence,
            projected.CursorSet.AudioCursor!.Sequence);
        Assert.Equal(0, projected.CursorSet.VideoCursor.NextSequenceIndex);
        Assert.Equal(0, projected.CursorSet.AudioCursor.NextSequenceIndex);
    }

    [Fact]
    public void Project_RejectsMissingSource()
    {
        Assert.Equal(
            Result.MissingSelectionProjection,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjector
                .Project(null, out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjection
            Projection()
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjectionTests
                .Projection();
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjector
                .Project(source, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjection>(
                projection);
    }
}
