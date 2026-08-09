namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetCreationResult
{
    Created,
    MissingSelection,
    NoSelectedSequences,
    VideoCursorCreationFailed,
    AudioCursorCreationFailed,
}

public sealed class PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet
{
    internal PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet(
        PlaybackIsoBmffMovieTrackCodecPacketSequenceSelection selection,
        PlaybackIsoBmffTrackCodecPacketSequenceCursor? videoCursor,
        PlaybackIsoBmffTrackCodecPacketSequenceCursor? audioCursor)
    {
        Selection = selection;
        VideoCursor = videoCursor;
        AudioCursor = audioCursor;
    }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceSelection Selection
    { get; }

    public PlaybackIsoBmffTrackCodecPacketSequenceCursor? VideoCursor
    { get; }

    public PlaybackIsoBmffTrackCodecPacketSequenceCursor? AudioCursor
    { get; }

    public bool HasVideo => VideoCursor is not null;

    public bool HasAudio => AudioCursor is not null;
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetFactory
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetCreationResult
            TryCreate(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSelection?
                    selection,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet?
                    cursorSet)
    {
        cursorSet = null;
        if (selection is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetCreationResult
                    .MissingSelection;
        }

        if (!selection.HasVideo && !selection.HasAudio)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetCreationResult
                    .NoSelectedSequences;
        }

        PlaybackIsoBmffTrackCodecPacketSequenceCursor? videoCursor = null;
        if (selection.VideoStream is { } videoStream &&
            PlaybackIsoBmffTrackCodecPacketSequenceCursorFactory.TryCreate(
                videoStream.Sequence,
                out videoCursor) !=
            PlaybackIsoBmffTrackCodecPacketSequenceCursorCreationResult
                .Created)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetCreationResult
                    .VideoCursorCreationFailed;
        }

        PlaybackIsoBmffTrackCodecPacketSequenceCursor? audioCursor = null;
        if (selection.AudioStream is { } audioStream &&
            PlaybackIsoBmffTrackCodecPacketSequenceCursorFactory.TryCreate(
                audioStream.Sequence,
                out audioCursor) !=
            PlaybackIsoBmffTrackCodecPacketSequenceCursorCreationResult
                .Created)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetCreationResult
                    .AudioCursorCreationFailed;
        }

        cursorSet = new(selection, videoCursor, audioCursor);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetCreationResult
                .Created;
    }
}
