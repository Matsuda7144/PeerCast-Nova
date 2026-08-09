namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjectionResult
{
    Projected,
    MissingEventProjection,
    SourceMismatch,
    EventLimitExceeded,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjection
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjection(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventSourceProjection
                source,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor
                cursor)
    {
        Source = source;
        Cursor = cursor;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventSourceProjection
            Source
    { get; }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor
            Cursor
    { get; }
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjectionResult
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventSourceProjection?
                    source,
                out
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjectionResult
                    .MissingEventProjection;
        }

        if (!ReferenceEquals(
                source.EventProjection.StepView,
                source.Source.StepView))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjectionResult
                    .SourceMismatch;
        }

        var result =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorFactory
                .TryCreate(source.EventProjection, out var cursor);
        if (result !=
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorCreationResult
                    .Created ||
            cursor is null)
        {
            return result ==
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorCreationResult
                    .EventLimitExceeded
                ? PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjectionResult
                    .EventLimitExceeded
                : PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjectionResult
                    .SourceMismatch;
        }

        if (!ReferenceEquals(cursor.Projection, source.EventProjection) ||
            cursor.NextEventIndex != 0)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjectionResult
                    .SourceMismatch;
        }

        projection = new(source, cursor);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjectionResult
                .Projected;
    }
}
