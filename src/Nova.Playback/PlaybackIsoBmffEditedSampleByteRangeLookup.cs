namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffEditedSampleByteRangeLookupResult
{
    Found,
    MissingAdmission,
    InvalidReconciliation,
    SampleNumberOutOfRange,
    OffsetOverflow,
}

public sealed class PlaybackIsoBmffEditedSampleByteRange
{
    internal PlaybackIsoBmffEditedSampleByteRange(
        PlaybackIsoBmffEditedSampleAdmission admission,
        PlaybackIsoBmffSampleByteRange range,
        PlaybackIsoBmffSampleChunkLocation chunkLocation)
    {
        Admission = admission;
        Range = range;
        ChunkLocation = chunkLocation;
    }

    public PlaybackIsoBmffEditedSampleAdmission Admission { get; }

    public ulong SampleNumber => Admission.SampleNumber;

    public PlaybackIsoBmffSampleByteRange Range { get; }

    public PlaybackIsoBmffSampleChunkLocation ChunkLocation { get; }

    public uint SampleDescriptionIndex =>
        ChunkLocation.SampleDescriptionIndex;
}

public static class PlaybackIsoBmffEditedSampleByteRangeLookup
{
    public static PlaybackIsoBmffEditedSampleByteRangeLookupResult Lookup(
        PlaybackIsoBmffEditedSampleAdmission? admission,
        PlaybackIsoBmffSampleToChunkBox? sampleToChunk,
        PlaybackIsoBmffReconciledChunkTable? chunkReconciliation,
        PlaybackIsoBmffChunkOffsetBox? chunkOffset,
        PlaybackIsoBmffChunkLargeOffsetBox? chunkLargeOffset,
        PlaybackIsoBmffReconciledSampleCount? sampleReconciliation,
        PlaybackIsoBmffSampleSizeBox? sampleSize,
        PlaybackIsoBmffCompactSampleSizeBox? compactSampleSize,
        out PlaybackIsoBmffEditedSampleByteRange? editedRange)
    {
        editedRange = null;
        if (admission is null)
        {
            return PlaybackIsoBmffEditedSampleByteRangeLookupResult
                .MissingAdmission;
        }

        var rangeResult = PlaybackIsoBmffSampleByteRangeLookup.Lookup(
            sampleToChunk,
            chunkReconciliation,
            chunkOffset,
            chunkLargeOffset,
            sampleReconciliation,
            sampleSize,
            compactSampleSize,
            admission.SampleNumber,
            out var range,
            out var chunkLocation);
        if (rangeResult != PlaybackIsoBmffSampleByteRangeLookupResult.Found ||
            range is null ||
            chunkLocation is null)
        {
            return rangeResult switch
            {
                PlaybackIsoBmffSampleByteRangeLookupResult
                    .SampleNumberOutOfRange =>
                    PlaybackIsoBmffEditedSampleByteRangeLookupResult
                        .SampleNumberOutOfRange,
                PlaybackIsoBmffSampleByteRangeLookupResult.OffsetOverflow =>
                    PlaybackIsoBmffEditedSampleByteRangeLookupResult
                        .OffsetOverflow,
                _ => PlaybackIsoBmffEditedSampleByteRangeLookupResult
                    .InvalidReconciliation,
            };
        }

        editedRange = new(admission, range.Value, chunkLocation.Value);
        return PlaybackIsoBmffEditedSampleByteRangeLookupResult.Found;
    }
}
