namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionResult
{
    Certified,
    MissingSource,
    SourceNotExhausted,
    ProjectionMismatch,
    InvalidCursorState,
    EventCountMismatch,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletion
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletion(
            IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalSource
                source,
            int initialRemainingCount)
    {
        Source = source;
        InitialRemainingCount = initialRemainingCount;
    }

    public
        IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalSource
            Source
    { get; }

    public int InitialRemainingCount { get; }

    public int ExpectedEventCount => InitialRemainingCount;

    public int ActualEventCount => Source.TotalEventCount;

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor
        InitialCursor => Source.InitialCursor;

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor
        FinalCursor => Source.FinalCursor;
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionFactory
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionResult
            TryCreate(
                IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalSource?
                    source,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletion?
                    completion)
    {
        completion = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionResult
                    .MissingSource;
        }

        if (!source.IsExhausted)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionResult
                    .SourceNotExhausted;
        }

        var initial = source.InitialCursor;
        var final = source.FinalCursor;
        if (!ReferenceEquals(initial.Projection, final.Projection))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionResult
                    .ProjectionMismatch;
        }

        if (initial.NextEventIndex < 0 ||
            initial.NextEventIndex > initial.Projection.EventCount ||
            final.NextEventIndex < initial.NextEventIndex ||
            !final.IsExhausted)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionResult
                    .InvalidCursorState;
        }

        var initialRemainingCount = initial.RemainingCount;
        if (source.TotalEventCount != initialRemainingCount ||
            final.NextEventIndex !=
                initial.NextEventIndex + source.TotalEventCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionResult
                    .EventCountMismatch;
        }

        completion = new(source, initialRemainingCount);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletionResult
                .Certified;
    }
}
