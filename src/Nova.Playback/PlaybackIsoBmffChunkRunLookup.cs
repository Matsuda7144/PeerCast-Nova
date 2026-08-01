namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffChunkRunLookupResult
{
    Found,
    InvalidReconciliation,
    ChunkNumberOutOfRange,
}

public readonly record struct PlaybackIsoBmffChunkRun(
    uint RunIndex,
    uint FirstChunk,
    uint LastChunk,
    uint SamplesPerChunk,
    uint SampleDescriptionIndex);

public static class PlaybackIsoBmffChunkRunLookup
{
    public static PlaybackIsoBmffChunkRunLookupResult Lookup(
        PlaybackIsoBmffSampleToChunkBox? sampleToChunk,
        PlaybackIsoBmffReconciledChunkTable? reconciliation,
        uint chunkNumber,
        out PlaybackIsoBmffChunkRun? run)
    {
        run = null;
        if (sampleToChunk is null || reconciliation is null ||
            reconciliation.ChunkOffsetTableKind is not
                PlaybackIsoBmffChunkOffsetTableKind.Standard and not
                PlaybackIsoBmffChunkOffsetTableKind.Large)
        {
            return PlaybackIsoBmffChunkRunLookupResult.InvalidReconciliation;
        }

        var entries = sampleToChunk.Entries;
        if ((uint)entries.Count != reconciliation.SampleToChunkRunCount ||
            (entries.Count == 0) != (reconciliation.ChunkCount == 0) ||
            (entries.Count != 0 &&
                entries[entries.Count - 1].FirstChunk >
                    reconciliation.ChunkCount))
        {
            return PlaybackIsoBmffChunkRunLookupResult.InvalidReconciliation;
        }

        ulong totalSampleCount = 0;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (reconciliation.SampleDescriptionCount is { } descriptionCount &&
                entry.SampleDescriptionIndex > descriptionCount)
            {
                return PlaybackIsoBmffChunkRunLookupResult.InvalidReconciliation;
            }

            var nextFirstChunk = index + 1 < entries.Count
                ? entries[index + 1].FirstChunk
                : reconciliation.ChunkCount + 1;
            totalSampleCount +=
                (ulong)(nextFirstChunk - entry.FirstChunk) *
                entry.SamplesPerChunk;
        }

        if (totalSampleCount != reconciliation.TotalSampleCount)
        {
            return PlaybackIsoBmffChunkRunLookupResult.InvalidReconciliation;
        }

        if (chunkNumber == 0 || chunkNumber > reconciliation.ChunkCount)
        {
            return PlaybackIsoBmffChunkRunLookupResult.ChunkNumberOutOfRange;
        }

        var low = 0;
        var high = entries.Count - 1;
        var foundIndex = 0;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (entries[middle].FirstChunk <= chunkNumber)
            {
                foundIndex = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        var found = entries[foundIndex];
        var lastChunk = foundIndex + 1 < entries.Count
            ? entries[foundIndex + 1].FirstChunk - 1
            : reconciliation.ChunkCount;
        run = new(
            (uint)foundIndex,
            found.FirstChunk,
            lastChunk,
            found.SamplesPerChunk,
            found.SampleDescriptionIndex);
        return PlaybackIsoBmffChunkRunLookupResult.Found;
    }
}
