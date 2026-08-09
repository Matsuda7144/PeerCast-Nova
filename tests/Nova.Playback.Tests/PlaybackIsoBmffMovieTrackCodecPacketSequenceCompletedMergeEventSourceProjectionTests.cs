using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventSourceProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventSourceProjectionTests
{
    [Fact]
    public void Project_RetainsExactStepViewAndOrderedEvents()
    {
        var source = StepView();

        var projection = Project(source, 8);
        Assert.Same(source, projection.Source);
        Assert.Equal(8, projection.MaximumEventCount);
        Assert.Same(source.StepView, projection.EventProjection.StepView);
        Assert.Equal(4, projection.EventProjection.EventCount);
    }

    [Fact]
    public void Project_RejectsMissingSourceAndInvalidLimit()
    {
        AssertResult(Result.MissingStepViewProjection, null, 1);
        AssertResult(Result.InvalidEventLimit, StepView(), 0);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventSourceProjection
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewSourceProjection
                    source,
                int maximumEventCount)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventSourceProjector
                .Project(source, maximumEventCount, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventSourceProjection>(
                projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewSourceProjection
            StepView() =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewSourceProjectionTests
            .Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionProjectionTests
                    .Projection());

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewSourceProjection?
            source,
        int maximumEventCount)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventSourceProjector
                .Project(source, maximumEventCount, out var projection));
        Assert.Null(projection);
    }
}
