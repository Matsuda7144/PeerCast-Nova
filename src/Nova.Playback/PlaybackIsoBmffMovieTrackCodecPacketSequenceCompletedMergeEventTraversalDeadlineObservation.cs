using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineState
{
    Pending,
    Ready,
    Expired,
}

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationResult
{
    Observed,
    MissingDeadlineProjection,
    EventLimitExceeded,
    SourceMismatch,
    InvalidDeadline,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservation
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservation(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadline
                deadline,
            BigInteger observationTick,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineState
                state)
    {
        Deadline = deadline;
        ObservationTick = observationTick;
        State = state;
    }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadline
        Deadline
    { get; }

    public BigInteger ObservationTick { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineState
        State
    { get; }

    public BigInteger TicksUntilReady => Deadline.ReadyTick - ObservationTick;

    public BigInteger TicksUntilExpiration =>
        Deadline.ExpirationTickExclusive - ObservationTick;
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationSnapshot
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationSnapshot(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineProjection
                source,
            BigInteger observationTick,
            ReadOnlyCollection<
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservation>
                observations)
    {
        Source = source;
        ObservationTick = observationTick;
        Observations = observations;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineProjection
            Source
    { get; }

    public BigInteger ObservationTick { get; }

    public IReadOnlyList<
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservation>
        Observations
    { get; }
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObserver
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationResult
            Observe(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineProjection?
                    source,
                BigInteger observationTick,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationSnapshot?
                    snapshot)
    {
        snapshot = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationResult
                    .MissingDeadlineProjection;
        }

        if (source.EventCount >
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjector
                .MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationResult
                    .EventLimitExceeded;
        }

        if (source.EventCount != source.Source.EventCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationResult
                    .SourceMismatch;
        }

        var observations = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservation[
                source.EventCount];
        for (var index = 0; index < source.EventCount; index++)
        {
            var deadline = source.Deadlines[index];
            if (!ReferenceEquals(deadline.Time, source.Source.Events[index]))
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationResult
                        .SourceMismatch;
            }

            if (deadline.ReadyTick >= deadline.ExpirationTickExclusive)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationResult
                        .InvalidDeadline;
            }

            var state = observationTick < deadline.ReadyTick
                ? PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineState
                    .Pending
                : observationTick < deadline.ExpirationTickExclusive
                    ? PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineState
                        .Ready
                    : PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineState
                        .Expired;
            observations[index] = new(deadline, observationTick, state);
        }

        snapshot = new(source, observationTick, Array.AsReadOnly(observations));
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadlineObservationResult
                .Observed;
    }
}
