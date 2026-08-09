namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionResult
{
    Certified,
    MissingSource,
    SourceNotExhausted,
    InvalidTieBreak,
    CursorSourceMismatch,
    InvalidCursorState,
    StepCountMismatch,
}

public sealed class PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletion
{
    internal PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletion(
        IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalSource
            source,
        int initialVideoRemainingCount,
        int initialAudioRemainingCount)
    {
        Source = source;
        InitialVideoRemainingCount = initialVideoRemainingCount;
        InitialAudioRemainingCount = initialAudioRemainingCount;
    }

    public IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalSource
        Source
    { get; }

    public int InitialVideoRemainingCount { get; }

    public int InitialAudioRemainingCount { get; }

    public int ExpectedStepCount =>
        InitialVideoRemainingCount + InitialAudioRemainingCount;

    public int ActualStepCount => Source.TotalStepCount;

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet
        InitialCursorSet => Source.InitialCursorSet;

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet FinalCursorSet
        => Source.FinalCursorSet;
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionFactory
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionResult
            TryCreate(
                IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalSource?
                    source,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletion?
                    completion)
    {
        completion = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionResult
                    .MissingSource;
        }

        if (!source.IsExhausted)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionResult
                    .SourceNotExhausted;
        }

        if (!Enum.IsDefined(source.TieBreak))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionResult
                    .InvalidTieBreak;
        }

        var initial = source.InitialCursorSet;
        var final = source.FinalCursorSet;
        if (!ReferenceEquals(initial.Selection, final.Selection))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionResult
                    .CursorSourceMismatch;
        }

        if (!TryValidateRole(
                initial.Selection.VideoStream,
                initial.VideoCursor,
                final.VideoCursor,
                out var videoRemaining) ||
            !TryValidateRole(
                initial.Selection.AudioStream,
                initial.AudioCursor,
                final.AudioCursor,
                out var audioRemaining))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionResult
                    .InvalidCursorState;
        }

        var expectedStepCount = checked(videoRemaining + audioRemaining);
        if (source.TotalStepCount != expectedStepCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionResult
                    .StepCountMismatch;
        }

        completion = new(source, videoRemaining, audioRemaining);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionResult
                .Certified;
    }

    private static bool TryValidateRole(
        PlaybackIsoBmffIndexedTrackCodecPacketSequence? stream,
        PlaybackIsoBmffTrackCodecPacketSequenceCursor? initialCursor,
        PlaybackIsoBmffTrackCodecPacketSequenceCursor? finalCursor,
        out int initialRemainingCount)
    {
        initialRemainingCount = 0;
        if (stream is null)
        {
            return initialCursor is null && finalCursor is null;
        }

        if (initialCursor is null || finalCursor is null ||
            !ReferenceEquals(initialCursor.Sequence, stream.Sequence) ||
            !ReferenceEquals(finalCursor.Sequence, stream.Sequence) ||
            initialCursor.NextSequenceIndex < 0 ||
            initialCursor.NextSequenceIndex > stream.Sequence.Entries.Count ||
            !finalCursor.IsExhausted)
        {
            return false;
        }

        initialRemainingCount = initialCursor.RemainingCount;
        return true;
    }
}
