using System.Numerics;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineProjectionTests
{
    [Fact]
    public void TryProject_RetainsExactSignedTimelineIntervals()
    {
        var source = ClockOrigin(-10);

        var projection = Project(source);
        Assert.Same(source, projection.Source);
        Assert.Equal(source.EventCount, projection.EventCount);
        Assert.Equal(new BigInteger(-10), projection.Deadlines[0].ReadyTick);
        Assert.Equal(new BigInteger(-9),
            projection.Deadlines[0].ExpirationTickExclusive);
        Assert.Equal(BigInteger.One,
            projection.Deadlines[0].DurationTicks);
    }

    [Fact]
    public void TryProject_RejectsMissingAndMismatchedSource()
    {
        AssertResult(Result.MissingClockOriginProjection, null);
        var source = ClockOrigin(0);
        var mismatched = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginProjection(
                source.Source,
                source.OriginTick,
                Array.AsReadOnly(source.Events.Reverse().ToArray()));
        AssertResult(Result.SourceMismatch, mismatched);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginProjection?
            source)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineProjector
                .TryProject(source, out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineProjection
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginProjection
                    source)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineProjector
                .TryProject(source, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineProjection>(
                projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginProjection
            ClockOrigin(BigInteger originTick) =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginProjectionTests
            .Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockOriginProjectionTests
                    .IntegralTime(),
                originTick);
}
