namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueAcknowledgementTests
{
    [Fact]
    public void TryCreate_RetainsExactAdvanceTriple()
    {
        var (source, entry, successor) = Advance();

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueAcknowledgementResult
                .Acknowledged,
            PlaybackIsoBmffMovieCodecPacketEventQueueAcknowledgementFactory
                .TryCreate(
                    source,
                    entry,
                    successor,
                    out var acknowledgement));
        var created = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueAcknowledgement>(
                acknowledgement);
        Assert.Same(source, created.SourceCursor);
        Assert.Same(entry, created.Entry);
        Assert.Same(successor, created.SuccessorCursor);
        Assert.Equal(entry.SequenceIndex, created.SequenceIndex);
    }

    [Fact]
    public void TryCreate_RejectsMissingAdvanceParts()
    {
        var (source, entry, successor) = Advance();

        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueAcknowledgementResult
                .MissingSourceCursor,
            null,
            entry,
            successor);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueAcknowledgementResult
                .MissingEntry,
            source,
            null,
            successor);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueAcknowledgementResult
                .MissingSuccessorCursor,
            source,
            entry,
            null);
    }

    [Fact]
    public void TryCreate_RejectsForeignSnapshotAndEntry()
    {
        var (source, entry, successor) = Advance();
        var (foreignSource, foreignEntry, foreignSuccessor) = Advance();

        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueAcknowledgementResult
                .SnapshotMismatch,
            source,
            entry,
            foreignSuccessor);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueAcknowledgementResult
                .SequenceMismatch,
            source,
            foreignEntry,
            successor);
        Assert.NotSame(source.Snapshot, foreignSource.Snapshot);
    }

    [Fact]
    public void TryCreate_RejectsSkippedCursorTransition()
    {
        var (source, entry, successor) = Advance();
        var skipped = new PlaybackIsoBmffMovieCodecPacketEventQueueCursor(
            successor.Snapshot,
            successor.NextSequenceIndex + 1);

        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueAcknowledgementResult
                .InvalidCursorTransition,
            source,
            entry,
            skipped);
    }

    private static void AssertResult(
        PlaybackIsoBmffMovieCodecPacketEventQueueAcknowledgementResult expected,
        PlaybackIsoBmffMovieCodecPacketEventQueueCursor? source,
        PlaybackIsoBmffMovieCodecPacketEventQueueEntry? entry,
        PlaybackIsoBmffMovieCodecPacketEventQueueCursor? successor)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieCodecPacketEventQueueAcknowledgementFactory
                .TryCreate(
                    source,
                    entry,
                    successor,
                    out var acknowledgement));
        Assert.Null(acknowledgement);
    }

    private static (
        PlaybackIsoBmffMovieCodecPacketEventQueueCursor Source,
        PlaybackIsoBmffMovieCodecPacketEventQueueEntry Entry,
        PlaybackIsoBmffMovieCodecPacketEventQueueCursor Successor) Advance()
    {
        var snapshot = Snapshot();
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueCursorCreationResult
                .Created,
            PlaybackIsoBmffMovieCodecPacketEventQueueCursorFactory.TryCreate(
                snapshot,
                out var cursor));
        var source = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueCursor>(cursor);
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueCursorAdvanceResult
                .Advanced,
            PlaybackIsoBmffMovieCodecPacketEventQueueCursorFactory.TryAdvance(
                source,
                out var entry,
                out var successor));
        return (
            source,
            Assert.IsType<PlaybackIsoBmffMovieCodecPacketEventQueueEntry>(
                entry),
            Assert.IsType<PlaybackIsoBmffMovieCodecPacketEventQueueCursor>(
                successor));
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
