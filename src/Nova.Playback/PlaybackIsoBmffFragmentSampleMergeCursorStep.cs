namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffFragmentSampleMergeCursorStepResult
{
    Stepped,
    Completed,
    InvalidInput,
    InvalidCursor,
}

public sealed class PlaybackIsoBmffFragmentSampleMergeCursorStep
{
    internal PlaybackIsoBmffFragmentSampleMergeCursorStep(
        PlaybackIsoBmffFragmentSampleMergeCursor cursor,
        PlaybackIsoBmffFragmentBatchPresentationSample sample,
        PlaybackIsoBmffFragmentSampleMergeCursor nextCursor)
    {
        Cursor = cursor;
        Sample = sample;
        NextCursor = nextCursor;
    }

    public PlaybackIsoBmffFragmentSampleMergeCursor Cursor { get; }

    public PlaybackIsoBmffFragmentBatchPresentationSample Sample { get; }

    public PlaybackIsoBmffFragmentSampleMergeCursor NextCursor { get; }

    public int Index => Cursor.NextIndex;
}

public static class PlaybackIsoBmffFragmentSampleMergeCursorStepper
{
    public static PlaybackIsoBmffFragmentSampleMergeCursorStepResult Step(
        PlaybackIsoBmffFragmentSampleMergeCursor? cursor,
        out PlaybackIsoBmffFragmentSampleMergeCursorStep? step)
    {
        step = null;
        if (cursor is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeCursorStepResult.InvalidInput;
        }

        if (cursor.NextIndex < 0 || cursor.NextIndex > cursor.Source.Samples.Count)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeCursorStepResult.InvalidCursor;
        }

        if (cursor.IsComplete)
        {
            return PlaybackIsoBmffFragmentSampleMergeCursorStepResult.Completed;
        }

        var nextCursor = new PlaybackIsoBmffFragmentSampleMergeCursor(
            cursor.Source,
            cursor.NextIndex + 1);
        step = new(
            cursor,
            cursor.Source.Samples[cursor.NextIndex],
            nextCursor);
        return PlaybackIsoBmffFragmentSampleMergeCursorStepResult.Stepped;
    }
}
