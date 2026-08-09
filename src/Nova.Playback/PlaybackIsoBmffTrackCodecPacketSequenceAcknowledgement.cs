namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffTrackCodecPacketSequenceAcknowledgementResult
{
    Acknowledged,
    MissingSourceCursor,
    MissingEntry,
    MissingSuccessorCursor,
    SequenceMismatch,
    EntryMismatch,
    InvalidCursorTransition,
}

public sealed class PlaybackIsoBmffTrackCodecPacketSequenceAcknowledgement
{
    internal PlaybackIsoBmffTrackCodecPacketSequenceAcknowledgement(
        PlaybackIsoBmffTrackCodecPacketSequenceCursor sourceCursor,
        PlaybackIsoBmffTrackCodecPacketSequenceEntry entry,
        PlaybackIsoBmffTrackCodecPacketSequenceCursor successorCursor)
    {
        SourceCursor = sourceCursor;
        Entry = entry;
        SuccessorCursor = successorCursor;
    }

    public PlaybackIsoBmffTrackCodecPacketSequenceCursor SourceCursor
    { get; }

    public PlaybackIsoBmffTrackCodecPacketSequenceEntry Entry { get; }

    public PlaybackIsoBmffTrackCodecPacketSequenceCursor SuccessorCursor
    { get; }

    public int SequenceIndex => Entry.SequenceIndex;

    public ulong SampleNumber => Entry.SampleNumber;
}

public static class
    PlaybackIsoBmffTrackCodecPacketSequenceAcknowledgementFactory
{
    public static PlaybackIsoBmffTrackCodecPacketSequenceAcknowledgementResult
        TryCreate(
            PlaybackIsoBmffTrackCodecPacketSequenceCursor? sourceCursor,
            PlaybackIsoBmffTrackCodecPacketSequenceEntry? entry,
            PlaybackIsoBmffTrackCodecPacketSequenceCursor? successorCursor,
            out PlaybackIsoBmffTrackCodecPacketSequenceAcknowledgement?
                acknowledgement)
    {
        acknowledgement = null;
        if (sourceCursor is null)
        {
            return
                PlaybackIsoBmffTrackCodecPacketSequenceAcknowledgementResult
                    .MissingSourceCursor;
        }

        if (entry is null)
        {
            return
                PlaybackIsoBmffTrackCodecPacketSequenceAcknowledgementResult
                    .MissingEntry;
        }

        if (successorCursor is null)
        {
            return
                PlaybackIsoBmffTrackCodecPacketSequenceAcknowledgementResult
                    .MissingSuccessorCursor;
        }

        var sequence = sourceCursor.Sequence;
        if (!ReferenceEquals(sequence, successorCursor.Sequence))
        {
            return
                PlaybackIsoBmffTrackCodecPacketSequenceAcknowledgementResult
                    .SequenceMismatch;
        }

        if ((uint)sourceCursor.NextSequenceIndex >=
                (uint)sequence.Entries.Count ||
            !ReferenceEquals(
                sequence.Entries[sourceCursor.NextSequenceIndex],
                entry) ||
            entry.SequenceIndex != sourceCursor.NextSequenceIndex)
        {
            return
                PlaybackIsoBmffTrackCodecPacketSequenceAcknowledgementResult
                    .EntryMismatch;
        }

        if (successorCursor.NextSequenceIndex !=
            sourceCursor.NextSequenceIndex + 1)
        {
            return
                PlaybackIsoBmffTrackCodecPacketSequenceAcknowledgementResult
                    .InvalidCursorTransition;
        }

        acknowledgement = new(sourceCursor, entry, successorCursor);
        return PlaybackIsoBmffTrackCodecPacketSequenceAcknowledgementResult
            .Acknowledged;
    }
}
