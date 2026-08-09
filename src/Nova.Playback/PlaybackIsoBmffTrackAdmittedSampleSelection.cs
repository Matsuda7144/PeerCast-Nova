using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffTrackAdmittedSampleSelectionResult
{
    Selected,
    MissingAdmissionProjection,
    InvalidMaximumSelectionCount,
    AdmissionLimitExceeded,
    SourceMismatch,
    InvalidDisposition,
}

public sealed class PlaybackIsoBmffTrackAdmittedSampleSelection
{
    internal PlaybackIsoBmffTrackAdmittedSampleSelection(
        PlaybackIsoBmffTrackSampleEditAdmissionProjection source,
        int maximumSelectionCount,
        int totalAdmittedCount,
        ReadOnlyCollection<PlaybackIsoBmffTrackSampleEditAdmission> entries)
    {
        Source = source;
        MaximumSelectionCount = maximumSelectionCount;
        TotalAdmittedCount = totalAdmittedCount;
        Entries = entries;
    }

    public PlaybackIsoBmffTrackSampleEditAdmissionProjection Source { get; }

    public int MaximumSelectionCount { get; }

    public int TotalAdmittedCount { get; }

    public IReadOnlyList<PlaybackIsoBmffTrackSampleEditAdmission> Entries
    { get; }

    public bool IsTruncated => Entries.Count < TotalAdmittedCount;
}

public static class PlaybackIsoBmffTrackAdmittedSampleSelector
{
    public static PlaybackIsoBmffTrackAdmittedSampleSelectionResult Select(
        PlaybackIsoBmffTrackSampleEditAdmissionProjection? source,
        int maximumSelectionCount,
        out PlaybackIsoBmffTrackAdmittedSampleSelection? selection)
    {
        selection = null;
        if (source is null)
        {
            return PlaybackIsoBmffTrackAdmittedSampleSelectionResult
                .MissingAdmissionProjection;
        }

        if (maximumSelectionCount is < 1 or
            > PlaybackIsoBmffTrackSampleSequencePlanFactory
                .MaximumPlannedSampleCount)
        {
            return PlaybackIsoBmffTrackAdmittedSampleSelectionResult
                .InvalidMaximumSelectionCount;
        }

        if (source.Entries.Count >
            PlaybackIsoBmffTrackSampleSequencePlanFactory
                .MaximumPlannedSampleCount)
        {
            return PlaybackIsoBmffTrackAdmittedSampleSelectionResult
                .AdmissionLimitExceeded;
        }

        if (source.Entries.Count != source.Plan.Entries.Count)
        {
            return PlaybackIsoBmffTrackAdmittedSampleSelectionResult
                .SourceMismatch;
        }

        var selected = new List<PlaybackIsoBmffTrackSampleEditAdmission>(
            Math.Min(maximumSelectionCount, source.Entries.Count));
        var totalAdmittedCount = 0;
        for (var index = 0; index < source.Entries.Count; index++)
        {
            var entry = source.Entries[index];
            if (!ReferenceEquals(entry.PlanEntry, source.Plan.Entries[index]))
            {
                return PlaybackIsoBmffTrackAdmittedSampleSelectionResult
                    .SourceMismatch;
            }

            if (!Enum.IsDefined(entry.Disposition) ||
                entry.IsAdmitted != (entry.Admission is not null) ||
                entry.Admission is not null &&
                entry.Admission.SampleNumber != entry.SampleNumber)
            {
                return PlaybackIsoBmffTrackAdmittedSampleSelectionResult
                    .InvalidDisposition;
            }

            if (!entry.IsAdmitted)
            {
                continue;
            }

            totalAdmittedCount++;
            if (selected.Count < maximumSelectionCount)
            {
                selected.Add(entry);
            }
        }

        selection = new(
            source,
            maximumSelectionCount,
            totalAdmittedCount,
            selected.AsReadOnly());
        return PlaybackIsoBmffTrackAdmittedSampleSelectionResult.Selected;
    }
}
