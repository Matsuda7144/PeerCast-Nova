namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationProjectionResult
{
    Projected,
    MissingSourceProjection,
    SourceMismatch,
    SourceExhausted,
    InvalidSource,
    InvalidAdditionalEventLimit,
    TraversalFailed,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationProjection
    : IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjectionSource
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationProjection(
            IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjectionSource
                source,
            int additionalEventLimit,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuation
                continuation)
    {
        Source = source;
        AdditionalEventLimit = additionalEventLimit;
        Continuation = continuation;
    }

    public
        IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjectionSource
            Source
    { get; }

    public int AdditionalEventLimit { get; }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuation
            Continuation
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjection
        CursorSource => Source.CursorSource;

    public IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalSource
        TraversalSource => Continuation;
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationProjectionResult
            Project(
                IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjectionSource?
                    source,
                int additionalEventLimit,
                out
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationProjectionResult
                    .MissingSourceProjection;
        }

        if (!ReferenceEquals(
                source.TraversalSource.InitialCursor,
                source.CursorSource.Cursor))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationProjectionResult
                    .SourceMismatch;
        }

        var result =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationFactory
                .TryContinue(
                    source.TraversalSource,
                    additionalEventLimit,
                    out var continuation);
        if (result !=
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationResult
                    .Continued ||
            continuation is null)
        {
            return result switch
            {
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationResult
                    .SourceExhausted =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationProjectionResult
                        .SourceExhausted,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationResult
                    .InvalidSource =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationProjectionResult
                        .InvalidSource,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationResult
                    .InvalidAdditionalEventLimit =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationProjectionResult
                        .InvalidAdditionalEventLimit,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationResult
                    .TraversalFailed =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationProjectionResult
                        .TraversalFailed,
                _ =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationProjectionResult
                        .SourceMismatch,
            };
        }

        if (!ReferenceEquals(
                continuation.Source,
                source.TraversalSource) ||
            !ReferenceEquals(
                continuation.InitialCursor,
                source.CursorSource.Cursor) ||
            continuation.Segment.MaximumEventCount != additionalEventLimit)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationProjectionResult
                    .SourceMismatch;
        }

        projection = new(source, additionalEventLimit, continuation);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationProjectionResult
                .Projected;
    }
}
