namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineSourceProjectionResult
{
    Projected,
    InvalidInput,
    SourceMismatch,
    InvalidDeadlineInterval,
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineSourceProjection
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineSourceProjection(
            PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginSourceProjection
                source,
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjection
                deadlines)
    {
        Source = source;
        Deadlines = deadlines;
    }

    public
        PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginSourceProjection
            Source
    { get; }

    public PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjection
        Deadlines
    { get; }
}

public static class
    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineSourceProjector
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineSourceProjectionResult
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginSourceProjection?
                    source,
                out
                    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineSourceProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineSourceProjectionResult
                    .InvalidInput;
        }

        if (!ReferenceEquals(source.Timing.Source, source.Source.Timing))
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineSourceProjectionResult
                    .SourceMismatch;
        }

        var result =
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjector.Project(
                source.Timing,
                out var deadlines);
        if (result !=
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjectionResult
                    .Projected ||
            deadlines is null)
        {
            return result switch
            {
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjectionResult
                    .InvalidDeadlineInterval =>
                    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineSourceProjectionResult
                        .InvalidDeadlineInterval,
                _ =>
                    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineSourceProjectionResult
                        .SourceMismatch,
            };
        }

        if (!ReferenceEquals(deadlines.Source, source.Timing))
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineSourceProjectionResult
                    .SourceMismatch;
        }

        projection = new(source, deadlines);
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineSourceProjectionResult
                .Projected;
    }
}
