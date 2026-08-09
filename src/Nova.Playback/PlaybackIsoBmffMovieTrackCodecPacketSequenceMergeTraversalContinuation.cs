namespace PeerCast.Nova.Playback;

public interface
    IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalSource
{
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet InitialCursorSet
    { get; }

    PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet FinalCursorSet
    { get; }

    PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak TieBreak
    { get; }

    int TotalStepCount { get; }

    bool IsExhausted { get; }
}

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuationResult
{
    Continued,
    MissingSource,
    SourceExhausted,
    InvalidSource,
    InvalidAdditionalStepLimit,
    TraversalFailed,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuation :
    IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalSource
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuation(
            IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalSource
                source,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversal segment)
    {
        Source = source;
        Segment = segment;
    }

    public IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalSource
        Source
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversal Segment
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet
        InitialCursorSet => Source.InitialCursorSet;

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet FinalCursorSet
        => Segment.FinalCursorSet;

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak
        TieBreak => Source.TieBreak;

    public int StartingMergeIndex => Source.TotalStepCount;

    public int SegmentStepCount => Segment.StepCount;

    public int TotalStepCount => StartingMergeIndex + SegmentStepCount;

    public IReadOnlyList<
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStep> Steps =>
        Segment.Steps;

    public bool IsExhausted => Segment.IsExhausted;

    public bool IsTruncated => Segment.IsTruncated;
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuationFactory
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuationResult
            TryContinue(
                IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalSource?
                    source,
                int additionalStepLimit,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuation?
                    continuation)
    {
        continuation = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuationResult
                    .MissingSource;
        }

        if (source.IsExhausted)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuationResult
                    .SourceExhausted;
        }

        if (!Enum.IsDefined(source.TieBreak) || source.TotalStepCount < 0 ||
            source.TotalStepCount >=
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraverser
                    .MaximumStepCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuationResult
                    .InvalidSource;
        }

        var remainingLimit =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraverser
                .MaximumStepCount - source.TotalStepCount;
        if (additionalStepLimit <= 0 ||
            additionalStepLimit > remainingLimit)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuationResult
                    .InvalidAdditionalStepLimit;
        }

        if (PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraverser
                .TryTraverse(
                    source.FinalCursorSet,
                    source.TieBreak,
                    additionalStepLimit,
                    out var segment) !=
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalResult
                .Traversed ||
            segment is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuationResult
                    .TraversalFailed;
        }

        if (segment.StepCount == 0 ||
            !ReferenceEquals(
                segment.InitialCursorSet,
                source.FinalCursorSet) ||
            segment.TieBreak != source.TieBreak)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuationResult
                    .InvalidSource;
        }

        continuation = new(source, segment);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalContinuationResult
                .Continued;
    }
}
