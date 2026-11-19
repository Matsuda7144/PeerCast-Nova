using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewTests
{
    [Fact]
    public void TryProject_AssignsGlobalIndexesAcrossContinuationSegments()
    {
        var completion = ChainedCompletion();

        var view = Project(completion);
        Assert.Same(completion, view.Completion);
        Assert.Equal(6, view.EventCount);
        for (var index = 0; index < view.Acknowledgements.Count; index++)
        {
            var acknowledgement = view.Acknowledgements[index];
            Assert.Equal(index, acknowledgement.GlobalTraversalIndex);
            Assert.Same(
                acknowledgement.Acknowledgement.Event,
                acknowledgement.Event);
            if (index > 0)
            {
                Assert.Same(
                    view.Acknowledgements[index - 1].Acknowledgement
                        .SuccessorCursor,
                    acknowledgement.Acknowledgement.SourceCursor);
            }
        }
    }

    [Fact]
    public void TryProject_RetainsExactSingleTraversalAcknowledgements()
    {
        var traversal =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationTests
                .Traversal(16);
        var completion = Completion(traversal);

        var view = Project(completion);
        Assert.Equal(traversal.EventCount, view.EventCount);
        for (var index = 0;
            index < traversal.Acknowledgements.Count;
            index++)
        {
            Assert.Same(
                traversal.Acknowledgements[index],
                view.Acknowledgements[index].Acknowledgement);
        }
    }

    [Fact]
    public void TryProject_AcceptsAlreadyExhaustedZeroEventCompletion()
    {
        var full =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationTests
                .Traversal(16);
        var zero =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTests
                .Traverse(full.FinalCursor, 1);

        var view = Project(Completion(zero));
        Assert.Equal(0, view.EventCount);
        Assert.Empty(view.Acknowledgements);
    }

    [Fact]
    public void TryProject_RejectsMissingCompletion()
    {
        Assert.Equal(
            Result.MissingCompletion,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewProjector
                .TryProject(null, out var view));
        Assert.Null(view);
    }

    [Fact]
    public void TryProject_RejectsCertifiedUnsupportedSourceType()
    {
        var full =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationTests
                .Traversal(16);
        var source = new Source(
            full.InitialCursor,
            full.FinalCursor,
            full.TotalEventCount,
            true);
        var completion = Completion(source);

        Assert.Equal(
            Result.UnsupportedTraversalSource,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewProjector
                .TryProject(completion, out var view));
        Assert.Null(view);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementView
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletion
                    completion)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewProjector
                .TryProject(completion, out var view));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementView>(
                view);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletion
            ChainedCompletion()
    {
        var initial =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationTests
                .Traversal(2);
        var first =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationTests
                .Continue(initial, 2);
        return Completion(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationTests
                .Continue(first, 2));
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletion
            Completion(
                IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalSource
                    source)
    {
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionResult
                .Certified,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionFactory
                .TryCreate(source, out var completion));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletion>(
                completion);
    }

    private sealed record Source(
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor
            InitialCursor,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor
            FinalCursor,
        int TotalEventCount,
        bool IsExhausted) :
        IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalSource;
}
