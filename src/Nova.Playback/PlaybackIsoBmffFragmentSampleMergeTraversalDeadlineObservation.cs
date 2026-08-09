using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineState
{
    Pending,
    Ready,
    Expired,
}

public enum
    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationResult
{
    Observed,
    InvalidInput,
    SourceMismatch,
    InvalidDeadline,
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservation
{
    internal PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservation(
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadline deadline,
        BigInteger observationTick,
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineState decodeState,
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineState
            presentationState)
    {
        Deadline = deadline;
        ObservationTick = observationTick;
        DecodeState = decodeState;
        PresentationState = presentationState;
    }

    public PlaybackIsoBmffFragmentSampleMergeTraversalDeadline Deadline
    { get; }

    public BigInteger ObservationTick { get; }

    public PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineState DecodeState
    { get; }

    public PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineState
        PresentationState
    { get; }

    public BigInteger TicksUntilDecodeReady =>
        Deadline.DecodeReadyTick - ObservationTick;

    public BigInteger TicksUntilDecodeExpiration =>
        Deadline.DecodeExpirationTickExclusive - ObservationTick;

    public BigInteger TicksUntilPresentationReady =>
        Deadline.PresentationReadyTick - ObservationTick;

    public BigInteger TicksUntilPresentationExpiration =>
        Deadline.PresentationExpirationTickExclusive - ObservationTick;
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSnapshot
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSnapshot(
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjection source,
            BigInteger observationTick,
            ReadOnlyCollection<
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservation>
                observations)
    {
        Source = source;
        ObservationTick = observationTick;
        Observations = observations;
    }

    public PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjection Source
    { get; }

    public BigInteger ObservationTick { get; }

    public IReadOnlyList<
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservation>
        Observations
    { get; }

    public int SampleCount => Observations.Count;
}

public static class
    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObserver
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationResult
            Observe(
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineProjection?
                    source,
                BigInteger observationTick,
                out
                    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSnapshot?
                        snapshot)
    {
        snapshot = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationResult
                    .InvalidInput;
        }

        if (source.SampleCount != source.Source.SampleCount)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationResult
                    .SourceMismatch;
        }

        var observations = new
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservation[
                source.SampleCount];
        for (var index = 0; index < observations.Length; index++)
        {
            var deadline = source.Deadlines[index];
            if (!ReferenceEquals(deadline.Source, source.Source.Samples[index]))
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationResult
                        .SourceMismatch;
            }

            if (deadline.DecodeReadyTick >=
                    deadline.DecodeExpirationTickExclusive ||
                deadline.PresentationReadyTick >=
                    deadline.PresentationExpirationTickExclusive)
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationResult
                        .InvalidDeadline;
            }

            observations[index] = new(
                deadline,
                observationTick,
                Classify(
                    deadline.DecodeReadyTick,
                    deadline.DecodeExpirationTickExclusive,
                    observationTick),
                Classify(
                    deadline.PresentationReadyTick,
                    deadline.PresentationExpirationTickExclusive,
                    observationTick));
        }

        snapshot = new(
            source,
            observationTick,
            Array.AsReadOnly(observations));
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationResult
                .Observed;
    }

    private static PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineState
        Classify(
            BigInteger readyTick,
            BigInteger expirationTickExclusive,
            BigInteger observationTick) =>
        observationTick < readyTick
            ? PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineState.Pending
            : observationTick < expirationTickExclusive
                ? PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineState.Ready
                : PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineState
                    .Expired;
}
