using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewSourceProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewSourceProjectionTests
{
    [Fact]
    public void Project_RetainsExactCompletionAndGlobalStepChain()
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionProjectionTests
                .Projection();

        var projection = Project(source);
        Assert.Same(source, projection.Source);
        Assert.Same(source.Completion, projection.StepView.Completion);
        Assert.Equal(4, projection.StepView.StepCount);
        for (var index = 0; index < projection.StepView.Steps.Count; index++)
        {
            Assert.Equal(index,
                projection.StepView.Steps[index].GlobalMergeIndex);
        }
    }

    [Fact]
    public void Project_RejectsMissingSource()
    {
        Assert.Equal(
            Result.MissingCompletionProjection,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewSourceProjector
                .Project(null, out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewSourceProjection
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionProjection
                    source)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewSourceProjector
                .Project(source, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewSourceProjection>(
                projection);
    }
}
