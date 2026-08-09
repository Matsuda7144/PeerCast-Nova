namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffMovieCodecPacketEventQueueAcknowledgementResult
{
    Acknowledged,
    MissingSourceCursor,
    MissingEntry,
    MissingSuccessorCursor,
    SnapshotMismatch,
    SequenceMismatch,
    InvalidCursorTransition,
}

public sealed class PlaybackIsoBmffMovieCodecPacketEventQueueAcknowledgement
{
    internal PlaybackIsoBmffMovieCodecPacketEventQueueAcknowledgement(
        PlaybackIsoBmffMovieCodecPacketEventQueueCursor sourceCursor,
        PlaybackIsoBmffMovieCodecPacketEventQueueEntry entry,
        PlaybackIsoBmffMovieCodecPacketEventQueueCursor successorCursor)
    {
        SourceCursor = sourceCursor;
        Entry = entry;
        SuccessorCursor = successorCursor;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueCursor SourceCursor
    {
        get;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueEntry Entry { get; }

    public PlaybackIsoBmffMovieCodecPacketEventQueueCursor SuccessorCursor
    {
        get;
    }

    public int SequenceIndex => Entry.SequenceIndex;
}

public static class
    PlaybackIsoBmffMovieCodecPacketEventQueueAcknowledgementFactory
{
    public static PlaybackIsoBmffMovieCodecPacketEventQueueAcknowledgementResult
        TryCreate(
            PlaybackIsoBmffMovieCodecPacketEventQueueCursor? sourceCursor,
            PlaybackIsoBmffMovieCodecPacketEventQueueEntry? entry,
            PlaybackIsoBmffMovieCodecPacketEventQueueCursor? successorCursor,
            out PlaybackIsoBmffMovieCodecPacketEventQueueAcknowledgement?
                acknowledgement)
    {
        acknowledgement = null;
        if (sourceCursor is null)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueAcknowledgementResult
                    .MissingSourceCursor;
        }

        if (entry is null)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueAcknowledgementResult
                    .MissingEntry;
        }

        if (successorCursor is null)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueAcknowledgementResult
                    .MissingSuccessorCursor;
        }

        var snapshot = sourceCursor.Snapshot;
        if (!ReferenceEquals(snapshot, successorCursor.Snapshot))
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueAcknowledgementResult
                    .SnapshotMismatch;
        }

        if ((uint)sourceCursor.NextSequenceIndex >= (uint)snapshot.Count ||
            !ReferenceEquals(
                snapshot.Entries[sourceCursor.NextSequenceIndex],
                entry) ||
            entry.SequenceIndex != sourceCursor.NextSequenceIndex)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueAcknowledgementResult
                    .SequenceMismatch;
        }

        if (successorCursor.NextSequenceIndex !=
            sourceCursor.NextSequenceIndex + 1)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueAcknowledgementResult
                    .InvalidCursorTransition;
        }

        acknowledgement = new(sourceCursor, entry, successorCursor);
        return PlaybackIsoBmffMovieCodecPacketEventQueueAcknowledgementResult
            .Acknowledged;
    }
}
