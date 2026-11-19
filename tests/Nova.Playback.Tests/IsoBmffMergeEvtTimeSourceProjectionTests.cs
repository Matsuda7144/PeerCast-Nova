using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeSourceProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeSourceProjectionTests
{
    [Fact]
    public void Project_RetainsExactAcknowledgementViewAndRationalTimes()
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewSourceProjectionTests
                .Projection();

        var projection = Project(source);
        Assert.Same(source, projection.Source);
        Assert.Same(source.View, projection.TimeProjection.AcknowledgementView);
        Assert.Equal(source.View.EventCount, projection.TimeProjection.EventCount);
        for (var index = 0; index < projection.TimeProjection.EventCount; index++)
        {
            Assert.Same(
                source.View.Acknowledgements[index],
                projection.TimeProjection.Events[index].Acknowledgement);
            Assert.Equal(
                source.View.Acknowledgements[index].Event.MovieStartNumerator,
                projection.TimeProjection.Events[index].StartSecondsNumerator);
        }
    }

    [Fact]
    public void Project_RejectsMissingSource()
    {
        Assert.Equal(
            Result.MissingAcknowledgementViewProjection,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeSourceProjector
                .Project(null, out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeSourceProjection
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewSourceProjection
                    source)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeSourceProjector
                .Project(source, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeSourceProjection>(
                projection);
    }
}
