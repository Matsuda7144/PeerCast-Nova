namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffChunkOffsetTableKind
{
    Standard,
    Large,
}

public enum PlaybackIsoBmffChunkTableReconciliationResult
{
    Reconciled,
    MissingSampleToChunk,
    MissingChunkOffsetTable,
    ConflictingChunkOffsetTables,
    SampleToChunkCoverageMismatch,
    SampleCountMismatch,
    MissingSampleDescription,
    SampleDescriptionIndexOutOfRange,
}

public sealed record PlaybackIsoBmffReconciledChunkTable(
    uint ChunkCount,
    uint SampleToChunkRunCount,
    PlaybackIsoBmffChunkOffsetTableKind ChunkOffsetTableKind,
    ulong TotalSampleCount,
    uint? SampleDescriptionCount = null);

public static class PlaybackIsoBmffChunkTableReconciler
{
    public static PlaybackIsoBmffChunkTableReconciliationResult Reconcile(
        PlaybackIsoBmffSampleToChunkBox? sampleToChunk,
        PlaybackIsoBmffChunkOffsetBox? chunkOffset,
        PlaybackIsoBmffChunkLargeOffsetBox? chunkLargeOffset,
        out PlaybackIsoBmffReconciledChunkTable? reconciliation)
        => ReconcileCore(
            sampleToChunk,
            chunkOffset,
            chunkLargeOffset,
            null,
            false,
            null,
            out reconciliation);

    public static PlaybackIsoBmffChunkTableReconciliationResult Reconcile(
        PlaybackIsoBmffSampleToChunkBox? sampleToChunk,
        PlaybackIsoBmffChunkOffsetBox? chunkOffset,
        PlaybackIsoBmffChunkLargeOffsetBox? chunkLargeOffset,
        uint expectedSampleCount,
        out PlaybackIsoBmffReconciledChunkTable? reconciliation)
        => ReconcileCore(
            sampleToChunk,
            chunkOffset,
            chunkLargeOffset,
            null,
            false,
            expectedSampleCount,
            out reconciliation);

    public static PlaybackIsoBmffChunkTableReconciliationResult Reconcile(
        PlaybackIsoBmffSampleToChunkBox? sampleToChunk,
        PlaybackIsoBmffChunkOffsetBox? chunkOffset,
        PlaybackIsoBmffChunkLargeOffsetBox? chunkLargeOffset,
        PlaybackIsoBmffSampleDescriptionBox? sampleDescription,
        out PlaybackIsoBmffReconciledChunkTable? reconciliation)
        => ReconcileCore(
            sampleToChunk,
            chunkOffset,
            chunkLargeOffset,
            sampleDescription,
            true,
            null,
            out reconciliation);

    public static PlaybackIsoBmffChunkTableReconciliationResult Reconcile(
        PlaybackIsoBmffSampleToChunkBox? sampleToChunk,
        PlaybackIsoBmffChunkOffsetBox? chunkOffset,
        PlaybackIsoBmffChunkLargeOffsetBox? chunkLargeOffset,
        PlaybackIsoBmffSampleDescriptionBox? sampleDescription,
        uint expectedSampleCount,
        out PlaybackIsoBmffReconciledChunkTable? reconciliation)
        => ReconcileCore(
            sampleToChunk,
            chunkOffset,
            chunkLargeOffset,
            sampleDescription,
            true,
            expectedSampleCount,
            out reconciliation);

    private static PlaybackIsoBmffChunkTableReconciliationResult ReconcileCore(
        PlaybackIsoBmffSampleToChunkBox? sampleToChunk,
        PlaybackIsoBmffChunkOffsetBox? chunkOffset,
        PlaybackIsoBmffChunkLargeOffsetBox? chunkLargeOffset,
        PlaybackIsoBmffSampleDescriptionBox? sampleDescription,
        bool requireSampleDescription,
        uint? expectedSampleCount,
        out PlaybackIsoBmffReconciledChunkTable? reconciliation)
    {
        reconciliation = null;
        if (sampleToChunk is null)
        {
            return PlaybackIsoBmffChunkTableReconciliationResult
                .MissingSampleToChunk;
        }

        if (chunkOffset is null && chunkLargeOffset is null)
        {
            return PlaybackIsoBmffChunkTableReconciliationResult
                .MissingChunkOffsetTable;
        }

        if (chunkOffset is not null && chunkLargeOffset is not null)
        {
            return PlaybackIsoBmffChunkTableReconciliationResult
                .ConflictingChunkOffsetTables;
        }

        var chunkCount = (uint)(chunkOffset?.ChunkOffsets.Count ??
            chunkLargeOffset!.ChunkOffsets.Count);
        var runCount = sampleToChunk.Entries.Count;
        if ((runCount == 0) != (chunkCount == 0) ||
            (runCount != 0 &&
                sampleToChunk.Entries[runCount - 1].FirstChunk > chunkCount))
        {
            return PlaybackIsoBmffChunkTableReconciliationResult
                .SampleToChunkCoverageMismatch;
        }

        if (requireSampleDescription && sampleDescription is null)
        {
            return PlaybackIsoBmffChunkTableReconciliationResult
                .MissingSampleDescription;
        }

        if (sampleDescription is not null)
        {
            var sampleDescriptionCount = (uint)sampleDescription.Entries.Count;
            for (var index = 0; index < runCount; index++)
            {
                if (sampleToChunk.Entries[index].SampleDescriptionIndex >
                    sampleDescriptionCount)
                {
                    return PlaybackIsoBmffChunkTableReconciliationResult
                        .SampleDescriptionIndexOutOfRange;
                }
            }
        }

        ulong totalSampleCount = 0;
        for (var index = 0; index < runCount; index++)
        {
            var entry = sampleToChunk.Entries[index];
            var nextFirstChunk = index + 1 < runCount
                ? sampleToChunk.Entries[index + 1].FirstChunk
                : chunkCount + 1;
            var chunksInRun = nextFirstChunk - entry.FirstChunk;
            totalSampleCount += (ulong)chunksInRun * entry.SamplesPerChunk;
        }

        if (expectedSampleCount is { } expected &&
            totalSampleCount != expected)
        {
            return PlaybackIsoBmffChunkTableReconciliationResult
                .SampleCountMismatch;
        }

        reconciliation = new(
            chunkCount,
            (uint)runCount,
            chunkOffset is null
                ? PlaybackIsoBmffChunkOffsetTableKind.Large
                : PlaybackIsoBmffChunkOffsetTableKind.Standard,
            totalSampleCount,
            sampleDescription is null
                ? null
                : (uint)sampleDescription.Entries.Count);
        return PlaybackIsoBmffChunkTableReconciliationResult.Reconciled;
    }
}
