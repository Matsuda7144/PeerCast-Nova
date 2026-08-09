namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorCreationResult
{
    Created,
    MissingProjection,
    EventLimitExceeded,
}

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorAdvanceResult
{
    Advanced,
    Exhausted,
    MissingCursor,
    InvalidCursor,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjection
                projection,
            int nextEventIndex)
    {
        Projection = projection;
        NextEventIndex = nextEventIndex;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjection
            Projection
    { get; }

    public int NextEventIndex { get; }

    public int RemainingCount => Projection.EventCount - NextEventIndex;

    public bool IsExhausted => NextEventIndex == Projection.EventCount;
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorFactory
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorCreationResult
            TryCreate(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjection?
                    projection,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor?
                    cursor)
    {
        cursor = null;
        if (projection is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorCreationResult
                    .MissingProjection;
        }

        if (projection.EventCount >
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjector
                .MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorCreationResult
                    .EventLimitExceeded;
        }

        cursor = new(projection, 0);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorCreationResult
                .Created;
    }

    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorAdvanceResult
            TryAdvance(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor?
                    cursor,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEvent?
                    packetEvent,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor?
                    successorCursor)
    {
        packetEvent = null;
        successorCursor = null;
        if (cursor is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorAdvanceResult
                    .MissingCursor;
        }

        if (cursor.NextEventIndex < 0 ||
            cursor.NextEventIndex > cursor.Projection.EventCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorAdvanceResult
                    .InvalidCursor;
        }

        if (cursor.IsExhausted)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorAdvanceResult
                    .Exhausted;
        }

        packetEvent = cursor.Projection.Events[cursor.NextEventIndex];
        successorCursor = new(cursor.Projection, cursor.NextEventIndex + 1);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorAdvanceResult
                .Advanced;
    }
}
