namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjectionResult
{
    Projected,
    MissingCursorSetProjection,
    SourceMismatch,
    InvalidTieBreak,
    InvalidStepLimit,
    InvalidCursorState,
    StepAdvanceFailed,
    StepProjectionFailed,
    StepSequenceValidationFailed,
}

public interface
    IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjectionSource
{
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjection CursorSource
    { get; }

    IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalSource
        TraversalSource
    { get; }
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjection
    : IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjectionSource
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjection(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjection
                source,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak
                tieBreak,
            int maximumStepCount,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversal
                traversal)
    {
        Source = source;
        TieBreak = tieBreak;
        MaximumStepCount = maximumStepCount;
        Traversal = traversal;
    }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjection
        Source
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak
        TieBreak
    { get; }

    public int MaximumStepCount { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversal Traversal
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjection
        CursorSource => Source;

    public IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalSource
        TraversalSource => Traversal;
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjectionResult
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjection?
                    source,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak
                    tieBreak,
                int maximumStepCount,
                out
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjectionResult
                    .MissingCursorSetProjection;
        }

        if (!ReferenceEquals(
                source.CursorSet.Selection,
                source.Source.Selection))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjectionResult
                    .SourceMismatch;
        }

        var result = PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraverser
            .TryTraverse(
                source.CursorSet,
                tieBreak,
                maximumStepCount,
                out var traversal);
        if (result !=
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalResult
                    .Traversed ||
            traversal is null)
        {
            return result switch
            {
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalResult
                    .InvalidTieBreak =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjectionResult
                        .InvalidTieBreak,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalResult
                    .InvalidStepLimit =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjectionResult
                        .InvalidStepLimit,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalResult
                    .InvalidCursorState =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjectionResult
                        .InvalidCursorState,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalResult
                    .StepAdvanceFailed =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjectionResult
                        .StepAdvanceFailed,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalResult
                    .StepProjectionFailed =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjectionResult
                        .StepProjectionFailed,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalResult
                    .StepSequenceValidationFailed =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjectionResult
                        .StepSequenceValidationFailed,
                _ =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjectionResult
                        .SourceMismatch,
            };
        }

        if (!ReferenceEquals(traversal.InitialCursorSet, source.CursorSet) ||
            traversal.TieBreak != tieBreak ||
            traversal.MaximumStepCount != maximumStepCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjectionResult
                    .SourceMismatch;
        }

        projection = new(source, tieBreak, maximumStepCount, traversal);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjectionResult
                .Projected;
    }
}
