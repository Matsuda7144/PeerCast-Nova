namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionProjectionResult
{
    Projected,
    MissingTraversalProjection,
    SourceMismatch,
    SourceNotExhausted,
    InvalidTieBreak,
    CursorSourceMismatch,
    InvalidCursorState,
    StepCountMismatch,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionProjection
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionProjection(
            IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjectionSource
                source,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletion
                completion)
    {
        Source = source;
        Completion = completion;
    }

    public
        IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjectionSource
            Source
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletion
        Completion
    { get; }
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionProjectionResult
            Project(
                IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjectionSource?
                    source,
                out
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionProjectionResult
                    .MissingTraversalProjection;
        }

        if (!ReferenceEquals(
                source.TraversalSource.InitialCursorSet,
                source.CursorSource.CursorSet))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionProjectionResult
                    .SourceMismatch;
        }

        var result =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionFactory
                .TryCreate(source.TraversalSource, out var completion);
        if (result !=
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionResult
                    .Certified ||
            completion is null)
        {
            return result switch
            {
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionResult
                    .SourceNotExhausted =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionProjectionResult
                        .SourceNotExhausted,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionResult
                    .InvalidTieBreak =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionProjectionResult
                        .InvalidTieBreak,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionResult
                    .CursorSourceMismatch =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionProjectionResult
                        .CursorSourceMismatch,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionResult
                    .InvalidCursorState =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionProjectionResult
                        .InvalidCursorState,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionResult
                    .StepCountMismatch =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionProjectionResult
                        .StepCountMismatch,
                _ =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionProjectionResult
                        .SourceMismatch,
            };
        }

        if (!ReferenceEquals(completion.Source, source.TraversalSource) ||
            !ReferenceEquals(
                completion.InitialCursorSet,
                source.CursorSource.CursorSet))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionProjectionResult
                    .SourceMismatch;
        }

        projection = new(source, completion);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionProjectionResult
                .Projected;
    }
}
