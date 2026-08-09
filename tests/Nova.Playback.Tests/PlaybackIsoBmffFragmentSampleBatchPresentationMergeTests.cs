using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffFragmentSampleBatchPresentationMergeTests
{
    [Fact]
    public void Merge_OrdersAcrossBatchesByExactPresentationAndDecodeKeys()
    {
        var first = Batch(1, decode: 0, presentation: 30);
        var second = Batch(2, decode: 10, presentation: 10);
        var chain = Chain(first, second);

        Assert.Equal(
            PlaybackIsoBmffFragmentSampleBatchPresentationMergeResult.Merged,
            PlaybackIsoBmffFragmentSampleBatchPresentationMerger.Merge(
                chain,
                out var merge));
        var merged = Assert.IsType<
            PlaybackIsoBmffFragmentSampleBatchPresentationMerge>(merge);
        Assert.Same(chain, merged.Source);
        Assert.Equal([2U, 1U], merged.Samples.Select(
            sample => sample.SequenceNumber));
        Assert.Equal([1, 0], merged.Samples.Select(
            sample => sample.BatchIndex));
        Assert.IsNotType<PlaybackIsoBmffFragmentBatchPresentationSample[]>(
            merged.Samples);
    }

    [Fact]
    public void Merge_EqualTimesUseBatchThenSampleIndexTieBreaks()
    {
        var first = Batch(1, decode: 0, presentation: 0, sampleIndex: 0);
        var second = Batch(2, decode: 0, presentation: 0, sampleIndex: 0);

        PlaybackIsoBmffFragmentSampleBatchPresentationMerger.Merge(
            Chain(first, second),
            out var merge);
        Assert.Equal(
            [0, 1],
            Assert.IsType<PlaybackIsoBmffFragmentSampleBatchPresentationMerge>(
                merge).Samples.Select(sample => sample.BatchIndex));
    }

    [Fact]
    public void Merge_EmptyBatchChainIsValid()
    {
        var continuity = new PlaybackIsoBmffFragmentContinuityChain(
            null,
            new ReadOnlyCollection<PlaybackIsoBmffFragmentContinuityBinding>([]));
        var chain = new PlaybackIsoBmffFragmentSampleBatchChain(
            continuity,
            null,
            null,
            new ReadOnlyCollection<PlaybackIsoBmffFragmentSampleBatch>([]));

        Assert.Equal(
            PlaybackIsoBmffFragmentSampleBatchPresentationMergeResult.Merged,
            PlaybackIsoBmffFragmentSampleBatchPresentationMerger.Merge(
                chain,
                out var merge));
        Assert.Empty(Assert.IsType<
            PlaybackIsoBmffFragmentSampleBatchPresentationMerge>(merge).Samples);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65537)]
    public void Merge_InvalidLimitRejectsBeforeInputAccess(int maximum)
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleBatchPresentationMergeResult
                .InvalidConfiguration,
            PlaybackIsoBmffFragmentSampleBatchPresentationMerger.Merge(
                null,
                out var merge,
                new() { MaximumSampleCount = maximum }));
        Assert.Null(merge);
    }

    [Fact]
    public void Merge_SampleLimitRejectsBeforeAllocation()
    {
        var batch = Batch(1, 0, 0);
        var chain = SingleChain(batch);
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleBatchPresentationMergeResult
                .SampleLimitExceeded,
            PlaybackIsoBmffFragmentSampleBatchPresentationMerger.Merge(
                chain,
                out var merge,
                new() { MaximumSampleCount = 0 }));
        Assert.Null(merge);
    }

    [Fact]
    public void Merge_NullSourceIsRejectedWithoutProjection()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleBatchPresentationMergeResult.InvalidInput,
            PlaybackIsoBmffFragmentSampleBatchPresentationMerger.Merge(
                null,
                out var merge));
        Assert.Null(merge);
    }

    private static PlaybackIsoBmffFragmentSampleBatchChain Chain(
        PlaybackIsoBmffFragmentSampleBatch previous,
        PlaybackIsoBmffFragmentSampleBatch current)
    {
        var continuity = PlaybackIsoBmffFragmentSampleBatchChainTests.Continuity(
            previous,
            current);
        PlaybackIsoBmffFragmentSampleBatchChainProjector.Project(
            continuity,
            [previous, current],
            out var chain);
        return Assert.IsType<PlaybackIsoBmffFragmentSampleBatchChain>(chain);
    }

    private static PlaybackIsoBmffFragmentSampleBatchChain SingleChain(
        PlaybackIsoBmffFragmentSampleBatch batch)
    {
        var continuity = new PlaybackIsoBmffFragmentContinuityChain(
            null,
            new ReadOnlyCollection<PlaybackIsoBmffFragmentContinuityBinding>([]));
        PlaybackIsoBmffFragmentSampleBatchChainProjector.Project(
            continuity,
            [batch],
            out var chain);
        return Assert.IsType<PlaybackIsoBmffFragmentSampleBatchChain>(chain);
    }

    private static PlaybackIsoBmffFragmentSampleBatch Batch(
        uint sequence,
        int decode,
        int presentation,
        int sampleIndex = 0)
    {
        var timedSample = new PlaybackIsoBmffTimedFragmentSample(
            sampleIndex,
            1,
            1,
            0,
            decode,
            decode + 1,
            presentation);
        var timed = new PlaybackIsoBmffTimedFragmentRun(
            7,
            1,
            decode,
            decode + 1,
            new ReadOnlyCollection<PlaybackIsoBmffTimedFragmentSample>(
                [timedSample]));
        var rationalSample = new PlaybackIsoBmffFragmentSampleRationalTime(
            sampleIndex,
            48_000,
            decode,
            decode + 1,
            presentation,
            1,
            1,
            0,
            sequence * 10,
            (sequence * 10) + 1);
        var rational = new PlaybackIsoBmffFragmentSampleRationalTimeProjection(
            7,
            1,
            48_000,
            new ReadOnlyCollection<PlaybackIsoBmffFragmentSampleRationalTime>(
                [rationalSample]));
        PlaybackIsoBmffFragmentSamplePresentationOrderer.Order(
            rational,
            out var order);
        PlaybackIsoBmffFragmentSampleBatchBinder.Bind(
            new(sequence),
            timed,
            order,
            out var batch);
        return Assert.IsType<PlaybackIsoBmffFragmentSampleBatch>(batch);
    }
}
