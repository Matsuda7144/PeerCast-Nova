using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeContinuationProjectionResult;
using TieBreak = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeContinuationProjectionTests
{
    [Fact]
    public void Project_ContinuesExactFinalStateToExhaustion()
    {
        var source = Initial();

        var projection = Project(source, 2);
        Assert.Same(source, projection.Source);
        Assert.Equal(2, projection.AdditionalStepLimit);
        Assert.Same(
            source.Traversal.FinalCursorSet,
            projection.Continuation.Segment.InitialCursorSet);
        Assert.Equal(2, projection.Continuation.StartingMergeIndex);
        Assert.Equal(4, projection.Continuation.TotalStepCount);
        Assert.True(projection.Continuation.IsExhausted);
        Assert.Same(
            source.CursorSource.CursorSet,
            projection.Continuation.InitialCursorSet);
    }

    [Fact]
    public void Project_RejectsMissingSourceAndInvalidLimit()
    {
        AssertResult(Result.MissingSourceProjection, null, 1);
        AssertResult(Result.InvalidAdditionalStepLimit, Initial(), 0);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeContinuationProjection
            Project(
                IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjectionSource
                    source,
                int additionalStepLimit)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeContinuationProjector
                .Project(
                    source,
                    additionalStepLimit,
                    out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeContinuationProjection>(
                projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjection
            Initial() =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjectionTests
            .Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjectionTests
                    .Projection(),
                TieBreak.PreferVideo,
                2);

    private static void AssertResult(
        Result expected,
        IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjectionSource?
            source,
        int additionalStepLimit)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeContinuationProjector
                .Project(
                    source,
                    additionalStepLimit,
                    out var projection));
        Assert.Null(projection);
    }
}
