using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjectionTests
{
    [Fact]
    public void TryProject_RetainsExactAcknowledgementsAndRationalSeconds()
    {
        var view = View();

        var projection = Project(view);
        Assert.Same(view, projection.AcknowledgementView);
        Assert.Equal(view.EventCount, projection.EventCount);
        for (var index = 0; index < projection.EventCount; index++)
        {
            var time = projection.Events[index];
            var packetEvent = view.Acknowledgements[index].Event;
            Assert.Same(view.Acknowledgements[index], time.Acknowledgement);
            Assert.Equal(1_000u, time.MovieTimescale);
            Assert.Equal(packetEvent.MovieStartNumerator,
                time.StartSecondsNumerator);
            Assert.Equal(packetEvent.MovieEndNumeratorExclusive,
                time.EndSecondsNumeratorExclusive);
            Assert.Equal(
                (ulong)packetEvent.Denominator * 1_000,
                time.SecondsDenominator);
        }
    }

    [Fact]
    public void TryProject_AcceptsCertifiedZeroEventView()
    {
        var full =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationTests
                .Traversal(16);
        var zero =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTests
                .Traverse(full.FinalCursor, 1);
        var completion =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewTests
                .Completion(zero);
        var view =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewTests
                .Project(completion);

        var projection = Project(view);
        Assert.Equal(0, projection.EventCount);
        Assert.Empty(projection.Events);
    }

    [Fact]
    public void TryProject_RejectsMissingViewAndInvalidMovieTimescale()
    {
        AssertResult(Result.MissingAcknowledgementView, null);
        AssertResult(
            Result.InvalidMovieTimescale,
            View(new(0, 0, 0, 0, null)));
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementView?
            view)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjector
                .TryProject(view, out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjection
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementView
                    view)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjector
                .TryProject(view, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjection>(
                projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementView
            View(PlaybackIsoBmffMovieHeaderTimingBox? movieTiming = null)
    {
        var stepView =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjectionTests
                .View(
                    [new(0, 10, 12, 90_000, true)],
                    [new(0, 20, 22, 48_000, true)],
                    movieTiming);
        var eventProjection =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjectionTests
                .Project(stepView, 16);
        var cursor =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorTests
                .Cursor(eventProjection);
        var traversal =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTests
                .Traverse(cursor, 16);
        var completion =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewTests
                .Completion(traversal);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewTests
                .Project(completion);
    }
}
