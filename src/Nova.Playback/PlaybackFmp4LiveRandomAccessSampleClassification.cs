namespace PeerCast.Nova.Playback;

public enum PlaybackFmp4LiveRandomAccessSampleClassificationResult
{
    Classified,
    ReservedDependsOnValue,
}

public enum PlaybackFmp4LiveRandomAccessSampleKind
{
    IndependentlyDecodable,
    NotIndependentlyDecodable,
    Indeterminate,
}

public readonly record struct PlaybackFmp4LiveRandomAccessSampleClassification(
    uint SampleFlags,
    PlaybackIsoBmffSampleDependsOn DependsOn,
    bool IsNonSyncSample,
    PlaybackFmp4LiveRandomAccessSampleKind Kind)
{
    public bool IsIndependentlyDecodable =>
        Kind == PlaybackFmp4LiveRandomAccessSampleKind.IndependentlyDecodable;
}

public static class PlaybackFmp4LiveRandomAccessSampleClassifier
{
    private const int SampleDependsOnShift = 24;
    private const uint SampleDependsOnMask = 0x03;
    private const uint SampleIsNonSyncSampleMask = 0x00010000;

    public static PlaybackFmp4LiveRandomAccessSampleClassificationResult Classify(
        uint sampleFlags,
        out PlaybackFmp4LiveRandomAccessSampleClassification classification)
    {
        var dependsOnValue =
            (sampleFlags >> SampleDependsOnShift) & SampleDependsOnMask;
        if (dependsOnValue == 3)
        {
            classification = default;
            return PlaybackFmp4LiveRandomAccessSampleClassificationResult
                .ReservedDependsOnValue;
        }

        var dependsOn = (PlaybackIsoBmffSampleDependsOn)dependsOnValue;
        var isNonSyncSample =
            (sampleFlags & SampleIsNonSyncSampleMask) != 0;
        var kind = isNonSyncSample ||
            dependsOn == PlaybackIsoBmffSampleDependsOn.DependsOnOthers
            ? PlaybackFmp4LiveRandomAccessSampleKind.NotIndependentlyDecodable
            : dependsOn == PlaybackIsoBmffSampleDependsOn.Independent
                ? PlaybackFmp4LiveRandomAccessSampleKind.IndependentlyDecodable
                : PlaybackFmp4LiveRandomAccessSampleKind.Indeterminate;

        classification = new(
            sampleFlags,
            dependsOn,
            isNonSyncSample,
            kind);
        return PlaybackFmp4LiveRandomAccessSampleClassificationResult.Classified;
    }
}
