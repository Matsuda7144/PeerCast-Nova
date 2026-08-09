namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventAcknowledgementResult
{
    Acknowledged,
    MissingSourceCursor,
    MissingEvent,
    MissingSuccessorCursor,
    ProjectionMismatch,
    EventMismatch,
    InvalidCursorTransition,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventAcknowledgement
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventAcknowledgement(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor
                sourceCursor,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEvent
                packetEvent,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor
                successorCursor)
    {
        SourceCursor = sourceCursor;
        Event = packetEvent;
        SuccessorCursor = successorCursor;
    }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor
        SourceCursor
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEvent Event
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor
        SuccessorCursor
    { get; }

    public int EventIndex => SourceCursor.NextEventIndex;

    public int GlobalMergeIndex => Event.GlobalMergeIndex;

    public int AppearanceIndex => Event.AppearanceIndex;
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventAcknowledgementFactory
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventAcknowledgementResult
            TryCreate(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor?
                    sourceCursor,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEvent?
                    packetEvent,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor?
                    successorCursor,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventAcknowledgement?
                    acknowledgement)
    {
        acknowledgement = null;
        if (sourceCursor is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventAcknowledgementResult
                    .MissingSourceCursor;
        }

        if (packetEvent is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventAcknowledgementResult
                    .MissingEvent;
        }

        if (successorCursor is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventAcknowledgementResult
                    .MissingSuccessorCursor;
        }

        var projection = sourceCursor.Projection;
        if (!ReferenceEquals(projection, successorCursor.Projection))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventAcknowledgementResult
                    .ProjectionMismatch;
        }

        if ((uint)sourceCursor.NextEventIndex >=
                (uint)projection.EventCount ||
            !ReferenceEquals(
                projection.Events[sourceCursor.NextEventIndex],
                packetEvent))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventAcknowledgementResult
                    .EventMismatch;
        }

        if (successorCursor.NextEventIndex !=
            sourceCursor.NextEventIndex + 1)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventAcknowledgementResult
                    .InvalidCursorTransition;
        }

        acknowledgement = new(sourceCursor, packetEvent, successorCursor);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventAcknowledgementResult
                .Acknowledged;
    }
}
