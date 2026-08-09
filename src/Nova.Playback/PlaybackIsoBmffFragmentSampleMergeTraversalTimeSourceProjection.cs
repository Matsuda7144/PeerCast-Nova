namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffFragmentSampleMergeTraversalTimeSourceProjectionResult
{
    Projected,
    InvalidInput,
    SourceMismatch,
    SampleLimitExceeded,
    InvalidMediaTimescale,
    MediaTimescaleMismatch,
    InvalidSampleTime,
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalTimeSourceProjection
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalTimeSourceProjection(
            PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewSourceProjection
                source,
            PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjection timing)
    {
        Source = source;
        Timing = timing;
    }

    public
        PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewSourceProjection
            Source
    { get; }

    public PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjection Timing
    { get; }
}

public static class
    PlaybackIsoBmffFragmentSampleMergeTraversalTimeSourceProjector
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalTimeSourceProjectionResult
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewSourceProjection?
                    source,
                out
                    PlaybackIsoBmffFragmentSampleMergeTraversalTimeSourceProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalTimeSourceProjectionResult
                    .InvalidInput;
        }

        if (!ReferenceEquals(source.View.Completion, source.Source))
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalTimeSourceProjectionResult
                    .SourceMismatch;
        }

        var result =
            PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjector.Project(
                source.View,
                out var timing);
        if (result !=
                PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjectionResult
                    .Projected ||
            timing is null)
        {
            return result switch
            {
                PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjectionResult
                    .SampleLimitExceeded =>
                    PlaybackIsoBmffFragmentSampleMergeTraversalTimeSourceProjectionResult
                        .SampleLimitExceeded,
                PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjectionResult
                    .InvalidMediaTimescale =>
                    PlaybackIsoBmffFragmentSampleMergeTraversalTimeSourceProjectionResult
                        .InvalidMediaTimescale,
                PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjectionResult
                    .MediaTimescaleMismatch =>
                    PlaybackIsoBmffFragmentSampleMergeTraversalTimeSourceProjectionResult
                        .MediaTimescaleMismatch,
                PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjectionResult
                    .InvalidSampleTime =>
                    PlaybackIsoBmffFragmentSampleMergeTraversalTimeSourceProjectionResult
                        .InvalidSampleTime,
                _ =>
                    PlaybackIsoBmffFragmentSampleMergeTraversalTimeSourceProjectionResult
                        .SourceMismatch,
            };
        }

        if (!ReferenceEquals(timing.AcknowledgementView, source.View) ||
            timing.SampleCount != source.View.AcknowledgementCount)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalTimeSourceProjectionResult
                    .SourceMismatch;
        }

        projection = new(source, timing);
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalTimeSourceProjectionResult
                .Projected;
    }
}
