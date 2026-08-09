namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventSourceProjectionResult
{
    Projected,
    MissingStepViewProjection,
    SourceMismatch,
    InvalidEventLimit,
    EventLimitExceeded,
    MissingAppearance,
    InvalidAppearance,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventSourceProjection
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventSourceProjection(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewSourceProjection
                source,
            int maximumEventCount,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjection
                eventProjection)
    {
        Source = source;
        MaximumEventCount = maximumEventCount;
        EventProjection = eventProjection;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewSourceProjection
            Source
    { get; }

    public int MaximumEventCount { get; }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjection
            EventProjection
    { get; }
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventSourceProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventSourceProjectionResult
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewSourceProjection?
                    source,
                int maximumEventCount,
                out
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventSourceProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventSourceProjectionResult
                    .MissingStepViewProjection;
        }

        if (!ReferenceEquals(
                source.StepView.Completion,
                source.Source.Completion))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventSourceProjectionResult
                    .SourceMismatch;
        }

        var result =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjector
                .TryProject(
                    source.StepView,
                    maximumEventCount,
                    out var eventProjection);
        if (result !=
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjectionResult
                    .Projected ||
            eventProjection is null)
        {
            return result switch
            {
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjectionResult
                    .InvalidEventLimit =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventSourceProjectionResult
                        .InvalidEventLimit,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjectionResult
                    .EventLimitExceeded =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventSourceProjectionResult
                        .EventLimitExceeded,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjectionResult
                    .MissingAppearance =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventSourceProjectionResult
                        .MissingAppearance,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjectionResult
                    .InvalidAppearance =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventSourceProjectionResult
                        .InvalidAppearance,
                _ =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventSourceProjectionResult
                        .SourceMismatch,
            };
        }

        if (!ReferenceEquals(eventProjection.StepView, source.StepView))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventSourceProjectionResult
                    .SourceMismatch;
        }

        projection = new(source, maximumEventCount, eventProjection);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventSourceProjectionResult
                .Projected;
    }
}
