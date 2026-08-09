using AdvanceResult = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorAdvanceResult;
using CreationResult = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorCreationResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorTests
{
    [Fact]
    public void TryCreate_StartsAtExactFirstEvent()
    {
        var projection = Projection();

        var cursor = Cursor(projection);
        Assert.Same(projection, cursor.Projection);
        Assert.Equal(0, cursor.NextEventIndex);
        Assert.Equal(projection.EventCount, cursor.RemainingCount);
        Assert.False(cursor.IsExhausted);
    }

    [Fact]
    public void TryAdvance_ReturnsExactEventAndImmutableSuccessor()
    {
        var cursor = Cursor(Projection());

        Assert.Equal(
            AdvanceResult.Advanced,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorFactory
                .TryAdvance(cursor, out var packetEvent, out var successor));
        Assert.Same(cursor.Projection.Events[0], packetEvent);
        Assert.Equal(0, cursor.NextEventIndex);
        var next = Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor>(
                successor);
        Assert.Same(cursor.Projection, next.Projection);
        Assert.Equal(1, next.NextEventIndex);
        Assert.Equal(cursor.RemainingCount - 1, next.RemainingCount);
    }

    [Fact]
    public void TryAdvance_ReportsExactExhaustionWithoutEvent()
    {
        var cursor = Cursor(Projection());
        while (!cursor.IsExhausted)
        {
            Assert.Equal(
                AdvanceResult.Advanced,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorFactory
                    .TryAdvance(cursor, out _, out var successor));
            cursor = Assert.IsType<
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor>(
                    successor);
        }

        Assert.Equal(
            AdvanceResult.Exhausted,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorFactory
                .TryAdvance(
                    cursor,
                    out var packetEvent,
                    out var exhaustedSuccessor));
        Assert.Null(packetEvent);
        Assert.Null(exhaustedSuccessor);
        Assert.Equal(0, cursor.RemainingCount);
    }

    [Fact]
    public void Operations_RejectMissingAndInvalidInputs()
    {
        Assert.Equal(
            CreationResult.MissingProjection,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorFactory
                .TryCreate(null, out var missing));
        Assert.Null(missing);
        Assert.Equal(
            AdvanceResult.MissingCursor,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorFactory
                .TryAdvance(null, out var missingEvent, out var missingNext));
        Assert.Null(missingEvent);
        Assert.Null(missingNext);

        var projection = Projection();
        var invalid = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor(
                projection,
                projection.EventCount + 1);
        Assert.Equal(
            AdvanceResult.InvalidCursor,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorFactory
                .TryAdvance(invalid, out var invalidEvent, out var invalidNext));
        Assert.Null(invalidEvent);
        Assert.Null(invalidNext);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor
            Cursor(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjection
                    projection)
    {
        Assert.Equal(
            CreationResult.Created,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorFactory
                .TryCreate(projection, out var cursor));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor>(
                cursor);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjection
            Projection() =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjectionTests
            .Projection();
}
