using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewProjectionResult;
using TieBreak = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewTests
{
    [Fact]
    public void TryProject_AssignsGlobalIndexesAcrossContinuationSegments()
    {
        var completion = ChainedCompletion();

        var view = Project(completion);
        Assert.Same(completion, view.Completion);
        Assert.Equal(6, view.StepCount);
        for (var index = 0; index < view.Steps.Count; index++)
        {
            Assert.Equal(index, view.Steps[index].GlobalMergeIndex);
            Assert.Same(view.Steps[index].Step.Packet,
                view.Steps[index].Packet);
            if (index > 0)
            {
                Assert.Same(
                    view.Steps[index - 1].Step.SuccessorCursorSet,
                    view.Steps[index].Step.Advance.SourceCursorSet);
            }
        }
    }

    [Fact]
    public void TryProject_RetainsExactSingleTraversalSteps()
    {
        var traversal = FullTraversal();
        var completion = Completion(traversal);

        var view = Project(completion);
        Assert.Equal(traversal.StepCount, view.StepCount);
        for (var index = 0; index < traversal.Steps.Count; index++)
        {
            Assert.Same(traversal.Steps[index], view.Steps[index].Step);
        }
    }

    [Fact]
    public void TryProject_AcceptsAlreadyExhaustedZeroStepCompletion()
    {
        var full = FullTraversal();
        var zero =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalTests
                .Traverse(full.FinalCursorSet, TieBreak.PreferVideo, 1);

        var view = Project(Completion(zero));
        Assert.Equal(0, view.StepCount);
        Assert.Empty(view.Steps);
    }

    [Fact]
    public void TryProject_RejectsMissingCompletion()
    {
        Assert.Equal(
            Result.MissingCompletion,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewProjector
                .TryProject(null, out var view));
        Assert.Null(view);
    }

    [Fact]
    public void TryProject_RejectsCertifiedUnsupportedSourceType()
    {
        var full = FullTraversal();
        var source = new Source(
            full.InitialCursorSet,
            full.FinalCursorSet,
            full.TieBreak,
            full.TotalStepCount,
            true);
        var completion = Completion(source);

        Assert.Equal(
            Result.UnsupportedTraversalSource,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewProjector
                .TryProject(completion, out var view));
        Assert.Null(view);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepView
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletion
                    completion)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewProjector
                .TryProject(completion, out var view));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepView>(
                view);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletion
            ChainedCompletion()
    {
        var initial =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalTests
                .Traverse(
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalTests
                        .InitialCursorSet(),
                    TieBreak.PreferVideo,
                    2);
        var first = Continue(initial, 2);
        return Completion(Continue(first, 2));
    }

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuation
            Continue(
                IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalSource
                    source,
                int limit)
    {
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuationResult
                .Continued,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuationFactory
                .TryContinue(source, limit, out var continuation));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuation>(
                continuation);
    }

    private static PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletion
        Completion(
            IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalSource
                source)
    {
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionResult
                .Certified,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionFactory
                .TryCreate(source, out var completion));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletion>(
                completion);
    }

    private static PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversal
        FullTraversal() =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalTests
            .Traverse(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalTests
                    .InitialCursorSet(),
                TieBreak.PreferVideo,
                16);

    private sealed record Source(
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet InitialCursorSet,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet FinalCursorSet,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak
            TieBreak,
        int TotalStepCount,
        bool IsExhausted) :
        IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalSource;
}
