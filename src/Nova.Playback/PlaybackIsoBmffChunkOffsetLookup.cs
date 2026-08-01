namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffChunkOffsetLookupResult
{
    Found,
    InvalidReconciliation,
    ChunkNumberOutOfRange,
}

public static class PlaybackIsoBmffChunkOffsetLookup
{
    public static PlaybackIsoBmffChunkOffsetLookupResult Lookup(
        PlaybackIsoBmffReconciledChunkTable? reconciliation,
        PlaybackIsoBmffChunkOffsetBox? chunkOffset,
        PlaybackIsoBmffChunkLargeOffsetBox? chunkLargeOffset,
        uint chunkNumber,
        out ulong? offset)
    {
        offset = null;
        if (reconciliation is null)
        {
            return PlaybackIsoBmffChunkOffsetLookupResult
                .InvalidReconciliation;
        }

        int entryCount;
        switch (reconciliation.ChunkOffsetTableKind)
        {
            case PlaybackIsoBmffChunkOffsetTableKind.Standard
                when chunkOffset is not null && chunkLargeOffset is null:
                entryCount = chunkOffset.ChunkOffsets.Count;
                break;
            case PlaybackIsoBmffChunkOffsetTableKind.Large
                when chunkOffset is null && chunkLargeOffset is not null:
                entryCount = chunkLargeOffset.ChunkOffsets.Count;
                break;
            default:
                return PlaybackIsoBmffChunkOffsetLookupResult
                    .InvalidReconciliation;
        }

        if ((uint)entryCount != reconciliation.ChunkCount)
        {
            return PlaybackIsoBmffChunkOffsetLookupResult
                .InvalidReconciliation;
        }

        if (chunkNumber == 0 || chunkNumber > reconciliation.ChunkCount)
        {
            return PlaybackIsoBmffChunkOffsetLookupResult
                .ChunkNumberOutOfRange;
        }

        var index = checked((int)chunkNumber - 1);
        offset = chunkOffset is null
            ? chunkLargeOffset!.ChunkOffsets[index]
            : chunkOffset.ChunkOffsets[index];
        return PlaybackIsoBmffChunkOffsetLookupResult.Found;
    }
}
