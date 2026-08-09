using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityPolicy
{
    ReadyOnly,
    ReadyOrExpired,
}

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityKind
{
    IneligiblePending,
    EligibleReady,
    IneligibleExpired,
    EligibleExpired,
}

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityResult
{
    Projected,
    MissingObservationSnapshot,
    EventLimitExceeded,
    InvalidPolicy,
    SourceMismatch,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibility
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibility(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservation
                observation,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityKind
                kind)
    {
        Observation = observation;
        Kind = kind;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservation
            Observation
    { get; }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityKind
            Kind
    { get; }

    public bool IsEligible => Kind is
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityKind
            .EligibleReady or
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityKind
            .EligibleExpired;
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityProjection
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityProjection(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationSnapshot
                source,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityPolicy
                policy,
            ReadOnlyCollection<
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibility>
                entries)
    {
        Source = source;
        Policy = policy;
        Entries = entries;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationSnapshot
            Source
    { get; }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityPolicy
            Policy
    { get; }

    public IReadOnlyList<
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibility>
        Entries
    { get; }
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityResult
            TryProject(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationSnapshot?
                    source,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityPolicy
                    policy,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityProjection?
                    projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityResult
                    .MissingObservationSnapshot;
        }

        if (source.Observations.Count >
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjector
                .MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityResult
                    .EventLimitExceeded;
        }

        if (!Enum.IsDefined(policy))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityResult
                    .InvalidPolicy;
        }

        if (source.Observations.Count != source.Source.EventCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityResult
                    .SourceMismatch;
        }

        var entries = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibility[
                source.Observations.Count];
        for (var index = 0; index < source.Observations.Count; index++)
        {
            var observation = source.Observations[index];
            if (!ReferenceEquals(
                    observation.Deadline,
                    source.Source.Deadlines[index]))
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityResult
                        .SourceMismatch;
            }

            var kind = observation.State switch
            {
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineState
                    .Pending =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityKind
                        .IneligiblePending,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineState
                    .Ready =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityKind
                        .EligibleReady,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineState
                    .Expired when policy ==
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityPolicy
                        .ReadyOrExpired =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityKind
                        .EligibleExpired,
                _ =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityKind
                        .IneligibleExpired,
            };
            entries[index] = new(observation, kind);
        }

        projection = new(source, policy, Array.AsReadOnly(entries));
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityResult
                .Projected;
    }
}
