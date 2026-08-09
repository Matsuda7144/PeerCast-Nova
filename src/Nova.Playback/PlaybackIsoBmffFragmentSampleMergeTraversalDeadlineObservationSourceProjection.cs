using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSourceProjectionResult
{
    Projected,
    InvalidInput,
    SourceMismatch,
    InvalidDeadline,
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSourceProjection
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSourceProjection(
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineSourceProjection
                source,
            BigInteger observationTick,
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSnapshot
                snapshot)
    {
        Source = source;
        ObservationTick = observationTick;
        Snapshot = snapshot;
    }

    public PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineSourceProjection
        Source
    { get; }

    public BigInteger ObservationTick { get; }

    public
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSnapshot
            Snapshot
    { get; }
}

public static class
    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSourceProjector
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSourceProjectionResult
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineSourceProjection?
                    source,
                BigInteger observationTick,
                out
                    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSourceProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSourceProjectionResult
                    .InvalidInput;
        }

        if (!ReferenceEquals(source.Deadlines.Source, source.Source.Timing))
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSourceProjectionResult
                    .SourceMismatch;
        }

        var result =
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObserver.Observe(
                source.Deadlines,
                observationTick,
                out var snapshot);
        if (result !=
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationResult
                    .Observed ||
            snapshot is null)
        {
            return result switch
            {
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationResult
                    .InvalidDeadline =>
                    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSourceProjectionResult
                        .InvalidDeadline,
                _ =>
                    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSourceProjectionResult
                        .SourceMismatch,
            };
        }

        if (!ReferenceEquals(snapshot.Source, source.Deadlines) ||
            snapshot.ObservationTick != observationTick)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSourceProjectionResult
                    .SourceMismatch;
        }

        projection = new(source, observationTick, snapshot);
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSourceProjectionResult
                .Projected;
    }
}
