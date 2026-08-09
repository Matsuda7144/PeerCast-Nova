namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffMovieCodecPacketEventQueueCursorCreationResult
{
    Created,
    MissingSnapshot,
    EventLimitExceeded,
    InvalidSequence,
}

public enum PlaybackIsoBmffMovieCodecPacketEventQueueCursorAdvanceResult
{
    Advanced,
    Exhausted,
    MissingCursor,
    InvalidCursor,
}

public sealed class PlaybackIsoBmffMovieCodecPacketEventQueueCursor
{
    internal PlaybackIsoBmffMovieCodecPacketEventQueueCursor(
        PlaybackIsoBmffMovieCodecPacketEventQueueSnapshot snapshot,
        int nextSequenceIndex)
    {
        Snapshot = snapshot;
        NextSequenceIndex = nextSequenceIndex;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueSnapshot Snapshot { get; }

    public int NextSequenceIndex { get; }

    public int RemainingCount => Snapshot.Count - NextSequenceIndex;

    public bool IsExhausted => NextSequenceIndex == Snapshot.Count;
}

public static class PlaybackIsoBmffMovieCodecPacketEventQueueCursorFactory
{
    public static
        PlaybackIsoBmffMovieCodecPacketEventQueueCursorCreationResult TryCreate(
            PlaybackIsoBmffMovieCodecPacketEventQueueSnapshot? snapshot,
            out PlaybackIsoBmffMovieCodecPacketEventQueueCursor? cursor)
    {
        cursor = null;
        if (snapshot is null)
        {
            return PlaybackIsoBmffMovieCodecPacketEventQueueCursorCreationResult
                .MissingSnapshot;
        }

        if (snapshot.Count >
            PlaybackIsoBmffMovieCodecPacketEventProjector.MaximumEventCount)
        {
            return PlaybackIsoBmffMovieCodecPacketEventQueueCursorCreationResult
                .EventLimitExceeded;
        }

        for (var index = 0; index < snapshot.Count; index++)
        {
            if (snapshot.Entries[index].SequenceIndex != index)
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueCursorCreationResult
                        .InvalidSequence;
            }
        }

        cursor = new(snapshot, 0);
        return PlaybackIsoBmffMovieCodecPacketEventQueueCursorCreationResult
            .Created;
    }

    public static PlaybackIsoBmffMovieCodecPacketEventQueueCursorAdvanceResult
        TryAdvance(
            PlaybackIsoBmffMovieCodecPacketEventQueueCursor? cursor,
            out PlaybackIsoBmffMovieCodecPacketEventQueueEntry? entry,
            out PlaybackIsoBmffMovieCodecPacketEventQueueCursor? nextCursor)
    {
        entry = null;
        nextCursor = null;
        if (cursor is null)
        {
            return PlaybackIsoBmffMovieCodecPacketEventQueueCursorAdvanceResult
                .MissingCursor;
        }

        if (cursor.NextSequenceIndex < 0 ||
            cursor.NextSequenceIndex > cursor.Snapshot.Count)
        {
            return PlaybackIsoBmffMovieCodecPacketEventQueueCursorAdvanceResult
                .InvalidCursor;
        }

        if (cursor.IsExhausted)
        {
            return PlaybackIsoBmffMovieCodecPacketEventQueueCursorAdvanceResult
                .Exhausted;
        }

        entry = cursor.Snapshot.Entries[cursor.NextSequenceIndex];
        if (entry.SequenceIndex != cursor.NextSequenceIndex)
        {
            entry = null;
            return PlaybackIsoBmffMovieCodecPacketEventQueueCursorAdvanceResult
                .InvalidCursor;
        }

        nextCursor = new(
            cursor.Snapshot,
            cursor.NextSequenceIndex + 1);
        return PlaybackIsoBmffMovieCodecPacketEventQueueCursorAdvanceResult
            .Advanced;
    }
}
