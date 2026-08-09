using System.Numerics;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeCursorResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffFragmentSampleMergeCursorTests
{
    [Fact]
    public void Project_CreatesInitialCursorOverExactSource()
    {
        var source = Merge(3);

        var cursor = Project(source, 0);

        Assert.Same(source, cursor.Source);
        Assert.Equal(0, cursor.NextIndex);
        Assert.Equal(3, cursor.Count);
        Assert.Equal(3, cursor.RemainingCount);
        Assert.False(cursor.IsComplete);
        Assert.Equal(source.Samples[0], cursor.Current);
    }

    [Fact]
    public void Project_CreatesResumedCursor()
    {
        var source = Merge(3);

        var cursor = Project(source, 2);

        Assert.Equal(2, cursor.NextIndex);
        Assert.Equal(1, cursor.RemainingCount);
        Assert.Equal(source.Samples[2], cursor.Current);
    }

    [Fact]
    public void Project_AcceptsCompleteCursor()
    {
        var source = Merge(2);

        var cursor = Project(source, source.Samples.Count);

        Assert.True(cursor.IsComplete);
        Assert.Equal(0, cursor.RemainingCount);
        Assert.Null(cursor.Current);
    }

    [Fact]
    public void Project_AcceptsEmptyCompleteCursor()
    {
        var cursor = Project(Merge(0), 0);

        Assert.True(cursor.IsComplete);
        Assert.Equal(0, cursor.Count);
        Assert.Null(cursor.Current);
    }

    [Fact]
    public void Project_RejectsMissingSourceAndOutOfRangeIndexes()
    {
        AssertResult(Result.InvalidInput, null, 0);
        var source = Merge(2);
        AssertResult(Result.IndexOutOfRange, source, -1);
        AssertResult(Result.IndexOutOfRange, source, 3);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffFragmentSampleBatchPresentationMerge? source,
        int nextIndex)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffFragmentSampleMergeCursorProjector.Project(
                source,
                nextIndex,
                out var cursor));
        Assert.Null(cursor);
    }

    internal static PlaybackIsoBmffFragmentSampleMergeCursor Project(
        PlaybackIsoBmffFragmentSampleBatchPresentationMerge source,
        int nextIndex)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeCursorProjector.Project(
                source,
                nextIndex,
                out var cursor));
        return Assert.IsType<PlaybackIsoBmffFragmentSampleMergeCursor>(cursor);
    }

    internal static PlaybackIsoBmffFragmentSampleBatchPresentationMerge Merge(
        int sampleCount)
    {
        var continuity = new PlaybackIsoBmffFragmentContinuityChain(
            null,
            Array.AsReadOnly(
                Array.Empty<PlaybackIsoBmffFragmentContinuityBinding>()));
        var chain = new PlaybackIsoBmffFragmentSampleBatchChain(
            continuity,
            7,
            48_000,
            Array.AsReadOnly(Array.Empty<PlaybackIsoBmffFragmentSampleBatch>()));
        var samples = Enumerable.Range(0, sampleCount)
            .Select(index =>
                new PlaybackIsoBmffFragmentBatchPresentationSample(
                    index / 2,
                    (uint)(10 + index / 2),
                    new PlaybackIsoBmffFragmentSampleRationalTime(
                        index,
                        48_000,
                        new BigInteger(index * 1_024),
                        new BigInteger((index + 1) * 1_024),
                        new BigInteger(index * 1_024 - 512),
                        new BigInteger(1_024),
                        (uint)(100 + index),
                        (uint)index,
                        new BigInteger(1_000 + index * 100),
                        new BigInteger(1_100 + index * 101))))
            .ToArray();
        return new(chain, Array.AsReadOnly(samples));
    }
}
