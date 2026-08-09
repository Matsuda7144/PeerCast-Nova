using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityPolicy
{
    ReadyOnly,
    ReadyOrExpired,
}

public enum PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityKind
{
    IneligiblePending,
    EligibleReady,
    IneligibleExpired,
    EligibleExpired,
}

public enum PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityResult
{
    Projected,
    MissingObservationSnapshot,
    EventLimitExceeded,
    InvalidPolicy,
    SourceMismatch,
}

public sealed class PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibility
{
    internal PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibility(
        PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservation observation,
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityKind kind)
    {
        Observation = observation;
        Kind = kind;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservation
        Observation
    { get; }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityKind Kind
    {
        get;
    }

    public bool IsEligible => Kind is
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityKind
            .EligibleReady or
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityKind
            .EligibleExpired;
}

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityProjection
{
    internal
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityProjection(
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservationSnapshot
                source,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityPolicy
                policy,
            ReadOnlyCollection<
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibility>
                entries)
    {
        Source = source;
        Policy = policy;
        Entries = entries;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservationSnapshot
        Source
    { get; }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityPolicy
        Policy
    { get; }

    public IReadOnlyList<
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibility> Entries
    {
        get;
    }
}

public static class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityProjector
{
    public static
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityResult
        Project(
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservationSnapshot?
                source,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityPolicy
                policy,
            out PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityProjection?
                projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityResult
                    .MissingObservationSnapshot;
        }

        if (source.Observations.Count >
            PlaybackIsoBmffMovieCodecPacketEventProjector.MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityResult
                    .EventLimitExceeded;
        }

        if (!Enum.IsDefined(policy))
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityResult
                    .InvalidPolicy;
        }

        if (source.Observations.Count != source.Source.Deadlines.Count)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityResult
                    .SourceMismatch;
        }

        var entries = new
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibility[
                source.Observations.Count];
        for (var index = 0; index < source.Observations.Count; index++)
        {
            var observation = source.Observations[index];
            if (!ReferenceEquals(
                    observation.Deadline,
                    source.Source.Deadlines[index]))
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityResult
                        .SourceMismatch;
            }

            var kind = observation.State switch
            {
                PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineState.Pending =>
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityKind
                        .IneligiblePending,
                PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineState.Ready =>
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityKind
                        .EligibleReady,
                PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineState.Expired
                    when policy ==
                        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityPolicy
                            .ReadyOrExpired =>
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityKind
                        .EligibleExpired,
                _ =>
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityKind
                        .IneligibleExpired,
            };
            entries[index] = new(observation, kind);
        }

        projection = new(source, policy, Array.AsReadOnly(entries));
        return
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityResult
                .Projected;
    }
}
