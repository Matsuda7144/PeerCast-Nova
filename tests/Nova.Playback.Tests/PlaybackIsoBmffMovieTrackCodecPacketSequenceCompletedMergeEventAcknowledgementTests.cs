using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventAcknowledgementResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventAcknowledgementTests
{
    [Fact]
    public void TryCreate_RetainsExactAdvanceTriple()
    {
        var (source, packetEvent, successor) = Advance();

        Assert.Equal(
            Result.Acknowledged,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventAcknowledgementFactory
                .TryCreate(
                    source,
                    packetEvent,
                    successor,
                    out var acknowledgement));
        var created = Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventAcknowledgement>(
                acknowledgement);
        Assert.Same(source, created.SourceCursor);
        Assert.Same(packetEvent, created.Event);
        Assert.Same(successor, created.SuccessorCursor);
        Assert.Equal(0, created.EventIndex);
        Assert.Equal(packetEvent.GlobalMergeIndex, created.GlobalMergeIndex);
        Assert.Equal(packetEvent.AppearanceIndex, created.AppearanceIndex);
    }

    [Fact]
    public void TryCreate_RejectsMissingAdvanceParts()
    {
        var (source, packetEvent, successor) = Advance();

        AssertResult(Result.MissingSourceCursor, null, packetEvent, successor);
        AssertResult(Result.MissingEvent, source, null, successor);
        AssertResult(
            Result.MissingSuccessorCursor,
            source,
            packetEvent,
            null);
    }

    [Fact]
    public void TryCreate_RejectsForeignProjectionAndEvent()
    {
        var (source, packetEvent, successor) = Advance();
        var (_, foreignEvent, foreignSuccessor) = Advance();

        AssertResult(
            Result.ProjectionMismatch,
            source,
            packetEvent,
            foreignSuccessor);
        AssertResult(Result.EventMismatch, source, foreignEvent, successor);
    }

    [Fact]
    public void TryCreate_RejectsSkippedCursorTransition()
    {
        var (source, packetEvent, successor) = Advance();
        var skipped = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor(
                successor.Projection,
                successor.NextEventIndex + 1);

        AssertResult(
            Result.InvalidCursorTransition,
            source,
            packetEvent,
            skipped);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor?
            source,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEvent?
            packetEvent,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor?
            successor)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventAcknowledgementFactory
                .TryCreate(
                    source,
                    packetEvent,
                    successor,
                    out var acknowledgement));
        Assert.Null(acknowledgement);
    }

    private static (
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor
            Source,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEvent Event,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor
            Successor) Advance()
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorTests
                .Cursor(
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorTests
                        .Projection());
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorAdvanceResult
                .Advanced,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorFactory
                .TryAdvance(source, out var packetEvent, out var successor));
        return (
            source,
            Assert.IsType<
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEvent>(
                    packetEvent),
            Assert.IsType<
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor>(
                    successor));
    }
}
