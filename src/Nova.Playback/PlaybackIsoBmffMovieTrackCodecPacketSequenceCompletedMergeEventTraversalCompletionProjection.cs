namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionProjectionResult
{
    Projected,
    MissingTraversalProjection,
    SourceMismatch,
    SourceNotExhausted,
    ProjectionMismatch,
    InvalidCursorState,
    EventCountMismatch,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionProjection
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionProjection(
            IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjectionSource
                source,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletion
                completion)
    {
        Source = source;
        Completion = completion;
    }

    public
        IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjectionSource
            Source
    { get; }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletion
            Completion
    { get; }
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionProjectionResult
            Project(
                IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjectionSource?
                    source,
                out
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionProjectionResult
                    .MissingTraversalProjection;
        }

        if (!ReferenceEquals(
                source.TraversalSource.InitialCursor,
                source.CursorSource.Cursor))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionProjectionResult
                    .SourceMismatch;
        }

        var result =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionFactory
                .TryCreate(source.TraversalSource, out var completion);
        if (result !=
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionResult
                    .Certified ||
            completion is null)
        {
            return result switch
            {
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionResult
                    .SourceNotExhausted =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionProjectionResult
                        .SourceNotExhausted,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionResult
                    .ProjectionMismatch =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionProjectionResult
                        .ProjectionMismatch,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionResult
                    .InvalidCursorState =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionProjectionResult
                        .InvalidCursorState,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionResult
                    .EventCountMismatch =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionProjectionResult
                        .EventCountMismatch,
                _ =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionProjectionResult
                        .SourceMismatch,
            };
        }

        if (!ReferenceEquals(completion.Source, source.TraversalSource) ||
            !ReferenceEquals(
                completion.InitialCursor,
                source.CursorSource.Cursor))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionProjectionResult
                    .SourceMismatch;
        }

        projection = new(source, completion);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionProjectionResult
                .Projected;
    }
}
