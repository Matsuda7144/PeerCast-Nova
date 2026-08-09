using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffFragmentSampleRationalTimeProjectorTests
{
    [Fact]
    public void Project_BindsExactNumeratorsToCallerMediaTimescale()
    {
        var large = BigInteger.Pow(2, 100);
        var source = Metadata(
            new PlaybackIsoBmffFragmentSampleMetadata(
                0,
                10,
                4,
                7,
                large,
                large + 10,
                -3,
                1_000,
                1_004));

        Assert.Equal(
            PlaybackIsoBmffFragmentSampleRationalTimeProjectResult.Projected,
            PlaybackIsoBmffFragmentSampleRationalTimeProjector.Project(
                source,
                48_000,
                out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffFragmentSampleRationalTimeProjection>(projection);
        Assert.Equal(7U, projected.TrackId);
        Assert.Equal(3U, projected.SampleDescriptionIndex);
        Assert.Equal(48_000U, projected.MediaTimescale);
        Assert.Equal(
            new PlaybackIsoBmffFragmentSampleRationalTime(
                0,
                48_000,
                large,
                large + 10,
                -3,
                10,
                4,
                7,
                1_000,
                1_004),
            Assert.Single(projected.Samples));
        Assert.IsNotType<PlaybackIsoBmffFragmentSampleRationalTime[]>(
            projected.Samples);
    }

    [Fact]
    public void Project_EmptyMetadataRetainsIdentityAndTimescale()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleRationalTimeProjectResult.Projected,
            PlaybackIsoBmffFragmentSampleRationalTimeProjector.Project(
                Metadata(),
                uint.MaxValue,
                out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffFragmentSampleRationalTimeProjection>(projection);
        Assert.Empty(projected.Samples);
        Assert.Equal(uint.MaxValue, projected.MediaTimescale);
    }

    [Fact]
    public void Project_ZeroTimescaleIsRejected()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleRationalTimeProjectResult
                .InvalidMediaTimescale,
            PlaybackIsoBmffFragmentSampleRationalTimeProjector.Project(
                Metadata(),
                0,
                out var projection));
        Assert.Null(projection);
    }

    [Fact]
    public void Project_NullMetadataIsRejectedBeforeTimescale()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleRationalTimeProjectResult.InvalidInput,
            PlaybackIsoBmffFragmentSampleRationalTimeProjector.Project(
                null,
                0,
                out var projection));
        Assert.Null(projection);
    }

    private static PlaybackIsoBmffFragmentSampleMetadataProjection Metadata(
        params PlaybackIsoBmffFragmentSampleMetadata[] samples) =>
        new(
            7,
            3,
            new ReadOnlyCollection<PlaybackIsoBmffFragmentSampleMetadata>(
                samples));
}
