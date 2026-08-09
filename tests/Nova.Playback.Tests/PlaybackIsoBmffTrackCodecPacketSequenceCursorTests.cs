namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffTrackCodecPacketSequenceCursorTests
{
    [Fact]
    public void TryCreate_StartsAtExactFirstEntry()
    {
        var sequence =
            PlaybackIsoBmffTrackCodecPacketSequenceTests.Sequence();

        Assert.Equal(
            PlaybackIsoBmffTrackCodecPacketSequenceCursorCreationResult
                .Created,
            PlaybackIsoBmffTrackCodecPacketSequenceCursorFactory.TryCreate(
                sequence,
                out var cursor));
        var created = Assert.IsType<
            PlaybackIsoBmffTrackCodecPacketSequenceCursor>(cursor);
        Assert.Same(sequence, created.Sequence);
        Assert.Equal(0, created.NextSequenceIndex);
        Assert.Equal(sequence.Entries.Count, created.RemainingCount);
        Assert.False(created.IsExhausted);
    }

    [Fact]
    public void TryAdvance_ReturnsEntryAndNewCursorWithoutMutation()
    {
        var cursor = Cursor();

        Assert.Equal(
            PlaybackIsoBmffTrackCodecPacketSequenceCursorAdvanceResult
                .Advanced,
            PlaybackIsoBmffTrackCodecPacketSequenceCursorFactory.TryAdvance(
                cursor,
                out var entry,
                out var nextCursor));
        Assert.Same(cursor.Sequence.Entries[0], entry);
        Assert.Equal(0, cursor.NextSequenceIndex);
        var next = Assert.IsType<
            PlaybackIsoBmffTrackCodecPacketSequenceCursor>(nextCursor);
        Assert.Same(cursor.Sequence, next.Sequence);
        Assert.Equal(1, next.NextSequenceIndex);
        Assert.Equal(cursor.Sequence.Entries.Count - 1, next.RemainingCount);
    }

    [Fact]
    public void TryAdvance_ReportsExactExhaustion()
    {
        var cursor = Cursor();
        while (!cursor.IsExhausted)
        {
            Assert.Equal(
                PlaybackIsoBmffTrackCodecPacketSequenceCursorAdvanceResult
                    .Advanced,
                PlaybackIsoBmffTrackCodecPacketSequenceCursorFactory
                    .TryAdvance(cursor, out _, out var next));
            cursor = Assert.IsType<
                PlaybackIsoBmffTrackCodecPacketSequenceCursor>(next);
        }

        Assert.Equal(
            PlaybackIsoBmffTrackCodecPacketSequenceCursorAdvanceResult
                .Exhausted,
            PlaybackIsoBmffTrackCodecPacketSequenceCursorFactory.TryAdvance(
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
            PlaybackIsoBmffTrackCodecPacketSequenceCursorCreationResult
                .MissingSequence,
            PlaybackIsoBmffTrackCodecPacketSequenceCursorFactory.TryCreate(
                null,
                out var missing));
        Assert.Null(missing);
        Assert.Equal(
            PlaybackIsoBmffTrackCodecPacketSequenceCursorAdvanceResult
                .MissingCursor,
            PlaybackIsoBmffTrackCodecPacketSequenceCursorFactory.TryAdvance(
                null,
                out var missingEntry,
                out var missingNext));
        Assert.Null(missingEntry);
        Assert.Null(missingNext);

        var sequence =
            PlaybackIsoBmffTrackCodecPacketSequenceTests.Sequence();
        var invalid = new PlaybackIsoBmffTrackCodecPacketSequenceCursor(
            sequence,
            sequence.Entries.Count + 1);
        Assert.Equal(
            PlaybackIsoBmffTrackCodecPacketSequenceCursorAdvanceResult
                .InvalidCursor,
            PlaybackIsoBmffTrackCodecPacketSequenceCursorFactory.TryAdvance(
                invalid,
                out var invalidEntry,
                out var invalidNext));
        Assert.Null(invalidEntry);
        Assert.Null(invalidNext);
    }

    private static PlaybackIsoBmffTrackCodecPacketSequenceCursor Cursor()
    {
        Assert.Equal(
            PlaybackIsoBmffTrackCodecPacketSequenceCursorCreationResult
                .Created,
            PlaybackIsoBmffTrackCodecPacketSequenceCursorFactory.TryCreate(
                PlaybackIsoBmffTrackCodecPacketSequenceTests.Sequence(),
                out var cursor));
        return Assert.IsType<
            PlaybackIsoBmffTrackCodecPacketSequenceCursor>(cursor);
    }
}
