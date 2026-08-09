namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffTrackCodecPacketSequenceCursorCreationResult
{
    Created,
    MissingSequence,
    PacketLimitExceeded,
    InvalidSequence,
}

public enum PlaybackIsoBmffTrackCodecPacketSequenceCursorAdvanceResult
{
    Advanced,
    Exhausted,
    MissingCursor,
    InvalidCursor,
}

public sealed class PlaybackIsoBmffTrackCodecPacketSequenceCursor
{
    internal PlaybackIsoBmffTrackCodecPacketSequenceCursor(
        PlaybackIsoBmffTrackCodecPacketSequence sequence,
        int nextSequenceIndex)
    {
        Sequence = sequence;
        NextSequenceIndex = nextSequenceIndex;
    }

    public PlaybackIsoBmffTrackCodecPacketSequence Sequence { get; }

    public int NextSequenceIndex { get; }

    public int RemainingCount => Sequence.Entries.Count - NextSequenceIndex;

    public bool IsExhausted => NextSequenceIndex == Sequence.Entries.Count;
}

public static class PlaybackIsoBmffTrackCodecPacketSequenceCursorFactory
{
    public static PlaybackIsoBmffTrackCodecPacketSequenceCursorCreationResult
        TryCreate(
            PlaybackIsoBmffTrackCodecPacketSequence? sequence,
            out PlaybackIsoBmffTrackCodecPacketSequenceCursor? cursor)
    {
        cursor = null;
        if (sequence is null)
        {
            return PlaybackIsoBmffTrackCodecPacketSequenceCursorCreationResult
                .MissingSequence;
        }

        if (sequence.Entries.Count >
            PlaybackIsoBmffTrackCodecPacketSequenceFactory.MaximumPacketCount)
        {
            return PlaybackIsoBmffTrackCodecPacketSequenceCursorCreationResult
                .PacketLimitExceeded;
        }

        ulong priorSampleNumber = 0;
        for (var index = 0; index < sequence.Entries.Count; index++)
        {
            var entry = sequence.Entries[index];
            if (entry.SequenceIndex != index || entry.SampleNumber == 0 ||
                index > 0 && entry.SampleNumber <= priorSampleNumber)
            {
                return
                    PlaybackIsoBmffTrackCodecPacketSequenceCursorCreationResult
                        .InvalidSequence;
            }

            priorSampleNumber = entry.SampleNumber;
        }

        cursor = new(sequence, 0);
        return PlaybackIsoBmffTrackCodecPacketSequenceCursorCreationResult
            .Created;
    }

    public static PlaybackIsoBmffTrackCodecPacketSequenceCursorAdvanceResult
        TryAdvance(
            PlaybackIsoBmffTrackCodecPacketSequenceCursor? cursor,
            out PlaybackIsoBmffTrackCodecPacketSequenceEntry? entry,
            out PlaybackIsoBmffTrackCodecPacketSequenceCursor? nextCursor)
    {
        entry = null;
        nextCursor = null;
        if (cursor is null)
        {
            return PlaybackIsoBmffTrackCodecPacketSequenceCursorAdvanceResult
                .MissingCursor;
        }

        if (cursor.NextSequenceIndex < 0 ||
            cursor.NextSequenceIndex > cursor.Sequence.Entries.Count)
        {
            return PlaybackIsoBmffTrackCodecPacketSequenceCursorAdvanceResult
                .InvalidCursor;
        }

        if (cursor.IsExhausted)
        {
            return PlaybackIsoBmffTrackCodecPacketSequenceCursorAdvanceResult
                .Exhausted;
        }

        entry = cursor.Sequence.Entries[cursor.NextSequenceIndex];
        if (entry.SequenceIndex != cursor.NextSequenceIndex)
        {
            entry = null;
            return PlaybackIsoBmffTrackCodecPacketSequenceCursorAdvanceResult
                .InvalidCursor;
        }

        nextCursor = new(cursor.Sequence, cursor.NextSequenceIndex + 1);
        return PlaybackIsoBmffTrackCodecPacketSequenceCursorAdvanceResult
            .Advanced;
    }
}
