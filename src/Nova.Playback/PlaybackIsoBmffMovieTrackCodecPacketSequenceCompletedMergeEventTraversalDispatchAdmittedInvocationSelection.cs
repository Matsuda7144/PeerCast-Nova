using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionResult
{
    Selected,
    MissingAdmissionProjection,
    InvalidMaximumSelectionCount,
    AdmissionLimitExceeded,
    SourceMismatch,
    InvalidDisposition,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelection
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelection(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionProjection
                source,
            int maximumSelectionCount,
            int totalAdmittedCount,
            ReadOnlyCollection<
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmission>
                admissions)
    {
        Source = source;
        MaximumSelectionCount = maximumSelectionCount;
        TotalAdmittedCount = totalAdmittedCount;
        Admissions = admissions;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionProjection
            Source
    { get; }

    public int MaximumSelectionCount { get; }

    public int TotalAdmittedCount { get; }

    public IReadOnlyList<
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmission>
        Admissions
    { get; }

    public bool IsTruncated => Admissions.Count < TotalAdmittedCount;
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionResult
            Select(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionProjection?
                    source,
                int maximumSelectionCount,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelection?
                    selection)
    {
        selection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionResult
                    .MissingAdmissionProjection;
        }

        if (maximumSelectionCount <= 0 ||
            maximumSelectionCount >
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjector
                    .MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionResult
                    .InvalidMaximumSelectionCount;
        }

        if (source.Admissions.Count >
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjector
                .MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionResult
                    .AdmissionLimitExceeded;
        }

        if (source.Admissions.Count != source.Source.Intents.Count)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionResult
                    .SourceMismatch;
        }

        var selected = new List<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmission>(
                Math.Min(maximumSelectionCount, source.Admissions.Count));
        var totalAdmittedCount = 0;
        for (var index = 0; index < source.Admissions.Count; index++)
        {
            var admission = source.Admissions[index];
            if (!ReferenceEquals(admission.Intent, source.Source.Intents[index]))
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionResult
                        .SourceMismatch;
            }

            if (!Enum.IsDefined(admission.Disposition))
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionResult
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
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionResult
                .Selected;
    }
}
