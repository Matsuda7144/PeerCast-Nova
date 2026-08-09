namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjectionResult
{
    Projected,
    MissingCursorProjection,
    SourceMismatch,
    InvalidEventLimit,
    InvalidCursor,
    AdvanceFailed,
    AcknowledgementFailed,
}

public interface
    IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjectionSource
{
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjection
        CursorSource
    { get; }

    IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalSource
        TraversalSource
    { get; }
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjection
    : IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjectionSource
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjection(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjection
                source,
            int maximumEventCount,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversal
                traversal)
    {
        Source = source;
        MaximumEventCount = maximumEventCount;
        Traversal = traversal;
    }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjection
        Source
    { get; }

    public int MaximumEventCount { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversal
        Traversal
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjection
        CursorSource => Source;

    public IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalSource
        TraversalSource => Traversal;
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjectionResult
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorProjection?
                    source,
                int maximumEventCount,
                out
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjectionResult
                    .MissingCursorProjection;
        }

        if (!ReferenceEquals(
                source.Cursor.Projection,
                source.Source.EventProjection) ||
            source.Cursor.NextEventIndex != 0)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjectionResult
                    .SourceMismatch;
        }

        var result =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraverser
                .TryTraverse(
                    source.Cursor,
                    maximumEventCount,
                    out var traversal);
        if (result !=
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalResult
                    .Traversed ||
            traversal is null)
        {
            return result switch
            {
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalResult
                    .InvalidEventLimit =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjectionResult
                        .InvalidEventLimit,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalResult
                    .InvalidCursor =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjectionResult
                        .InvalidCursor,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalResult
                    .AdvanceFailed =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjectionResult
                        .AdvanceFailed,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalResult
                    .AcknowledgementFailed =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjectionResult
                        .AcknowledgementFailed,
                _ =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjectionResult
                        .SourceMismatch,
            };
        }

        if (!ReferenceEquals(traversal.InitialCursor, source.Cursor) ||
            traversal.MaximumEventCount != maximumEventCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjectionResult
                    .SourceMismatch;
        }

        projection = new(source, maximumEventCount, traversal);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalProjectionResult
                .Projected;
    }
}
