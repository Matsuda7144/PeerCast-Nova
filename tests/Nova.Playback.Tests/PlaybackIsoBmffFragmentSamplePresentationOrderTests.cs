using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffFragmentSamplePresentationOrderTests
{
    [Fact]
    public void Order_SortsByPresentationThenDecodeThenSourceIndex()
    {
        var source = Projection(
            Sample(0, decode: 10, presentation: 30),
            Sample(1, decode: 20, presentation: 10),
            Sample(2, decode: 15, presentation: 30));

        Assert.Equal(
            PlaybackIsoBmffFragmentSamplePresentationOrderResult.Ordered,
            PlaybackIsoBmffFragmentSamplePresentationOrderer.Order(
                source,
                out var order));
        var ordered = Assert.IsType<
            PlaybackIsoBmffFragmentSamplePresentationOrder>(order);
        Assert.Same(source, ordered.Source);
        Assert.Equal([1, 0, 2], ordered.Samples.Select(
            sample => sample.SampleIndex));
        Assert.IsNotType<PlaybackIsoBmffFragmentSampleRationalTime[]>(
            ordered.Samples);
    }

    [Fact]
    public void Order_NegativePresentationNumeratorSortsFirstExactly()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentSamplePresentationOrderResult.Ordered,
            PlaybackIsoBmffFragmentSamplePresentationOrderer.Order(
                Projection(
                    Sample(0, 0, BigInteger.Pow(2, 100)),
                    Sample(1, 1, -BigInteger.Pow(2, 100))),
                out var order));
        Assert.Equal(
            [1, 0],
            Assert.IsType<PlaybackIsoBmffFragmentSamplePresentationOrder>(
                order).Samples.Select(sample => sample.SampleIndex));
    }

    [Fact]
    public void Order_EmptyProjectionIsValid()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentSamplePresentationOrderResult.Ordered,
            PlaybackIsoBmffFragmentSamplePresentationOrderer.Order(
                Projection(),
                out var order));
        Assert.Empty(Assert.IsType<
            PlaybackIsoBmffFragmentSamplePresentationOrder>(order).Samples);
    }

    [Fact]
    public void Order_InvalidSourceIndexIsRejected()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentSamplePresentationOrderResult
                .InvalidSampleIndex,
            PlaybackIsoBmffFragmentSamplePresentationOrderer.Order(
                Projection(Sample(1, 0, 0)),
                out var order));
        Assert.Null(order);
    }

    [Fact]
    public void Order_SampleTimescaleMismatchIsRejected()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentSamplePresentationOrderResult
                .MediaTimescaleMismatch,
            PlaybackIsoBmffFragmentSamplePresentationOrderer.Order(
                Projection(Sample(0, 0, 0, mediaTimescale: 90_000)),
                out var order));
        Assert.Null(order);
    }

    [Fact]
    public void Order_NullSourceIsRejectedWithoutProjection()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentSamplePresentationOrderResult.InvalidInput,
            PlaybackIsoBmffFragmentSamplePresentationOrderer.Order(
                null,
                out var order));
        Assert.Null(order);
    }

    private static PlaybackIsoBmffFragmentSampleRationalTimeProjection Projection(
        params PlaybackIsoBmffFragmentSampleRationalTime[] samples) =>
        new(
            7,
            3,
            48_000,
            new ReadOnlyCollection<PlaybackIsoBmffFragmentSampleRationalTime>(
                samples));

    private static PlaybackIsoBmffFragmentSampleRationalTime Sample(
        int index,
        BigInteger decode,
        BigInteger presentation,
        uint mediaTimescale = 48_000) =>
        new(
            index,
            mediaTimescale,
            decode,
            decode + 1,
            presentation,
            1,
            1,
            0,
            index,
            index + 1);
}
