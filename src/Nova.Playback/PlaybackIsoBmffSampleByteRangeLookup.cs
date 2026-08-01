namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffSampleByteRangeLookupResult
{
    Found,
    InvalidReconciliation,
    SampleNumberOutOfRange,
    OffsetOverflow,
}

public readonly record struct PlaybackIsoBmffSampleByteRange(
    ulong Offset,
    uint Size,
    ulong EndOffsetExclusive);

public static class PlaybackIsoBmffSampleByteRangeLookup
{
    public static PlaybackIsoBmffSampleByteRangeLookupResult Lookup(
        PlaybackIsoBmffSampleToChunkBox? sampleToChunk,
        PlaybackIsoBmffReconciledChunkTable? chunkReconciliation,
        PlaybackIsoBmffChunkOffsetBox? chunkOffset,
        PlaybackIsoBmffChunkLargeOffsetBox? chunkLargeOffset,
        PlaybackIsoBmffReconciledSampleCount? sampleReconciliation,
        PlaybackIsoBmffSampleSizeBox? sampleSize,
        PlaybackIsoBmffCompactSampleSizeBox? compactSampleSize,
        ulong sampleNumber,
        out PlaybackIsoBmffSampleByteRange? range) =>
        Lookup(
            sampleToChunk,
            chunkReconciliation,
            chunkOffset,
            chunkLargeOffset,
            sampleReconciliation,
            sampleSize,
            compactSampleSize,
            sampleNumber,
            out range,
            out _);

    public static PlaybackIsoBmffSampleByteRangeLookupResult Lookup(
        PlaybackIsoBmffSampleToChunkBox? sampleToChunk,
        PlaybackIsoBmffReconciledChunkTable? chunkReconciliation,
        PlaybackIsoBmffChunkOffsetBox? chunkOffset,
        PlaybackIsoBmffChunkLargeOffsetBox? chunkLargeOffset,
        PlaybackIsoBmffReconciledSampleCount? sampleReconciliation,
        PlaybackIsoBmffSampleSizeBox? sampleSize,
        PlaybackIsoBmffCompactSampleSizeBox? compactSampleSize,
        ulong sampleNumber,
        out PlaybackIsoBmffSampleByteRange? range,
        out PlaybackIsoBmffSampleChunkLocation? chunkLocation)
    {
        range = null;
        chunkLocation = null;
        if (chunkReconciliation is null || sampleReconciliation is null ||
            chunkReconciliation.TotalSampleCount !=
                sampleReconciliation.SampleCount)
        {
            return PlaybackIsoBmffSampleByteRangeLookupResult
                .InvalidReconciliation;
        }

        var locationResult = PlaybackIsoBmffSampleChunkLookup.Lookup(
            sampleToChunk,
            chunkReconciliation,
            sampleNumber,
            out var location);
        if (locationResult ==
            PlaybackIsoBmffSampleChunkLookupResult.SampleNumberOutOfRange)
        {
            return PlaybackIsoBmffSampleByteRangeLookupResult
                .SampleNumberOutOfRange;
        }

        if (locationResult != PlaybackIsoBmffSampleChunkLookupResult.Found ||
            location is null)
        {
            return PlaybackIsoBmffSampleByteRangeLookupResult
                .InvalidReconciliation;
        }

        chunkLocation = location;

        var boundedSampleNumber = checked((uint)sampleNumber);
        if (PlaybackIsoBmffSampleSizeLookup.Lookup(
                sampleReconciliation,
                sampleSize,
                compactSampleSize,
                boundedSampleNumber,
                out var size) != PlaybackIsoBmffSampleSizeLookupResult.Found ||
            size is null ||
            PlaybackIsoBmffChunkOffsetLookup.Lookup(
                chunkReconciliation,
                chunkOffset,
                chunkLargeOffset,
                location.Value.ChunkNumber,
                out var baseOffset) !=
                    PlaybackIsoBmffChunkOffsetLookupResult.Found ||
            baseOffset is null)
        {
            return PlaybackIsoBmffSampleByteRangeLookupResult
                .InvalidReconciliation;
        }

        ulong precedingBytes = 0;
        if (sampleSize is not null && sampleSize.DefaultSampleSize != 0)
        {
            precedingBytes =
                (ulong)location.Value.SampleIndexWithinChunk *
                sampleSize.DefaultSampleSize;
        }
        else
        {
            var firstIndex = checked((int)(
                location.Value.FirstSampleNumberInChunk - 1));
            var targetIndex = checked((int)sampleNumber - 1);
            for (var index = firstIndex; index < targetIndex; index++)
            {
                precedingBytes += sampleSize is null
                    ? compactSampleSize!.SampleSizes[index]
                    : sampleSize.SampleSizes[index];
            }
        }

        if (baseOffset.Value > ulong.MaxValue - precedingBytes)
        {
            chunkLocation = null;
            return PlaybackIsoBmffSampleByteRangeLookupResult.OffsetOverflow;
        }

        var offset = baseOffset.Value + precedingBytes;
        if (offset > ulong.MaxValue - size.Value)
        {
            chunkLocation = null;
            return PlaybackIsoBmffSampleByteRangeLookupResult.OffsetOverflow;
        }

        range = new(offset, size.Value, offset + size.Value);
        return PlaybackIsoBmffSampleByteRangeLookupResult.Found;
    }
}
