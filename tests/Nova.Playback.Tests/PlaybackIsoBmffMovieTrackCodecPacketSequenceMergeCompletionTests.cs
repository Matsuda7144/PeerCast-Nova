using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionResult;
using TieBreak = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionTests
{
    [Fact]
    public void TryCreate_CertifiesExactExhaustedTraversalCounts()
    {
        var traversal = FullTraversal();

        var completion = Create(traversal);
        Assert.Same(traversal, completion.Source);
        Assert.Same(traversal.InitialCursorSet,
            completion.InitialCursorSet);
        Assert.Same(traversal.FinalCursorSet,
            completion.FinalCursorSet);
        Assert.Equal(3, completion.InitialVideoRemainingCount);
        Assert.Equal(3, completion.InitialAudioRemainingCount);
        Assert.Equal(6, completion.ExpectedStepCount);
        Assert.Equal(6, completion.ActualStepCount);
    }

    [Fact]
    public void TryCreate_CertifiesChainedContinuationTotal()
    {
        var initial =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalTests
                .Traverse(
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalTests
                        .InitialCursorSet(),
                    TieBreak.PreferVideo,
                    2);
        var first = Continue(initial, 2);
        var second = Continue(first, 2);

        var completion = Create(second);
        Assert.Same(second, completion.Source);
        Assert.Equal(6, completion.ActualStepCount);
        Assert.Same(initial.InitialCursorSet,
            completion.InitialCursorSet);
    }

    [Fact]
    public void TryCreate_CertifiesAlreadyExhaustedZeroRemainingState()
    {
        var full = FullTraversal();
        var zero =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalTests
                .Traverse(
                    full.FinalCursorSet,
                    TieBreak.PreferVideo,
                    1);

        var completion = Create(zero);
        Assert.Equal(0, completion.ExpectedStepCount);
        Assert.Equal(0, completion.ActualStepCount);
    }

    [Fact]
    public void TryCreate_RejectsMissingAndTruncatedSources()
    {
        AssertResult(Result.MissingSource, null);
        var truncated =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalTests
                .Traverse(
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalTests
                        .InitialCursorSet(),
                    TieBreak.PreferVideo,
                    2);
        AssertResult(Result.SourceNotExhausted, truncated);
    }

    [Fact]
    public void TryCreate_RejectsInconsistentCumulativeStepCount()
    {
        var full = FullTraversal();
        var invalid = new Source(
            full.InitialCursorSet,
            full.FinalCursorSet,
            full.TieBreak,
            full.TotalStepCount - 1,
            true);

        AssertResult(Result.StepCountMismatch, invalid);
    }

    private static void AssertResult(
        Result expected,
        IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalSource?
            source)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionFactory
                .TryCreate(source, out var completion));
        Assert.Null(completion);
    }

    private static PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletion
        Create(
            IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalSource
                source)
    {
        Assert.Equal(
            Result.Certified,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionFactory
                .TryCreate(source, out var completion));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletion>(
                completion);
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
