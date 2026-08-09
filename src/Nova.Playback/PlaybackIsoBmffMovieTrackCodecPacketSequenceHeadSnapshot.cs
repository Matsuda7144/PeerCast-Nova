namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshotProjectionResult
{
    Projected,
    MissingCursorSet,
    NoSelectedCursors,
    InvalidVideoCursor,
    InvalidAudioCursor,
}

public sealed class PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshot
{
    internal PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshot(
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet cursorSet,
        PlaybackIsoBmffTrackCodecPacketSequenceEntry? videoHead,
        PlaybackIsoBmffTrackCodecPacketSequenceEntry? audioHead)
    {
        CursorSet = cursorSet;
        VideoHead = videoHead;
        AudioHead = audioHead;
    }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet CursorSet
    { get; }

    public PlaybackIsoBmffTrackCodecPacketSequenceEntry? VideoHead { get; }

    public PlaybackIsoBmffTrackCodecPacketSequenceEntry? AudioHead { get; }

    public bool HasVideoHead => VideoHead is not null;

    public bool HasAudioHead => AudioHead is not null;

    public bool IsExhausted => !HasVideoHead && !HasAudioHead;
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshotProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshotProjectionResult
            TryProject(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet?
                    cursorSet,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshot?
                    snapshot)
    {
        snapshot = null;
        if (cursorSet is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshotProjectionResult
                    .MissingCursorSet;
        }

        if (!cursorSet.Selection.HasVideo &&
            !cursorSet.Selection.HasAudio)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshotProjectionResult
                    .NoSelectedCursors;
        }

        if (!TryGetHead(
                cursorSet.Selection.VideoStream,
                cursorSet.VideoCursor,
                out var videoHead))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshotProjectionResult
                    .InvalidVideoCursor;
        }

        if (!TryGetHead(
                cursorSet.Selection.AudioStream,
                cursorSet.AudioCursor,
                out var audioHead))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshotProjectionResult
                    .InvalidAudioCursor;
        }

        snapshot = new(cursorSet, videoHead, audioHead);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshotProjectionResult
                .Projected;
    }

    private static bool TryGetHead(
        PlaybackIsoBmffIndexedTrackCodecPacketSequence? stream,
        PlaybackIsoBmffTrackCodecPacketSequenceCursor? cursor,
        out PlaybackIsoBmffTrackCodecPacketSequenceEntry? head)
    {
        head = null;
        if (stream is null)
        {
            return cursor is null;
        }

        if (cursor is null ||
            !ReferenceEquals(cursor.Sequence, stream.Sequence) ||
            cursor.NextSequenceIndex < 0 ||
            cursor.NextSequenceIndex > cursor.Sequence.Entries.Count)
        {
            return false;
        }

        if (cursor.IsExhausted)
        {
            return true;
        }

        var candidate = cursor.Sequence.Entries[cursor.NextSequenceIndex];
        if (candidate.SequenceIndex != cursor.NextSequenceIndex)
        {
            return false;
        }

        head = candidate;
        return true;
    }
}
