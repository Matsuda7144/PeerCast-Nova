using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewSourceProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewSourceProjectionTests
{
    [Fact]
    public void Project_ReconstructsExactGlobalAcknowledgementIndexes()
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionProjectionTests
                .Projection();

        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewSourceProjector
                .Project(source, out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewSourceProjection>(
                projection);
        Assert.Same(source, projected.Source);
        Assert.Same(source.Completion, projected.View.Completion);
        Assert.Equal(4, projected.View.EventCount);
        for (var index = 0; index < projected.View.EventCount; index++)
        {
            Assert.Equal(
                index,
                projected.View.Acknowledgements[index].GlobalTraversalIndex);
            Assert.Equal(
                index,
                projected.View.Acknowledgements[index].EventIndex);
        }
        Assert.Same(
            source.Completion.InitialCursor,
            projected.View.Acknowledgements[0].Acknowledgement.SourceCursor);
        Assert.Same(
            source.Completion.FinalCursor,
            projected.View.Acknowledgements[^1].Acknowledgement.SuccessorCursor);
    }

    [Fact]
    public void Project_RejectsMissingSource()
    {
        Assert.Equal(
            Result.MissingCompletionProjection,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewSourceProjector
                .Project(null, out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewSourceProjection
            Projection()
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionProjectionTests
                .Projection();
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewSourceProjector
                .Project(source, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewSourceProjection>(
                projection);
    }
}
