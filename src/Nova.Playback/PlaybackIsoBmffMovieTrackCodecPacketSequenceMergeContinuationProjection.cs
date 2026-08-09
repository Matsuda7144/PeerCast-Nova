namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeContinuationProjectionResult
{
    Projected,
    MissingSourceProjection,
    SourceMismatch,
    SourceExhausted,
    InvalidSource,
    InvalidAdditionalStepLimit,
    TraversalFailed,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeContinuationProjection
    : IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjectionSource
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeContinuationProjection(
            IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjectionSource
                source,
            int additionalStepLimit,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuation
                continuation)
    {
        Source = source;
        AdditionalStepLimit = additionalStepLimit;
        Continuation = continuation;
    }

    public
        IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjectionSource
            Source
    { get; }

    public int AdditionalStepLimit { get; }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuation
            Continuation
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjection
        CursorSource => Source.CursorSource;

    public IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalSource
        TraversalSource => Continuation;
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeContinuationProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeContinuationProjectionResult
            Project(
                IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjectionSource?
                    source,
                int additionalStepLimit,
                out
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeContinuationProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeContinuationProjectionResult
                    .MissingSourceProjection;
        }

        if (!ReferenceEquals(
                source.TraversalSource.InitialCursorSet,
                source.CursorSource.CursorSet))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeContinuationProjectionResult
                    .SourceMismatch;
        }

        var result =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuationFactory
                .TryContinue(
                    source.TraversalSource,
                    additionalStepLimit,
                    out var continuation);
        if (result !=
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuationResult
                    .Continued ||
            continuation is null)
        {
            return result switch
            {
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuationResult
                    .SourceExhausted =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeContinuationProjectionResult
                        .SourceExhausted,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuationResult
                    .InvalidSource =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeContinuationProjectionResult
                        .InvalidSource,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuationResult
                    .InvalidAdditionalStepLimit =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeContinuationProjectionResult
                        .InvalidAdditionalStepLimit,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuationResult
                    .TraversalFailed =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeContinuationProjectionResult
                        .TraversalFailed,
                _ =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeContinuationProjectionResult
                        .SourceMismatch,
            };
        }

        if (!ReferenceEquals(
                continuation.Source,
                source.TraversalSource) ||
            !ReferenceEquals(
                continuation.InitialCursorSet,
                source.CursorSource.CursorSet) ||
            continuation.Segment.MaximumStepCount != additionalStepLimit)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeContinuationProjectionResult
                    .SourceMismatch;
        }

        projection = new(source, additionalStepLimit, continuation);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeContinuationProjectionResult
                .Projected;
    }
}
