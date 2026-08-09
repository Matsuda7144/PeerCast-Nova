namespace PeerCast.Nova.Playback;

public interface
    IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalSource
{
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor
        InitialCursor
    { get; }

    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor
        FinalCursor
    { get; }

    int TotalEventCount { get; }

    bool IsExhausted { get; }
}

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationResult
{
    Continued,
    MissingSource,
    SourceExhausted,
    InvalidSource,
    InvalidAdditionalEventLimit,
    TraversalFailed,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuation :
    IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalSource
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuation(
            IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalSource
                source,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversal
                segment)
    {
        Source = source;
        Segment = segment;
    }

    public
        IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalSource
            Source
    { get; }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversal
            Segment
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor
        InitialCursor => Source.InitialCursor;

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor
        FinalCursor => Segment.FinalCursor;

    public int StartingEventIndex => Source.TotalEventCount;

    public int SegmentEventCount => Segment.EventCount;

    public int TotalEventCount => StartingEventIndex + SegmentEventCount;

    public bool IsExhausted => Segment.IsExhausted;

    public bool IsTruncated => Segment.IsTruncated;
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationFactory
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationResult
            TryContinue(
                IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalSource?
                    source,
                int additionalEventLimit,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuation?
                    continuation)
    {
        continuation = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationResult
                    .MissingSource;
        }

        if (source.IsExhausted)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationResult
                    .SourceExhausted;
        }

        var initial = source.InitialCursor;
        var final = source.FinalCursor;
        if (!ReferenceEquals(initial.Projection, final.Projection) ||
            source.TotalEventCount < 0 ||
            source.TotalEventCount >=
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraverser
                    .MaximumEventCount ||
            initial.NextEventIndex < 0 ||
            final.NextEventIndex !=
                initial.NextEventIndex + source.TotalEventCount ||
            final.NextEventIndex > final.Projection.EventCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationResult
                    .InvalidSource;
        }

        var remainingLimit =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraverser
                .MaximumEventCount - source.TotalEventCount;
        if (additionalEventLimit <= 0 ||
            additionalEventLimit > remainingLimit)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationResult
                    .InvalidAdditionalEventLimit;
        }

        if (PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraverser
                .TryTraverse(
                    final,
                    additionalEventLimit,
                    out var segment) !=
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalResult
                .Traversed ||
            segment is null || segment.EventCount == 0 ||
            !ReferenceEquals(segment.InitialCursor, final))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationResult
                    .TraversalFailed;
        }

        continuation = new(source, segment);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuationResult
                .Continued;
    }
}
