namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeSourceProjectionResult
{
    Projected,
    MissingAcknowledgementViewProjection,
    SourceMismatch,
    EventLimitExceeded,
    InvalidMovieTimescale,
    MovieTimescaleMismatch,
    InvalidEvent,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeSourceProjection
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeSourceProjection(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewSourceProjection
                source,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjection
                timeProjection)
    {
        Source = source;
        TimeProjection = timeProjection;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewSourceProjection
            Source
    { get; }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjection
            TimeProjection
    { get; }
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeSourceProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeSourceProjectionResult
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewSourceProjection?
                    source,
                out
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeSourceProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeSourceProjectionResult
                    .MissingAcknowledgementViewProjection;
        }

        if (!ReferenceEquals(source.View.Completion, source.Source.Completion))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeSourceProjectionResult
                    .SourceMismatch;
        }

        var result =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjector
                .TryProject(source.View, out var timeProjection);
        if (result !=
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjectionResult
                    .Projected ||
            timeProjection is null)
        {
            return result switch
            {
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjectionResult
                    .EventLimitExceeded =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeSourceProjectionResult
                        .EventLimitExceeded,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjectionResult
                    .InvalidMovieTimescale =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeSourceProjectionResult
                        .InvalidMovieTimescale,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjectionResult
                    .MovieTimescaleMismatch =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeSourceProjectionResult
                        .MovieTimescaleMismatch,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjectionResult
                    .InvalidEvent =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeSourceProjectionResult
                        .InvalidEvent,
                _ =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeSourceProjectionResult
                        .SourceMismatch,
            };
        }

        if (!ReferenceEquals(timeProjection.AcknowledgementView, source.View) ||
            timeProjection.EventCount != source.View.EventCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeSourceProjectionResult
                    .SourceMismatch;
        }

        projection = new(source, timeProjection);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeSourceProjectionResult
                .Projected;
    }
}
