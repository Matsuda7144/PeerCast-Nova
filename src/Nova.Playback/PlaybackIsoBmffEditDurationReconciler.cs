namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffEditDurationReconciliationResult
{
    Reconciled,
    MissingEditList,
    MissingTrackHeader,
    UnknownTrackDuration,
    DurationMismatch,
}

public readonly record struct PlaybackIsoBmffReconciledEditDuration(
    uint TrackId,
    ulong DurationUnits,
    int EditCount);

public static class PlaybackIsoBmffEditDurationReconciler
{
    public static PlaybackIsoBmffEditDurationReconciliationResult Reconcile(
        PlaybackIsoBmffEditListBox? editList,
        PlaybackIsoBmffTrackHeaderIdentityBox? trackHeader,
        out PlaybackIsoBmffReconciledEditDuration? reconciliation)
    {
        reconciliation = null;
        if (editList is null)
        {
            return PlaybackIsoBmffEditDurationReconciliationResult
                .MissingEditList;
        }

        if (trackHeader is null)
        {
            return PlaybackIsoBmffEditDurationReconciliationResult
                .MissingTrackHeader;
        }

        if (trackHeader.DurationUnits is not { } trackDurationUnits)
        {
            return PlaybackIsoBmffEditDurationReconciliationResult
                .UnknownTrackDuration;
        }

        if (editList.TotalSegmentDurationUnits != trackDurationUnits)
        {
            return PlaybackIsoBmffEditDurationReconciliationResult
                .DurationMismatch;
        }

        reconciliation = new(
            trackHeader.TrackId,
            trackDurationUnits,
            editList.Entries.Count);
        return PlaybackIsoBmffEditDurationReconciliationResult.Reconciled;
    }
}
