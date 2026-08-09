using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffFragmentSampleByteRangeProjectorTests
{
    [Fact]
    public void Project_AccumulatesContiguousRangesFromResolvedRunDataStart()
    {
        var result = PlaybackIsoBmffFragmentSampleByteRangeProjector.Project(
            DataOrigin(100),
            ResolvedRun(
                new(1, 4, 0, null),
                new(1, 6, 0, null)),
            out var projection);

        Assert.Equal(
            PlaybackIsoBmffFragmentSampleByteRangeProjectResult.Projected,
            result);
        var projected =
            Assert.IsType<PlaybackIsoBmffFragmentSampleByteRangeProjection>(
                projection);
        Assert.Equal(7U, projected.TrackId);
        Assert.Equal(3U, projected.SampleDescriptionIndex);
        Assert.Equal(new BigInteger(100), projected.RunDataStart);
        Assert.Equal(new BigInteger(110), projected.RunDataEndExclusive);
        Assert.Equal(
            [
                new PlaybackIsoBmffFragmentSampleByteRange(0, 100, 4, 104),
                new PlaybackIsoBmffFragmentSampleByteRange(1, 104, 6, 110),
            ],
            projected.Ranges);
        Assert.IsNotType<PlaybackIsoBmffFragmentSampleByteRange[]>(
            projected.Ranges);
    }

    [Fact]
    public void Project_ZeroLengthSampleDoesNotAdvanceCursor()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleByteRangeProjectResult.Projected,
            PlaybackIsoBmffFragmentSampleByteRangeProjector.Project(
                DataOrigin(9),
                ResolvedRun(
                    new(0, 0, 0, null),
                    new(0, 2, 0, null)),
                out var projection));
        var ranges = Assert.IsType<
            PlaybackIsoBmffFragmentSampleByteRangeProjection>(projection).Ranges;
        Assert.Equal(
            new PlaybackIsoBmffFragmentSampleByteRange(0, 9, 0, 9),
            ranges[0]);
        Assert.Equal(
            new PlaybackIsoBmffFragmentSampleByteRange(1, 9, 2, 11),
            ranges[1]);
    }

    [Fact]
    public void Project_UsesBigIntegerBeyondUnsignedSixtyFourBitBoundary()
    {
        var start = new BigInteger(ulong.MaxValue);
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleByteRangeProjectResult.Projected,
            PlaybackIsoBmffFragmentSampleByteRangeProjector.Project(
                DataOrigin(start),
                ResolvedRun(new PlaybackIsoBmffResolvedFragmentSample(
                    uint.MaxValue,
                    uint.MaxValue,
                    0,
                    null)),
                out var projection));
        Assert.Equal(
            start + uint.MaxValue,
            Assert.IsType<PlaybackIsoBmffFragmentSampleByteRangeProjection>(
                projection).RunDataEndExclusive);
    }

    [Fact]
    public void Project_EmptyRunRetainsStartAsExclusiveEnd()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleByteRangeProjectResult.Projected,
            PlaybackIsoBmffFragmentSampleByteRangeProjector.Project(
                DataOrigin(17),
                ResolvedRun(),
                out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffFragmentSampleByteRangeProjection>(projection);
        Assert.Empty(projected.Ranges);
        Assert.Equal(new BigInteger(17), projected.RunDataEndExclusive);
    }

    [Fact]
    public void Project_NullInputsAreRejectedWithoutProjection()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleByteRangeProjectResult.InvalidInput,
            PlaybackIsoBmffFragmentSampleByteRangeProjector.Project(
                null,
                null,
                out var projection));
        Assert.Null(projection);
    }

    private static PlaybackIsoBmffFragmentRunDataOrigin DataOrigin(
        BigInteger start) =>
        new(
            baseDataOffset: start,
            PlaybackIsoBmffFragmentBaseDataOriginKind.CallerImplicit,
            runDataStart: start,
            PlaybackIsoBmffFragmentRunDataStartKind.CallerImplicit,
            relativeDataOffset: null);

    private static PlaybackIsoBmffResolvedFragmentRun ResolvedRun(
        params PlaybackIsoBmffResolvedFragmentSample[] samples) =>
        new(
            7,
            3,
            new ReadOnlyCollection<PlaybackIsoBmffResolvedFragmentSample>(
                samples));
}
