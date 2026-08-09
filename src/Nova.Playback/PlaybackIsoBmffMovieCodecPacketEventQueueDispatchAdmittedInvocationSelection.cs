using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchAdmittedInvocationSelectionResult
{
    Selected,
    MissingAdmissionProjection,
    InvalidMaximumSelectionCount,
    AdmissionLimitExceeded,
    SourceMismatch,
    InvalidDisposition,
}

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchAdmittedInvocationSelection
{
    internal
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchAdmittedInvocationSelection(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionProjection
                source,
            int maximumSelectionCount,
            int totalAdmittedCount,
            ReadOnlyCollection<
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmission>
                admissions)
    {
        Source = source;
        MaximumSelectionCount = maximumSelectionCount;
        TotalAdmittedCount = totalAdmittedCount;
        Admissions = admissions;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionProjection
        Source
    { get; }

    public int MaximumSelectionCount { get; }

    public int TotalAdmittedCount { get; }

    public IReadOnlyList<
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmission>
        Admissions
    { get; }

    public bool IsTruncated => Admissions.Count < TotalAdmittedCount;
}

public static class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchAdmittedInvocationSelector
{
    public static
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchAdmittedInvocationSelectionResult
        Select(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionProjection?
                source,
            int maximumSelectionCount,
            out PlaybackIsoBmffMovieCodecPacketEventQueueDispatchAdmittedInvocationSelection?
                selection)
    {
        selection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchAdmittedInvocationSelectionResult
                    .MissingAdmissionProjection;
        }

        if (maximumSelectionCount <= 0 ||
            maximumSelectionCount >
                PlaybackIsoBmffMovieCodecPacketEventProjector.MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchAdmittedInvocationSelectionResult
                    .InvalidMaximumSelectionCount;
        }

        if (source.Admissions.Count >
            PlaybackIsoBmffMovieCodecPacketEventProjector.MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchAdmittedInvocationSelectionResult
                    .AdmissionLimitExceeded;
        }

        if (source.Admissions.Count != source.Source.Intents.Count)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchAdmittedInvocationSelectionResult
                    .SourceMismatch;
        }

        var selected = new List<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmission>(
                Math.Min(maximumSelectionCount, source.Admissions.Count));
        var totalAdmittedCount = 0;
        for (var index = 0; index < source.Admissions.Count; index++)
        {
            var admission = source.Admissions[index];
            if (!ReferenceEquals(admission.Intent, source.Source.Intents[index]))
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchAdmittedInvocationSelectionResult
                        .SourceMismatch;
            }

            if (!Enum.IsDefined(admission.Disposition))
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchAdmittedInvocationSelectionResult
                        .InvalidDisposition;
            }

            if (!admission.IsAdmitted)
            {
                continue;
            }

            totalAdmittedCount++;
            if (selected.Count < maximumSelectionCount)
            {
                selected.Add(admission);
            }
        }

        selection = new(
            source,
            maximumSelectionCount,
            totalAdmittedCount,
            selected.AsReadOnly());
        return
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchAdmittedInvocationSelectionResult
                .Selected;
    }
}
