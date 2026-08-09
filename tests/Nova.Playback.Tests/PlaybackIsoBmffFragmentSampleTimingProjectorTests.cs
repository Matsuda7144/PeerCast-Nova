using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffFragmentSampleTimingProjectorTests
{
    [Fact]
    public void Project_AccumulatesDecodeTimeAndAppliesSignedCompositionOffsets()
    {
        var decodeTime = new PlaybackIsoBmffTrackFragmentDecodeTimeBox(1, 100);
        var resolved = CreateResolvedRun(
            new(10, 20, 30, -3),
            new(11, 21, 31, 4));

        Assert.Equal(
            PlaybackIsoBmffFragmentSampleTimingProjectResult.Projected,
            PlaybackIsoBmffFragmentSampleTimingProjector.Project(
                decodeTime,
                resolved,
                out var timedRun));
        var timed = Assert.IsType<PlaybackIsoBmffTimedFragmentRun>(timedRun);
        Assert.Equal(7U, timed.TrackId);
        Assert.Equal(3U, timed.SampleDescriptionIndex);
        Assert.Equal(new BigInteger(100), timed.BaseMediaDecodeTime);
        Assert.Equal(new BigInteger(121), timed.NextDecodeTime);
        Assert.Equal(
            [
                new PlaybackIsoBmffTimedFragmentSample(
                    0,
                    10,
                    20,
                    30,
                    100,
                    110,
                    97),
                new PlaybackIsoBmffTimedFragmentSample(
                    1,
                    11,
                    21,
                    31,
                    110,
                    121,
                    114),
            ],
            timed.Samples);
        Assert.IsNotType<PlaybackIsoBmffTimedFragmentSample[]>(timed.Samples);
    }

    [Fact]
    public void Project_UsesBigIntegerBeyondUnsignedSixtyFourBitBoundary()
    {
        var decodeTime = new PlaybackIsoBmffTrackFragmentDecodeTimeBox(
            1,
            ulong.MaxValue);
        var resolved = CreateResolvedRun(
            new PlaybackIsoBmffResolvedFragmentSample(
                uint.MaxValue,
                0,
                0,
                long.MaxValue));

        Assert.Equal(
            PlaybackIsoBmffFragmentSampleTimingProjectResult.Projected,
            PlaybackIsoBmffFragmentSampleTimingProjector.Project(
                decodeTime,
                resolved,
                out var timedRun));
        var timed = Assert.IsType<PlaybackIsoBmffTimedFragmentRun>(timedRun);
        Assert.Equal(
            new BigInteger(ulong.MaxValue) + uint.MaxValue,
            timed.NextDecodeTime);
        Assert.Equal(
            new BigInteger(ulong.MaxValue) + long.MaxValue,
            timed.Samples[0].PresentationTime);
    }

    [Fact]
    public void Project_ZeroDurationDoesNotAdvanceDecodeCursor()
    {
        var resolved = CreateResolvedRun(
            new(0, 1, 2, null),
            new(0, 3, 4, null));

        Assert.Equal(
            PlaybackIsoBmffFragmentSampleTimingProjectResult.Projected,
            PlaybackIsoBmffFragmentSampleTimingProjector.Project(
                new(0, 9),
                resolved,
                out var timedRun));
        var timed = Assert.IsType<PlaybackIsoBmffTimedFragmentRun>(timedRun);
        Assert.All(timed.Samples, sample =>
        {
            Assert.Equal(new BigInteger(9), sample.DecodeTime);
            Assert.Equal(new BigInteger(9), sample.DecodeEndTimeExclusive);
            Assert.Equal(new BigInteger(9), sample.PresentationTime);
        });
        Assert.Equal(new BigInteger(9), timed.NextDecodeTime);
    }

    [Fact]
    public void Project_EmptyRunRetainsBaseAsNextDecodeTime()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleTimingProjectResult.Projected,
            PlaybackIsoBmffFragmentSampleTimingProjector.Project(
                new(0, 17),
                CreateResolvedRun(),
                out var timedRun));
        var timed = Assert.IsType<PlaybackIsoBmffTimedFragmentRun>(timedRun);
        Assert.Empty(timed.Samples);
        Assert.Equal(new BigInteger(17), timed.NextDecodeTime);
    }

    [Fact]
    public void Project_NullInputsAreRejectedWithoutProjection()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleTimingProjectResult.InvalidInput,
            PlaybackIsoBmffFragmentSampleTimingProjector.Project(
                null,
                null,
                out var timedRun));
        Assert.Null(timedRun);
    }

    private static PlaybackIsoBmffResolvedFragmentRun CreateResolvedRun(
        params PlaybackIsoBmffResolvedFragmentSample[] samples) =>
        new(7, 3, new ReadOnlyCollection<PlaybackIsoBmffResolvedFragmentSample>(
            samples));
}
