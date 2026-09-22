namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFmp4LiveRandomAccessSampleClassificationTests
{
    [Fact]
    public void Classify_IndependentSyncSampleIsIndependentlyDecodable()
    {
        const uint sampleFlags = 0x02000000;

        var result = PlaybackFmp4LiveRandomAccessSampleClassifier.Classify(
            sampleFlags,
            out var classification);

        Assert.Equal(
            PlaybackFmp4LiveRandomAccessSampleClassificationResult.Classified,
            result);
        Assert.Equal(sampleFlags, classification.SampleFlags);
        Assert.Equal(
            PlaybackIsoBmffSampleDependsOn.Independent,
            classification.DependsOn);
        Assert.False(classification.IsNonSyncSample);
        Assert.Equal(
            PlaybackFmp4LiveRandomAccessSampleKind.IndependentlyDecodable,
            classification.Kind);
        Assert.True(classification.IsIndependentlyDecodable);
    }

    [Theory]
    [InlineData(0x01000000U)]
    [InlineData(0x02010000U)]
    public void Classify_DependentOrNonSyncSampleIsNotIndependentlyDecodable(
        uint sampleFlags)
    {
        var result = PlaybackFmp4LiveRandomAccessSampleClassifier.Classify(
            sampleFlags,
            out var classification);

        Assert.Equal(
            PlaybackFmp4LiveRandomAccessSampleClassificationResult.Classified,
            result);
        Assert.Equal(
            PlaybackFmp4LiveRandomAccessSampleKind.NotIndependentlyDecodable,
            classification.Kind);
        Assert.False(classification.IsIndependentlyDecodable);
    }

    [Fact]
    public void Classify_UnknownDependencyIsIndeterminate()
    {
        var result = PlaybackFmp4LiveRandomAccessSampleClassifier.Classify(
            0,
            out var classification);

        Assert.Equal(
            PlaybackFmp4LiveRandomAccessSampleClassificationResult.Classified,
            result);
        Assert.Equal(
            PlaybackIsoBmffSampleDependsOn.Unknown,
            classification.DependsOn);
        Assert.False(classification.IsNonSyncSample);
        Assert.Equal(
            PlaybackFmp4LiveRandomAccessSampleKind.Indeterminate,
            classification.Kind);
        Assert.False(classification.IsIndependentlyDecodable);
    }

    [Fact]
    public void Classify_ReservedDependencyValueIsRejected()
    {
        var result = PlaybackFmp4LiveRandomAccessSampleClassifier.Classify(
            0x03000000,
            out var classification);

        Assert.Equal(
            PlaybackFmp4LiveRandomAccessSampleClassificationResult
                .ReservedDependsOnValue,
            result);
        Assert.Equal(default, classification);
    }
}
