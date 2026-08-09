namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffMovieCodecPacketEventQueueCursorTests
{
    [Fact]
    public void TryCreate_StartsAtFirstImmutableQueueEntry()
    {
        var snapshot = Snapshot();

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueCursorCreationResult
                .Created,
            PlaybackIsoBmffMovieCodecPacketEventQueueCursorFactory.TryCreate(
                snapshot,
                out var cursor));
        var created = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueCursor>(cursor);
        Assert.Same(snapshot, created.Snapshot);
        Assert.Equal(0, created.NextSequenceIndex);
        Assert.Equal(snapshot.Count, created.RemainingCount);
        Assert.False(created.IsExhausted);
    }

    [Fact]
    public void TryAdvance_ReturnsEntryAndNewCursorWithoutMutatingSource()
    {
        var cursor = Cursor();

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueCursorAdvanceResult
                .Advanced,
            PlaybackIsoBmffMovieCodecPacketEventQueueCursorFactory.TryAdvance(
                cursor,
                out var entry,
                out var nextCursor));
        Assert.Same(cursor.Snapshot.Entries[0], entry);
        Assert.Equal(0, cursor.NextSequenceIndex);
        var next = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueCursor>(nextCursor);
        Assert.Same(cursor.Snapshot, next.Snapshot);
        Assert.Equal(1, next.NextSequenceIndex);
        Assert.Equal(cursor.Snapshot.Count - 1, next.RemainingCount);
    }

    [Fact]
    public void TryAdvance_ReportsExhaustionWithoutAnEntry()
    {
        var cursor = Cursor();
        while (!cursor.IsExhausted)
        {
            Assert.Equal(
                PlaybackIsoBmffMovieCodecPacketEventQueueCursorAdvanceResult
                    .Advanced,
                PlaybackIsoBmffMovieCodecPacketEventQueueCursorFactory
                    .TryAdvance(cursor, out _, out var next));
            cursor = Assert.IsType<
                PlaybackIsoBmffMovieCodecPacketEventQueueCursor>(next);
        }

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueCursorAdvanceResult
                .Exhausted,
            PlaybackIsoBmffMovieCodecPacketEventQueueCursorFactory.TryAdvance(
                cursor,
                out var entry,
                out var nextCursor));
        Assert.Null(entry);
        Assert.Null(nextCursor);
        Assert.Equal(0, cursor.RemainingCount);
    }

    [Fact]
    public void Operations_RejectMissingAndInvalidInputs()
    {
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueCursorCreationResult
                .MissingSnapshot,
            PlaybackIsoBmffMovieCodecPacketEventQueueCursorFactory.TryCreate(
                null,
                out var missing));
        Assert.Null(missing);
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueCursorAdvanceResult
                .MissingCursor,
            PlaybackIsoBmffMovieCodecPacketEventQueueCursorFactory.TryAdvance(
                null,
                out var missingEntry,
                out var missingNext));
        Assert.Null(missingEntry);
        Assert.Null(missingNext);

        var snapshot = Snapshot();
        var invalid = new PlaybackIsoBmffMovieCodecPacketEventQueueCursor(
            snapshot,
            snapshot.Count + 1);
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueCursorAdvanceResult
                .InvalidCursor,
            PlaybackIsoBmffMovieCodecPacketEventQueueCursorFactory.TryAdvance(
                invalid,
                out var invalidEntry,
                out var invalidNext));
        Assert.Null(invalidEntry);
        Assert.Null(invalidNext);
    }

    private static PlaybackIsoBmffMovieCodecPacketEventQueueCursor Cursor()
    {
        var snapshot = Snapshot();
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueCursorCreationResult
                .Created,
            PlaybackIsoBmffMovieCodecPacketEventQueueCursorFactory.TryCreate(
                snapshot,
                out var cursor));
        return Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueCursor>(cursor);
    }

    private static PlaybackIsoBmffMovieCodecPacketEventQueueSnapshot Snapshot()
    {
        var eventProjection =
            PlaybackIsoBmffMovieCodecPacketEventOrderTests.Projection();
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventOrderingResult.Ordered,
            PlaybackIsoBmffMovieCodecPacketEventOrderer.Order(
                eventProjection,
                out var order));
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventTimeProjectionResult.Projected,
            PlaybackIsoBmffMovieCodecPacketEventTimeProjector.Project(
                order,
                out var timeProjection));
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueCreationResult.Created,
            PlaybackIsoBmffMovieCodecPacketEventQueueFactory.TryCreate(
                timeProjection,
                out var snapshot));
        return Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueSnapshot>(snapshot);
    }
}
