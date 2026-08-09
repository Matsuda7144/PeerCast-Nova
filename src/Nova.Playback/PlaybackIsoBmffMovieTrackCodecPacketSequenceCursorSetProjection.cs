namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjectionResult
{
    Projected,
    MissingSelectionProjection,
    SourceMismatch,
    NoSelectedSequences,
    VideoCursorCreationFailed,
    AudioCursorCreationFailed,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjection
{
    internal PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjection(
        PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjection source,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet cursorSet)
    {
        Source = source;
        CursorSet = cursorSet;
    }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjection
        Source
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet CursorSet
    { get; }
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjectionResult
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjection?
                    source,
                out
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjectionResult
                    .MissingSelectionProjection;
        }

        if (!ReferenceEquals(
                source.Selection.SequenceSet,
                source.Source.SequenceSet))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjectionResult
                    .SourceMismatch;
        }

        var result =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetFactory
                .TryCreate(source.Selection, out var cursorSet);
        if (result !=
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetCreationResult
                    .Created ||
            cursorSet is null)
        {
            return result switch
            {
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetCreationResult
                    .NoSelectedSequences =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjectionResult
                        .NoSelectedSequences,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetCreationResult
                    .VideoCursorCreationFailed =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjectionResult
                        .VideoCursorCreationFailed,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetCreationResult
                    .AudioCursorCreationFailed =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjectionResult
                        .AudioCursorCreationFailed,
                _ =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjectionResult
                        .SourceMismatch,
            };
        }

        projection = new(source, cursorSet);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjectionResult
                .Projected;
    }
}
