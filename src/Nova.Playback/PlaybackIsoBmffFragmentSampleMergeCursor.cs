namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffFragmentSampleMergeCursorResult
{
    Projected,
    InvalidInput,
    IndexOutOfRange,
}

public sealed class PlaybackIsoBmffFragmentSampleMergeCursor
{
    internal PlaybackIsoBmffFragmentSampleMergeCursor(
        PlaybackIsoBmffFragmentSampleBatchPresentationMerge source,
        int nextIndex)
    {
        Source = source;
        NextIndex = nextIndex;
    }

    public PlaybackIsoBmffFragmentSampleBatchPresentationMerge Source
    { get; }

    public int NextIndex { get; }

    public int Count => Source.Samples.Count;

    public int RemainingCount => Count - NextIndex;

    public bool IsComplete => NextIndex == Count;

    public PlaybackIsoBmffFragmentBatchPresentationSample? Current =>
        IsComplete ? null : Source.Samples[NextIndex];
}

public static class PlaybackIsoBmffFragmentSampleMergeCursorProjector
{
    public static PlaybackIsoBmffFragmentSampleMergeCursorResult Project(
        PlaybackIsoBmffFragmentSampleBatchPresentationMerge? source,
        int nextIndex,
        out PlaybackIsoBmffFragmentSampleMergeCursor? cursor)
    {
        cursor = null;
        if (source is null)
        {
            return PlaybackIsoBmffFragmentSampleMergeCursorResult.InvalidInput;
        }

        if (nextIndex < 0 || nextIndex > source.Samples.Count)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeCursorResult.IndexOutOfRange;
        }

        cursor = new(source, nextIndex);
        return PlaybackIsoBmffFragmentSampleMergeCursorResult.Projected;
    }
}
