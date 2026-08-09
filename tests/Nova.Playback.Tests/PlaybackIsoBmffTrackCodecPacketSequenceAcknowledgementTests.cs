namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffTrackCodecPacketSequenceAcknowledgementTests
{
    [Fact]
    public void TryCreate_RetainsExactAdvanceTriple()
    {
        var (source, entry, successor) = Advance();

        Assert.Equal(
            PlaybackIsoBmffTrackCodecPacketSequenceAcknowledgementResult
                .Acknowledged,
            PlaybackIsoBmffTrackCodecPacketSequenceAcknowledgementFactory
                .TryCreate(
                    source,
                    entry,
                    successor,
                    out var acknowledgement));
        var created = Assert.IsType<
            PlaybackIsoBmffTrackCodecPacketSequenceAcknowledgement>(
                acknowledgement);
        Assert.Same(source, created.SourceCursor);
        Assert.Same(entry, created.Entry);
        Assert.Same(successor, created.SuccessorCursor);
        Assert.Equal(entry.SequenceIndex, created.SequenceIndex);
        Assert.Equal(entry.SampleNumber, created.SampleNumber);
    }

    [Fact]
    public void TryCreate_RejectsMissingAdvanceParts()
    {
        var (source, entry, successor) = Advance();

        AssertResult(
            PlaybackIsoBmffTrackCodecPacketSequenceAcknowledgementResult
                .MissingSourceCursor,
            null,
            entry,
            successor);
        AssertResult(
            PlaybackIsoBmffTrackCodecPacketSequenceAcknowledgementResult
                .MissingEntry,
            source,
            null,
            successor);
        AssertResult(
            PlaybackIsoBmffTrackCodecPacketSequenceAcknowledgementResult
                .MissingSuccessorCursor,
            source,
            entry,
            null);
    }

    [Fact]
    public void TryCreate_RejectsForeignSequenceAndEntry()
    {
        var (source, entry, successor) = Advance();
        var (foreignSource, foreignEntry, foreignSuccessor) = Advance();

        AssertResult(
            PlaybackIsoBmffTrackCodecPacketSequenceAcknowledgementResult
                .SequenceMismatch,
            source,
            entry,
            foreignSuccessor);
        AssertResult(
            PlaybackIsoBmffTrackCodecPacketSequenceAcknowledgementResult
                .EntryMismatch,
            source,
            foreignEntry,
            successor);
        Assert.NotSame(source.Sequence, foreignSource.Sequence);
    }

    [Fact]
    public void TryCreate_RejectsSkippedCursorTransition()
    {
        var (source, entry, successor) = Advance();
        var skipped = new PlaybackIsoBmffTrackCodecPacketSequenceCursor(
            successor.Sequence,
            successor.NextSequenceIndex + 1);

        AssertResult(
            PlaybackIsoBmffTrackCodecPacketSequenceAcknowledgementResult
                .InvalidCursorTransition,
            source,
            entry,
            skipped);
    }

    private static void AssertResult(
        PlaybackIsoBmffTrackCodecPacketSequenceAcknowledgementResult expected,
        PlaybackIsoBmffTrackCodecPacketSequenceCursor? source,
        PlaybackIsoBmffTrackCodecPacketSequenceEntry? entry,
        PlaybackIsoBmffTrackCodecPacketSequenceCursor? successor)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffTrackCodecPacketSequenceAcknowledgementFactory
                .TryCreate(
                    source,
                    entry,
                    successor,
                    out var acknowledgement));
        Assert.Null(acknowledgement);
    }

    private static (
        PlaybackIsoBmffTrackCodecPacketSequenceCursor Source,
        PlaybackIsoBmffTrackCodecPacketSequenceEntry Entry,
        PlaybackIsoBmffTrackCodecPacketSequenceCursor Successor) Advance()
    {
        Assert.Equal(
            PlaybackIsoBmffTrackCodecPacketSequenceCursorCreationResult
                .Created,
            PlaybackIsoBmffTrackCodecPacketSequenceCursorFactory.TryCreate(
                PlaybackIsoBmffTrackCodecPacketSequenceTests.Sequence(),
                out var cursor));
        var source = Assert.IsType<
            PlaybackIsoBmffTrackCodecPacketSequenceCursor>(cursor);
        Assert.Equal(
            PlaybackIsoBmffTrackCodecPacketSequenceCursorAdvanceResult
                .Advanced,
            PlaybackIsoBmffTrackCodecPacketSequenceCursorFactory.TryAdvance(
                source,
                out var entry,
                out var successor));
        return (
            source,
            Assert.IsType<PlaybackIsoBmffTrackCodecPacketSequenceEntry>(entry),
            Assert.IsType<PlaybackIsoBmffTrackCodecPacketSequenceCursor>(
                successor));
    }
}
