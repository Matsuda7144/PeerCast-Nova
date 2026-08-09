using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionProjectionTests
{
    [Fact]
    public void Project_CertifiesExactProjectedTraversalCounts()
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeContinuationProjectionTests
                .Project(
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeContinuationProjectionTests
                        .Initial(),
                    2);

        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionProjector
                .Project(source, out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionProjection>(
                projection);
        Assert.Same(source, projected.Source);
        Assert.Same(
            source.TraversalSource,
            projected.Completion.Source);
        Assert.Same(
            source.CursorSource.CursorSet,
            projected.Completion.InitialCursorSet);
        Assert.Equal(2, projected.Completion.InitialVideoRemainingCount);
        Assert.Equal(2, projected.Completion.InitialAudioRemainingCount);
        Assert.Equal(4, projected.Completion.ExpectedStepCount);
        Assert.Equal(4, projected.Completion.ActualStepCount);
    }

    [Fact]
    public void Project_RejectsMissingAndTruncatedSources()
    {
        AssertResult(Result.MissingTraversalProjection, null);
        AssertResult(
            Result.SourceNotExhausted,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeContinuationProjectionTests
                .Initial());
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionProjection
            Projection()
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeContinuationProjectionTests
                .Project(
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeContinuationProjectionTests
                        .Initial(),
                    2);
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionProjector
                .Project(source, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionProjection>(
                projection);
    }

    private static void AssertResult(
        Result expected,
        IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjectionSource?
            source)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionProjector
                .Project(source, out var projection));
        Assert.Null(projection);
    }
}
