namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffSampleSizeLookupResult
{
    Found,
    InvalidReconciliation,
    SampleNumberOutOfRange,
}

public static class PlaybackIsoBmffSampleSizeLookup
{
    public static PlaybackIsoBmffSampleSizeLookupResult Lookup(
        PlaybackIsoBmffReconciledSampleCount? reconciliation,
        PlaybackIsoBmffSampleSizeBox? sampleSize,
        PlaybackIsoBmffCompactSampleSizeBox? compactSampleSize,
        uint sampleNumber,
        out uint? size)
    {
        size = null;
        if (reconciliation is null)
        {
            return PlaybackIsoBmffSampleSizeLookupResult.InvalidReconciliation;
        }

        uint sampleCount;
        ulong totalSampleBytes;
        switch (reconciliation.SampleSizeTableKind)
        {
            case PlaybackIsoBmffSampleSizeTableKind.Standard
                when sampleSize is not null && compactSampleSize is null:
                sampleCount = sampleSize.SampleCount;
                totalSampleBytes = sampleSize.TotalSampleBytes;
                break;
            case PlaybackIsoBmffSampleSizeTableKind.Compact
                when sampleSize is null && compactSampleSize is not null:
                sampleCount = compactSampleSize.SampleCount;
                totalSampleBytes = compactSampleSize.TotalSampleBytes;
                break;
            default:
                return PlaybackIsoBmffSampleSizeLookupResult
                    .InvalidReconciliation;
        }

        if (sampleCount != reconciliation.SampleCount ||
            totalSampleBytes != reconciliation.TotalSampleBytes)
        {
            return PlaybackIsoBmffSampleSizeLookupResult.InvalidReconciliation;
        }

        if (sampleNumber == 0 || sampleNumber > reconciliation.SampleCount)
        {
            return PlaybackIsoBmffSampleSizeLookupResult.SampleNumberOutOfRange;
        }

        var index = checked((int)sampleNumber - 1);
        size = sampleSize is null
            ? compactSampleSize!.SampleSizes[index]
            : sampleSize.DefaultSampleSize != 0
                ? sampleSize.DefaultSampleSize
                : sampleSize.SampleSizes[index];
        return PlaybackIsoBmffSampleSizeLookupResult.Found;
    }
}
