using System.Numerics;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjectionTests
{
    [Fact]
    public void TryProject_AppliesCallerRateAndRetainsExactTimes()
    {
        var source = TimeProjection();

        var projection = Project(source, 1_000);
        Assert.Same(source, projection.Source);
        Assert.Equal(1_000u, projection.TicksPerSecond);
        Assert.Equal(source.EventCount, projection.EventCount);
        for (var index = 0; index < projection.EventCount; index++)
        {
            var clock = projection.Events[index];
            Assert.Same(source.Events[index], clock.Time);
            Assert.Equal(
                (BigInteger)clock.Time.StartSecondsNumerator * 1_000,
                clock.StartTickNumerator);
            Assert.Equal(
                (BigInteger)clock.Time.EndSecondsNumeratorExclusive * 1_000,
                clock.EndTickNumeratorExclusive);
            Assert.Equal(clock.Time.SecondsDenominator,
                clock.TickDenominator);
        }
    }

    [Fact]
    public void TryProject_UsesBigIntegerForLargeTickNumerators()
    {
        var nearMaximum = Int128.MaxValue - 2;
        var stepView =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjectionTests
                .View(
                    [new(0, nearMaximum - 1, nearMaximum, 90_000, true)],
                    [new(0, nearMaximum, nearMaximum + 1, 48_000, true)],
                    new(0, 0, 0, 1_000, null));
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
        var view =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewTests
                .Project(completion);
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjectionTests
                .Project(view);

        var projection = Project(source, uint.MaxValue);
        Assert.True(
            projection.Events[^1].StartTickNumerator >
            (BigInteger)Int128.MaxValue);
    }

    [Fact]
    public void TryProject_RejectsMissingSourceAndInvalidRate()
    {
        AssertResult(Result.MissingTimeProjection, null, 1);
        AssertResult(Result.InvalidTicksPerSecond, TimeProjection(), 0);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjection?
            source,
        uint ticksPerSecond)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjector
                .TryProject(source, ticksPerSecond, out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjection
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjection
                    source,
                uint ticksPerSecond)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjector
                .TryProject(source, ticksPerSecond, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjection>(
                projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjection
            TimeProjection() =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjectionTests
            .Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjectionTests
                    .View());
}
