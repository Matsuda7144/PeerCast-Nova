namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffSampleChunkLookupResult
{
    Found,
    InvalidReconciliation,
    SampleNumberOutOfRange,
}

public readonly record struct PlaybackIsoBmffSampleChunkLocation(
    uint RunIndex,
    uint ChunkNumber,
    ulong FirstSampleNumberInChunk,
    uint SampleIndexWithinChunk,
    uint SamplesPerChunk,
    uint SampleDescriptionIndex);

public static class PlaybackIsoBmffSampleChunkLookup
{
    public static PlaybackIsoBmffSampleChunkLookupResult Lookup(
        PlaybackIsoBmffSampleToChunkBox? sampleToChunk,
        PlaybackIsoBmffReconciledChunkTable? reconciliation,
        ulong sampleNumber,
        out PlaybackIsoBmffSampleChunkLocation? location)
    {
        location = null;
        if (reconciliation is null)
        {
            return PlaybackIsoBmffSampleChunkLookupResult
                .InvalidReconciliation;
        }

        var validation = PlaybackIsoBmffChunkRunLookup.Lookup(
            sampleToChunk,
            reconciliation,
            1,
            out _);
        if ((reconciliation.ChunkCount == 0 &&
                validation !=
                    PlaybackIsoBmffChunkRunLookupResult.ChunkNumberOutOfRange) ||
            (reconciliation.ChunkCount != 0 &&
                validation != PlaybackIsoBmffChunkRunLookupResult.Found))
        {
            return PlaybackIsoBmffSampleChunkLookupResult
                .InvalidReconciliation;
        }

        if (sampleNumber == 0 ||
            sampleNumber > reconciliation.TotalSampleCount)
        {
            return PlaybackIsoBmffSampleChunkLookupResult.SampleNumberOutOfRange;
        }

        var entries = sampleToChunk!.Entries;
        ulong firstSampleNumberInRun = 1;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var nextFirstChunk = index + 1 < entries.Count
                ? entries[index + 1].FirstChunk
                : reconciliation.ChunkCount + 1;
            var chunkCount = nextFirstChunk - entry.FirstChunk;
            var sampleCount = (ulong)chunkCount * entry.SamplesPerChunk;
            if (sampleNumber <= firstSampleNumberInRun + sampleCount - 1)
            {
                var sampleOffsetInRun = sampleNumber - firstSampleNumberInRun;
                var chunkOffset = checked((uint)(
                    sampleOffsetInRun / entry.SamplesPerChunk));
                var sampleIndexWithinChunk = checked((uint)(
                    sampleOffsetInRun % entry.SamplesPerChunk));
                location = new(
                    (uint)index,
                    entry.FirstChunk + chunkOffset,
                    firstSampleNumberInRun +
                        ((ulong)chunkOffset * entry.SamplesPerChunk),
                    sampleIndexWithinChunk,
                    entry.SamplesPerChunk,
                    entry.SampleDescriptionIndex);
                return PlaybackIsoBmffSampleChunkLookupResult.Found;
            }

            firstSampleNumberInRun += sampleCount;
        }

        return PlaybackIsoBmffSampleChunkLookupResult.InvalidReconciliation;
    }
}
