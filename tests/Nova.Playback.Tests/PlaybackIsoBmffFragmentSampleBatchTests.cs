using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffFragmentSampleBatchTests
{
    [Fact]
    public void Bind_ExactSourcesProduceOneImmutableBatch()
    {
        var header = new PlaybackIsoBmffMovieFragmentHeaderBox(9);
        var timed = TimedRun(trackId: 7, sampleDescriptionIndex: 3);
        var order = Order(Rational(trackId: 7, sampleDescriptionIndex: 3));

        Assert.Equal(
            PlaybackIsoBmffFragmentSampleBatchResult.Bound,
            PlaybackIsoBmffFragmentSampleBatchBinder.Bind(
                header,
                timed,
                order,
                out var batch));
        var bound = Assert.IsType<PlaybackIsoBmffFragmentSampleBatch>(batch);
        Assert.Same(header, bound.FragmentHeader);
        Assert.Same(timed, bound.TimedRun);
        Assert.Same(order, bound.PresentationOrder);
        Assert.Equal(9U, bound.SequenceNumber);
        Assert.Equal(7U, bound.TrackId);
        Assert.Equal(3U, bound.SampleDescriptionIndex);
        Assert.Equal(48_000U, bound.MediaTimescale);
        Assert.Equal(2, bound.SampleCount);
    }

    [Fact]
    public void Bind_EmptyExactSourcesAreValid()
    {
        var timed = TimedRun(samples: []);
        var rational = Rational(samples: []);

        Assert.Equal(
            PlaybackIsoBmffFragmentSampleBatchResult.Bound,
            PlaybackIsoBmffFragmentSampleBatchBinder.Bind(
                new(0),
                timed,
                Order(rational),
                out var batch));
        Assert.Equal(0, Assert.IsType<PlaybackIsoBmffFragmentSampleBatch>(
            batch).SampleCount);
    }

    [Fact]
    public void Bind_TrackMismatchIsRejected()
    {
        AssertResult(
            PlaybackIsoBmffFragmentSampleBatchResult.TrackIdMismatch,
            TimedRun(trackId: 1),
            Order(Rational(trackId: 2)));
    }

    [Fact]
    public void Bind_SampleDescriptionMismatchIsRejected()
    {
        AssertResult(
            PlaybackIsoBmffFragmentSampleBatchResult
                .SampleDescriptionIndexMismatch,
            TimedRun(sampleDescriptionIndex: 1),
            Order(Rational(sampleDescriptionIndex: 2)));
    }

    [Fact]
    public void Bind_CountMismatchIsRejected()
    {
        AssertResult(
            PlaybackIsoBmffFragmentSampleBatchResult.SampleCountMismatch,
            TimedRun(samples: []),
            Order(Rational()));
    }

    [Fact]
    public void Bind_MetadataMismatchIsRejected()
    {
        var rational = Rational(samples:
            [RationalSample(0, 0, 10, 99), RationalSample(1, 10, 20, 4)]);
        AssertResult(
            PlaybackIsoBmffFragmentSampleBatchResult.SampleMetadataMismatch,
            TimedRun(),
            Order(rational));
    }

    [Fact]
    public void Bind_InvalidPresentationPermutationIsRejected()
    {
        var rational = Rational();
        var invalidOrder = new PlaybackIsoBmffFragmentSamplePresentationOrder(
            rational,
            new ReadOnlyCollection<PlaybackIsoBmffFragmentSampleRationalTime>(
                [rational.Samples[0], rational.Samples[0]]));
        AssertResult(
            PlaybackIsoBmffFragmentSampleBatchResult
                .PresentationOrderMismatch,
            TimedRun(),
            invalidOrder);
    }

    [Fact]
    public void Bind_NullInputsAreRejectedWithoutBatch()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleBatchResult.InvalidInput,
            PlaybackIsoBmffFragmentSampleBatchBinder.Bind(
                null,
                null,
                null,
                out var batch));
        Assert.Null(batch);
    }

    private static void AssertResult(
        PlaybackIsoBmffFragmentSampleBatchResult expected,
        PlaybackIsoBmffTimedFragmentRun timed,
        PlaybackIsoBmffFragmentSamplePresentationOrder order)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffFragmentSampleBatchBinder.Bind(
                new(1),
                timed,
                order,
                out var batch));
        Assert.Null(batch);
    }

    internal static PlaybackIsoBmffTimedFragmentRun TimedRun(
        uint trackId = 7,
        uint sampleDescriptionIndex = 3,
        PlaybackIsoBmffTimedFragmentSample[]? samples = null)
    {
        samples ??=
        [
            new(0, 10, 4, 1, 0, 10, 5),
            new(1, 10, 4, 2, 10, 20, 15),
        ];
        return new(
            trackId,
            sampleDescriptionIndex,
            baseMediaDecodeTime: 0,
            nextDecodeTime: samples.Length == 0 ? 0 : 20,
            new ReadOnlyCollection<PlaybackIsoBmffTimedFragmentSample>(samples));
    }

    internal static PlaybackIsoBmffFragmentSampleRationalTimeProjection Rational(
        uint trackId = 7,
        uint sampleDescriptionIndex = 3,
        PlaybackIsoBmffFragmentSampleRationalTime[]? samples = null)
    {
        samples ??=
        [
            RationalSample(0, 0, 10, 4),
            RationalSample(1, 10, 20, 4),
        ];
        return new(
            trackId,
            sampleDescriptionIndex,
            48_000,
            new ReadOnlyCollection<PlaybackIsoBmffFragmentSampleRationalTime>(
                samples));
    }

    internal static PlaybackIsoBmffFragmentSamplePresentationOrder Order(
        PlaybackIsoBmffFragmentSampleRationalTimeProjection rational)
    {
        PlaybackIsoBmffFragmentSamplePresentationOrderer.Order(
            rational,
            out var order);
        return Assert.IsType<PlaybackIsoBmffFragmentSamplePresentationOrder>(
            order);
    }

    private static PlaybackIsoBmffFragmentSampleRationalTime RationalSample(
        int index,
        int decode,
        int end,
        uint size) =>
        new(
            index,
            48_000,
            decode,
            end,
            decode + 5,
            end - decode,
            size,
            (uint)(index + 1),
            index * 4,
            (index * 4) + size);
}
