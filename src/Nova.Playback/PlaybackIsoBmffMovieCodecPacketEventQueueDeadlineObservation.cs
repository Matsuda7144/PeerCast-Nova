using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineState
{
    Pending,
    Ready,
    Expired,
}

public enum PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservationResult
{
    Observed,
    MissingDeadlineProjection,
    EventLimitExceeded,
    SourceMismatch,
    InvalidDeadline,
}

public sealed class PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservation
{
    internal PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservation(
        PlaybackIsoBmffMovieCodecPacketEventQueueDeadline deadline,
        BigInteger observationTick,
        PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineState state)
    {
        Deadline = deadline;
        ObservationTick = observationTick;
        State = state;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDeadline Deadline { get; }

    public BigInteger ObservationTick { get; }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineState State
    {
        get;
    }

    public BigInteger TicksUntilReady =>
        Deadline.ReadyTick - ObservationTick;

    public BigInteger TicksUntilExpiration =>
        Deadline.ExpirationTickExclusive - ObservationTick;
}

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservationSnapshot
{
    internal
        PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservationSnapshot(
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjection source,
            BigInteger observationTick,
            ReadOnlyCollection<
                PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservation>
                observations)
    {
        Source = source;
        ObservationTick = observationTick;
        Observations = observations;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjection Source
    {
        get;
    }

    public BigInteger ObservationTick { get; }

    public IReadOnlyList<
        PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservation>
        Observations
    { get; }
}

public static class
    PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObserver
{
    public static
        PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservationResult
        Observe(
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineProjection? source,
            BigInteger observationTick,
            out PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservationSnapshot?
                snapshot)
    {
        snapshot = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservationResult
                    .MissingDeadlineProjection;
        }

        if (source.Deadlines.Count >
            PlaybackIsoBmffMovieCodecPacketEventProjector.MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservationResult
                    .EventLimitExceeded;
        }

        if (source.Deadlines.Count != source.Source.Events.Count)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservationResult
                    .SourceMismatch;
        }

        var observations = new
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservation[
                source.Deadlines.Count];
        for (var index = 0; index < source.Deadlines.Count; index++)
        {
            var deadline = source.Deadlines[index];
            if (!ReferenceEquals(deadline.Time, source.Source.Events[index]))
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservationResult
                        .SourceMismatch;
            }

            if (deadline.ReadyTick >= deadline.ExpirationTickExclusive)
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservationResult
                        .InvalidDeadline;
            }

            var state = observationTick < deadline.ReadyTick
                ? PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineState.Pending
                : observationTick < deadline.ExpirationTickExclusive
                    ? PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineState.Ready
                    : PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineState
                        .Expired;
            observations[index] = new(deadline, observationTick, state);
        }

        snapshot = new(
            source,
            observationTick,
            Array.AsReadOnly(observations));
        return
            PlaybackIsoBmffMovieCodecPacketEventQueueDeadlineObservationResult
                .Observed;
    }
}
