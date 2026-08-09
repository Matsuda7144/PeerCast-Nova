using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffTrackSampleEditAdmissionDisposition
{
    Admitted,
    NotPresented,
    PartialSampleRejected,
}

public enum PlaybackIsoBmffTrackSampleEditAdmissionProjectionResult
{
    Projected,
    MissingPlan,
    MissingTimeToSample,
    MissingEditList,
    MissingMovieTiming,
    MissingMediaTiming,
    PlanLimitExceeded,
    SourceMismatch,
    InvalidConfiguration,
    InvalidReconciliation,
    SampleNumberOutOfRange,
    UnsupportedMediaRate,
    NonIntegralMediaDuration,
    OverlapLimitExceeded,
    InvalidMovieOverlap,
}

public sealed class PlaybackIsoBmffTrackSampleEditAdmission
{
    internal PlaybackIsoBmffTrackSampleEditAdmission(
        PlaybackIsoBmffTrackSampleSequencePlanEntry planEntry,
        PlaybackIsoBmffTrackSampleEditAdmissionDisposition disposition,
        PlaybackIsoBmffEditedSampleAdmission? admission)
    {
        PlanEntry = planEntry;
        Disposition = disposition;
        Admission = admission;
    }

    public PlaybackIsoBmffTrackSampleSequencePlanEntry PlanEntry { get; }

    public ulong SampleNumber => PlanEntry.SampleNumber;

    public PlaybackIsoBmffTrackSampleEditAdmissionDisposition Disposition
    { get; }

    public PlaybackIsoBmffEditedSampleAdmission? Admission { get; }

    public bool IsAdmitted => Disposition ==
        PlaybackIsoBmffTrackSampleEditAdmissionDisposition.Admitted;
}

public sealed class PlaybackIsoBmffTrackSampleEditAdmissionProjection
{
    internal PlaybackIsoBmffTrackSampleEditAdmissionProjection(
        PlaybackIsoBmffTrackSampleSequencePlan plan,
        PlaybackIsoBmffTimeToSampleBox timeToSample,
        PlaybackIsoBmffCompositionTimeToSampleBox? compositionTimeToSample,
        PlaybackIsoBmffEditListBox editList,
        PlaybackIsoBmffMovieHeaderTimingBox movieTiming,
        PlaybackIsoBmffMediaHeaderTimingBox mediaTiming,
        ReadOnlyCollection<PlaybackIsoBmffTrackSampleEditAdmission> entries)
    {
        Plan = plan;
        TimeToSample = timeToSample;
        CompositionTimeToSample = compositionTimeToSample;
        EditList = editList;
        MovieTiming = movieTiming;
        MediaTiming = mediaTiming;
        Entries = entries;
    }

    public PlaybackIsoBmffTrackSampleSequencePlan Plan { get; }

    public PlaybackIsoBmffTimeToSampleBox TimeToSample { get; }

    public PlaybackIsoBmffCompositionTimeToSampleBox? CompositionTimeToSample
    { get; }

    public PlaybackIsoBmffEditListBox EditList { get; }

    public PlaybackIsoBmffMovieHeaderTimingBox MovieTiming { get; }

    public PlaybackIsoBmffMediaHeaderTimingBox MediaTiming { get; }

    public IReadOnlyList<PlaybackIsoBmffTrackSampleEditAdmission> Entries
    { get; }
}

public static class PlaybackIsoBmffTrackSampleEditAdmissionProjector
{
    public static PlaybackIsoBmffTrackSampleEditAdmissionProjectionResult
        Project(
            PlaybackIsoBmffTrackSampleSequencePlan? plan,
            PlaybackIsoBmffTimeToSampleBox? timeToSample,
            PlaybackIsoBmffCompositionTimeToSampleBox? compositionTimeToSample,
            PlaybackIsoBmffEditListBox? editList,
            PlaybackIsoBmffMovieHeaderTimingBox? movieTiming,
            PlaybackIsoBmffMediaHeaderTimingBox? mediaTiming,
            out PlaybackIsoBmffTrackSampleEditAdmissionProjection? projection,
            PlaybackIsoBmffSampleEditOverlapLookupOptions? options = null)
    {
        projection = null;
        if (plan is null)
        {
            return PlaybackIsoBmffTrackSampleEditAdmissionProjectionResult
                .MissingPlan;
        }

        if (timeToSample is null)
        {
            return PlaybackIsoBmffTrackSampleEditAdmissionProjectionResult
                .MissingTimeToSample;
        }

        if (editList is null)
        {
            return PlaybackIsoBmffTrackSampleEditAdmissionProjectionResult
                .MissingEditList;
        }

        if (movieTiming is null)
        {
            return PlaybackIsoBmffTrackSampleEditAdmissionProjectionResult
                .MissingMovieTiming;
        }

        if (mediaTiming is null)
        {
            return PlaybackIsoBmffTrackSampleEditAdmissionProjectionResult
                .MissingMediaTiming;
        }

        if (plan.Entries.Count >
            PlaybackIsoBmffTrackSampleSequencePlanFactory
                .MaximumPlannedSampleCount)
        {
            return PlaybackIsoBmffTrackSampleEditAdmissionProjectionResult
                .PlanLimitExceeded;
        }

        var entries = new PlaybackIsoBmffTrackSampleEditAdmission[
            plan.Entries.Count];
        for (var index = 0; index < plan.Entries.Count; index++)
        {
            var planEntry = plan.Entries[index];
            if (planEntry.SequenceIndex != index ||
                planEntry.SampleNumber !=
                    plan.FirstSampleNumber + (ulong)index ||
                planEntry.SampleNumber > plan.Reconciliation.SampleCount)
            {
                return PlaybackIsoBmffTrackSampleEditAdmissionProjectionResult
                    .SourceMismatch;
            }

            var result = PlaybackIsoBmffEditedSampleAdmissionLookup.Lookup(
                timeToSample,
                compositionTimeToSample,
                plan.Reconciliation,
                editList,
                movieTiming,
                mediaTiming,
                planEntry.SampleNumber,
                out var admission,
                options);
            switch (result)
            {
                case PlaybackIsoBmffEditedSampleAdmissionLookupResult.Found
                    when admission is not null:
                    entries[index] = new(
                        planEntry,
                        PlaybackIsoBmffTrackSampleEditAdmissionDisposition
                            .Admitted,
                        admission);
                    break;
                case PlaybackIsoBmffEditedSampleAdmissionLookupResult
                    .NotPresented:
                    entries[index] = new(
                        planEntry,
                        PlaybackIsoBmffTrackSampleEditAdmissionDisposition
                            .NotPresented,
                        null);
                    break;
                case PlaybackIsoBmffEditedSampleAdmissionLookupResult
                    .PartialSampleRejected:
                    entries[index] = new(
                        planEntry,
                        PlaybackIsoBmffTrackSampleEditAdmissionDisposition
                            .PartialSampleRejected,
                        null);
                    break;
                default:
                    return MapFailure(result);
            }
        }

        projection = new(
            plan,
            timeToSample,
            compositionTimeToSample,
            editList,
            movieTiming,
            mediaTiming,
            Array.AsReadOnly(entries));
        return PlaybackIsoBmffTrackSampleEditAdmissionProjectionResult
            .Projected;
    }

    private static PlaybackIsoBmffTrackSampleEditAdmissionProjectionResult
        MapFailure(PlaybackIsoBmffEditedSampleAdmissionLookupResult result) =>
        result switch
        {
            PlaybackIsoBmffEditedSampleAdmissionLookupResult
                .InvalidConfiguration =>
                PlaybackIsoBmffTrackSampleEditAdmissionProjectionResult
                    .InvalidConfiguration,
            PlaybackIsoBmffEditedSampleAdmissionLookupResult
                .SampleNumberOutOfRange =>
                PlaybackIsoBmffTrackSampleEditAdmissionProjectionResult
                    .SampleNumberOutOfRange,
            PlaybackIsoBmffEditedSampleAdmissionLookupResult
                .UnsupportedMediaRate =>
                PlaybackIsoBmffTrackSampleEditAdmissionProjectionResult
                    .UnsupportedMediaRate,
            PlaybackIsoBmffEditedSampleAdmissionLookupResult
                .NonIntegralMediaDuration =>
                PlaybackIsoBmffTrackSampleEditAdmissionProjectionResult
                    .NonIntegralMediaDuration,
            PlaybackIsoBmffEditedSampleAdmissionLookupResult
                .OverlapLimitExceeded =>
                PlaybackIsoBmffTrackSampleEditAdmissionProjectionResult
                    .OverlapLimitExceeded,
            PlaybackIsoBmffEditedSampleAdmissionLookupResult
                .InvalidMovieOverlap =>
                PlaybackIsoBmffTrackSampleEditAdmissionProjectionResult
                    .InvalidMovieOverlap,
            _ => PlaybackIsoBmffTrackSampleEditAdmissionProjectionResult
                .InvalidReconciliation,
        };
}
