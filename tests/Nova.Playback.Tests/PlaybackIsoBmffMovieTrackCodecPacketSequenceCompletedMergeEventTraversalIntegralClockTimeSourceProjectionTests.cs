using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeSourceProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeSourceProjectionTests
{
    [Fact]
    public void Project_RetainsExactClockSourceAndCallerRoundingPolicy()
    {
        var source = ClockSource();

        var projection = Project(
            source,
            PlaybackIsoBmffClockTickRoundingMode.Floor,
            PlaybackIsoBmffClockTickRoundingMode.Ceiling);
        Assert.Same(source, projection.Source);
        Assert.Equal(
            PlaybackIsoBmffClockTickRoundingMode.Floor,
            projection.StartRounding);
        Assert.Equal(
            PlaybackIsoBmffClockTickRoundingMode.Ceiling,
            projection.EndRounding);
        Assert.Same(
            source.ClockTimeProjection,
            projection.IntegralClockTimeProjection.Source);
        Assert.Equal(
            source.ClockTimeProjection.EventCount,
            projection.IntegralClockTimeProjection.EventCount);
    }

    [Fact]
    public void Project_RejectsMissingSourceAndInvalidPolicy()
    {
        AssertResult(
            Result.MissingClockTimeSourceProjection,
            null,
            PlaybackIsoBmffClockTickRoundingMode.Floor,
            PlaybackIsoBmffClockTickRoundingMode.Ceiling);
        AssertResult(
            Result.InvalidRoundingMode,
            ClockSource(),
            (PlaybackIsoBmffClockTickRoundingMode)99,
            PlaybackIsoBmffClockTickRoundingMode.Ceiling);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeSourceProjection
            Projection() =>
        Project(
            ClockSource(),
            PlaybackIsoBmffClockTickRoundingMode.Floor,
            PlaybackIsoBmffClockTickRoundingMode.Ceiling);

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeSourceProjection
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeSourceProjection
                    source,
                PlaybackIsoBmffClockTickRoundingMode startRounding,
                PlaybackIsoBmffClockTickRoundingMode endRounding)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeSourceProjector
                .Project(
                    source,
                    startRounding,
                    endRounding,
                    out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeSourceProjection>(
                projection);
    }

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeSourceProjection
            ClockSource() =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeSourceProjectionTests
            .Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeSourceProjectionTests
                    .TimeSource(),
                1_000);

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeSourceProjection?
            source,
        PlaybackIsoBmffClockTickRoundingMode startRounding,
        PlaybackIsoBmffClockTickRoundingMode endRounding)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeSourceProjector
                .Project(
                    source,
                    startRounding,
                    endRounding,
                    out var projection));
        Assert.Null(projection);
    }
}
