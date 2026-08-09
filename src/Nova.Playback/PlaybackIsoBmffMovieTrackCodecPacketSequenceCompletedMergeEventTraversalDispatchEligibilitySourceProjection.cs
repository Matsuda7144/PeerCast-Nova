namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilitySourceProjectionResult
{
    Projected,
    MissingObservationSourceProjection,
    SourceMismatch,
    EventLimitExceeded,
    InvalidPolicy,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilitySourceProjection
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilitySourceProjection(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationSourceProjection
                source,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityPolicy
                policy,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityProjection
                eligibilityProjection)
    {
        Source = source;
        Policy = policy;
        EligibilityProjection = eligibilityProjection;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationSourceProjection
            Source
    { get; }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityPolicy
            Policy
    { get; }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityProjection
            EligibilityProjection
    { get; }
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilitySourceProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilitySourceProjectionResult
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationSourceProjection?
                    source,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityPolicy
                    policy,
                out
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilitySourceProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilitySourceProjectionResult
                    .MissingObservationSourceProjection;
        }

        if (!ReferenceEquals(
                source.ObservationSnapshot.Source,
                source.Source.DeadlineProjection))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilitySourceProjectionResult
                    .SourceMismatch;
        }

        var result =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityProjector
                .TryProject(
                    source.ObservationSnapshot,
                    policy,
                    out var eligibilityProjection);
        if (result !=
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityResult
                    .Projected ||
            eligibilityProjection is null)
        {
            return result switch
            {
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityResult
                    .EventLimitExceeded =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilitySourceProjectionResult
                        .EventLimitExceeded,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityResult
                    .InvalidPolicy =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilitySourceProjectionResult
                        .InvalidPolicy,
                _ =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilitySourceProjectionResult
                        .SourceMismatch,
            };
        }

        if (!ReferenceEquals(
                eligibilityProjection.Source,
                source.ObservationSnapshot) ||
            eligibilityProjection.Policy != policy)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilitySourceProjectionResult
                    .SourceMismatch;
        }

        projection = new(source, policy, eligibilityProjection);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilitySourceProjectionResult
                .Projected;
    }
}
