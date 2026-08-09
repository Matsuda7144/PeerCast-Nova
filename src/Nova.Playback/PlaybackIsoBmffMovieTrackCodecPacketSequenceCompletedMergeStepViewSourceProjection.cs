namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewSourceProjectionResult
{
    Projected,
    MissingCompletionProjection,
    SourceMismatch,
    UnsupportedTraversalSource,
    StepCountMismatch,
    InvalidStepChain,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewSourceProjection
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewSourceProjection(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionProjection
                source,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepView
                stepView)
    {
        Source = source;
        StepView = stepView;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionProjection
            Source
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepView
        StepView
    { get; }
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewSourceProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewSourceProjectionResult
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionProjection?
                    source,
                out
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewSourceProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewSourceProjectionResult
                    .MissingCompletionProjection;
        }

        if (!ReferenceEquals(
                source.Completion.Source,
                source.Source.TraversalSource))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewSourceProjectionResult
                    .SourceMismatch;
        }

        var result =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewProjector
                .TryProject(source.Completion, out var stepView);
        if (result !=
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewProjectionResult
                    .Projected ||
            stepView is null)
        {
            return result switch
            {
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewProjectionResult
                    .UnsupportedTraversalSource =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewSourceProjectionResult
                        .UnsupportedTraversalSource,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewProjectionResult
                    .StepCountMismatch =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewSourceProjectionResult
                        .StepCountMismatch,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewProjectionResult
                    .InvalidStepChain =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewSourceProjectionResult
                        .InvalidStepChain,
                _ =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewSourceProjectionResult
                        .SourceMismatch,
            };
        }

        if (!ReferenceEquals(stepView.Completion, source.Completion))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewSourceProjectionResult
                    .SourceMismatch;
        }

        projection = new(source, stepView);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewSourceProjectionResult
                .Projected;
    }
}
