namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffSampleSizeTableKind
{
    Standard,
    Compact,
}

public enum PlaybackIsoBmffSampleCountReconciliationResult
{
    Reconciled,
    MissingTimeToSample,
    MissingSampleSizeTable,
    ConflictingSampleSizeTables,
    SampleCountMismatch,
    CompositionSampleCountMismatch,
    DependencySampleCountMismatch,
    SyncSampleNumberOutOfRange,
    PartialSyncSampleNumberOutOfRange,
}

public sealed record PlaybackIsoBmffReconciledSampleCount(
    uint SampleCount,
    ulong TotalSampleBytes,
    PlaybackIsoBmffSampleSizeTableKind SampleSizeTableKind,
    bool HasCompositionOffsets = false,
    bool HasSampleDependencies = false,
    bool HasSyncSampleTable = false,
    bool HasPartialSyncSampleTable = false);

public static class PlaybackIsoBmffSampleCountReconciler
{
    public static PlaybackIsoBmffSampleCountReconciliationResult Reconcile(
        PlaybackIsoBmffTimeToSampleBox? timeToSample,
        PlaybackIsoBmffSampleSizeBox? sampleSize,
        PlaybackIsoBmffCompactSampleSizeBox? compactSampleSize,
        out PlaybackIsoBmffReconciledSampleCount? reconciliation)
        => Reconcile(
            timeToSample,
            sampleSize,
            compactSampleSize,
            null,
            null,
            null,
            null,
            out reconciliation);

    public static PlaybackIsoBmffSampleCountReconciliationResult Reconcile(
        PlaybackIsoBmffTimeToSampleBox? timeToSample,
        PlaybackIsoBmffSampleSizeBox? sampleSize,
        PlaybackIsoBmffCompactSampleSizeBox? compactSampleSize,
        PlaybackIsoBmffCompositionTimeToSampleBox? compositionTimeToSample,
        out PlaybackIsoBmffReconciledSampleCount? reconciliation)
        => Reconcile(
            timeToSample,
            sampleSize,
            compactSampleSize,
            compositionTimeToSample,
            null,
            null,
            null,
            out reconciliation);

    public static PlaybackIsoBmffSampleCountReconciliationResult Reconcile(
        PlaybackIsoBmffTimeToSampleBox? timeToSample,
        PlaybackIsoBmffSampleSizeBox? sampleSize,
        PlaybackIsoBmffCompactSampleSizeBox? compactSampleSize,
        PlaybackIsoBmffCompositionTimeToSampleBox? compositionTimeToSample,
        PlaybackIsoBmffSampleDependencyTypeBox? sampleDependencyType,
        out PlaybackIsoBmffReconciledSampleCount? reconciliation)
        => Reconcile(
            timeToSample,
            sampleSize,
            compactSampleSize,
            compositionTimeToSample,
            sampleDependencyType,
            null,
            null,
            out reconciliation);

    public static PlaybackIsoBmffSampleCountReconciliationResult Reconcile(
        PlaybackIsoBmffTimeToSampleBox? timeToSample,
        PlaybackIsoBmffSampleSizeBox? sampleSize,
        PlaybackIsoBmffCompactSampleSizeBox? compactSampleSize,
        PlaybackIsoBmffCompositionTimeToSampleBox? compositionTimeToSample,
        PlaybackIsoBmffSampleDependencyTypeBox? sampleDependencyType,
        PlaybackIsoBmffSyncSampleBox? syncSample,
        out PlaybackIsoBmffReconciledSampleCount? reconciliation)
        => Reconcile(
            timeToSample,
            sampleSize,
            compactSampleSize,
            compositionTimeToSample,
            sampleDependencyType,
            syncSample,
            null,
            out reconciliation);

    public static PlaybackIsoBmffSampleCountReconciliationResult Reconcile(
        PlaybackIsoBmffTimeToSampleBox? timeToSample,
        PlaybackIsoBmffSampleSizeBox? sampleSize,
        PlaybackIsoBmffCompactSampleSizeBox? compactSampleSize,
        PlaybackIsoBmffCompositionTimeToSampleBox? compositionTimeToSample,
        PlaybackIsoBmffSampleDependencyTypeBox? sampleDependencyType,
        PlaybackIsoBmffSyncSampleBox? syncSample,
        PlaybackIsoBmffPartialSyncSampleBox? partialSyncSample,
        out PlaybackIsoBmffReconciledSampleCount? reconciliation)
    {
        reconciliation = null;
        if (timeToSample is null)
        {
            return PlaybackIsoBmffSampleCountReconciliationResult
                .MissingTimeToSample;
        }

        if (sampleSize is null && compactSampleSize is null)
        {
            return PlaybackIsoBmffSampleCountReconciliationResult
                .MissingSampleSizeTable;
        }

        if (sampleSize is not null && compactSampleSize is not null)
        {
            return PlaybackIsoBmffSampleCountReconciliationResult
                .ConflictingSampleSizeTables;
        }

        var sampleCount = sampleSize?.SampleCount ??
            compactSampleSize!.SampleCount;
        if (timeToSample.TotalSampleCount != sampleCount)
        {
            return PlaybackIsoBmffSampleCountReconciliationResult
                .SampleCountMismatch;
        }

        if (compositionTimeToSample is not null &&
            compositionTimeToSample.TotalSampleCount != sampleCount)
        {
            return PlaybackIsoBmffSampleCountReconciliationResult
                .CompositionSampleCountMismatch;
        }

        if (sampleDependencyType is not null &&
            (uint)sampleDependencyType.Entries.Count != sampleCount)
        {
            return PlaybackIsoBmffSampleCountReconciliationResult
                .DependencySampleCountMismatch;
        }

        if (syncSample is not null &&
            syncSample.SampleNumbers.Count != 0 &&
            syncSample.SampleNumbers[syncSample.SampleNumbers.Count - 1] >
                sampleCount)
        {
            return PlaybackIsoBmffSampleCountReconciliationResult
                .SyncSampleNumberOutOfRange;
        }

        if (partialSyncSample is not null &&
            partialSyncSample.SampleNumbers.Count != 0 &&
            partialSyncSample.SampleNumbers[
                partialSyncSample.SampleNumbers.Count - 1] > sampleCount)
        {
            return PlaybackIsoBmffSampleCountReconciliationResult
                .PartialSyncSampleNumberOutOfRange;
        }

        reconciliation = new(
            sampleCount,
            sampleSize?.TotalSampleBytes ??
                compactSampleSize!.TotalSampleBytes,
            sampleSize is null
                ? PlaybackIsoBmffSampleSizeTableKind.Compact
                : PlaybackIsoBmffSampleSizeTableKind.Standard,
            compositionTimeToSample is not null,
            sampleDependencyType is not null,
            syncSample is not null,
            partialSyncSample is not null);
        return PlaybackIsoBmffSampleCountReconciliationResult.Reconciled;
    }
}
