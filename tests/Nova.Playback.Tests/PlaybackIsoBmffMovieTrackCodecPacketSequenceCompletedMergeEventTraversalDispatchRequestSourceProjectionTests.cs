using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestSourceProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestSourceProjectionTests
{
    [Fact]
    public void Project_RetainsExactBindingsPayloadsAndDeadlines()
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingSourceProjectionTests
                .Project(
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingSourceProjectionTests
                        .TargetSet());

        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestSourceProjector
                .Project(source, out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestSourceProjection>(
                projection);
        Assert.Same(source, projected.Source);
        Assert.Same(
            source.BindingProjection,
            projected.RequestProjection.Source);
        Assert.Equal(
            source.BindingProjection.Bindings.Count,
            projected.RequestProjection.Requests.Count);
        for (var index = 0;
            index < projected.RequestProjection.Requests.Count;
            index++)
        {
            Assert.Same(
                source.BindingProjection.Bindings[index],
                projected.RequestProjection.Requests[index].Binding);
            Assert.False(
                projected.RequestProjection.Requests[index].Payload.IsEmpty);
        }
    }

    [Fact]
    public void Project_RejectsMissingSource()
    {
        Assert.Equal(
            Result.MissingBindingSourceProjection,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestSourceProjector
                .Project(null, out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestSourceProjection
            Projection()
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingSourceProjectionTests
                .Project(
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingSourceProjectionTests
                        .TargetSet());
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestSourceProjector
                .Project(source, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestSourceProjection>(
                projection);
    }
}
