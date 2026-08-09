using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffFragmentSampleMetadataProjectorTests
{
    [Fact]
    public void Project_JoinsExactTimingAndByteRangeMetadata()
    {
        var result = PlaybackIsoBmffFragmentSampleMetadataProjector.Project(
            TimedRun(
                [new(0, 10, 4, 7, 100, 110, 97)]),
            ByteRanges(
                [new(0, 1_000, 4, 1_004)]),
            out var projection);

        Assert.Equal(
            PlaybackIsoBmffFragmentSampleMetadataProjectResult.Projected,
            result);
        var projected = Assert.IsType<
            PlaybackIsoBmffFragmentSampleMetadataProjection>(projection);
        Assert.Equal(7U, projected.TrackId);
        Assert.Equal(3U, projected.SampleDescriptionIndex);
        Assert.Equal(
            new PlaybackIsoBmffFragmentSampleMetadata(
                0,
                10,
                4,
                7,
                100,
                110,
                97,
                1_000,
                1_004),
            Assert.Single(projected.Samples));
        Assert.IsNotType<PlaybackIsoBmffFragmentSampleMetadata[]>(
            projected.Samples);
    }

    [Fact]
    public void Project_EmptyMatchingSourcesProduceImmutableEmptyProjection()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleMetadataProjectResult.Projected,
            PlaybackIsoBmffFragmentSampleMetadataProjector.Project(
                TimedRun([]),
                ByteRanges([]),
                out var projection));
        Assert.Empty(Assert.IsType<
            PlaybackIsoBmffFragmentSampleMetadataProjection>(projection).Samples);
    }

    [Fact]
    public void Project_RejectsTrackIdentityMismatch()
    {
        AssertResult(
            PlaybackIsoBmffFragmentSampleMetadataProjectResult.TrackIdMismatch,
            TimedRun([], trackId: 1),
            ByteRanges([], trackId: 2));
    }

    [Fact]
    public void Project_RejectsSampleDescriptionMismatch()
    {
        AssertResult(
            PlaybackIsoBmffFragmentSampleMetadataProjectResult
                .SampleDescriptionIndexMismatch,
            TimedRun([], sampleDescriptionIndex: 1),
            ByteRanges([], sampleDescriptionIndex: 2));
    }

    [Fact]
    public void Project_RejectsCountMismatch()
    {
        AssertResult(
            PlaybackIsoBmffFragmentSampleMetadataProjectResult
                .SampleCountMismatch,
            TimedRun([new(0, 1, 2, 3, 4, 5, 6)]),
            ByteRanges([]));
    }

    [Fact]
    public void Project_RejectsIndexMismatch()
    {
        AssertResult(
            PlaybackIsoBmffFragmentSampleMetadataProjectResult
                .SampleIndexMismatch,
            TimedRun([new(1, 1, 2, 3, 4, 5, 6)]),
            ByteRanges([new(0, 10, 2, 12)]));
    }

    [Fact]
    public void Project_RejectsSizeMismatch()
    {
        AssertResult(
            PlaybackIsoBmffFragmentSampleMetadataProjectResult
                .SampleSizeMismatch,
            TimedRun([new(0, 1, 2, 3, 4, 5, 6)]),
            ByteRanges([new(0, 10, 3, 13)]));
    }

    [Fact]
    public void Project_NullInputsAreRejectedWithoutProjection()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleMetadataProjectResult.InvalidInput,
            PlaybackIsoBmffFragmentSampleMetadataProjector.Project(
                null,
                null,
                out var projection));
        Assert.Null(projection);
    }

    private static void AssertResult(
        PlaybackIsoBmffFragmentSampleMetadataProjectResult expected,
        PlaybackIsoBmffTimedFragmentRun timedRun,
        PlaybackIsoBmffFragmentSampleByteRangeProjection ranges)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffFragmentSampleMetadataProjector.Project(
                timedRun,
                ranges,
                out var projection));
        Assert.Null(projection);
    }

    private static PlaybackIsoBmffTimedFragmentRun TimedRun(
        PlaybackIsoBmffTimedFragmentSample[] samples,
        uint trackId = 7,
        uint sampleDescriptionIndex = 3) =>
        new(
            trackId,
            sampleDescriptionIndex,
            baseMediaDecodeTime: 0,
            nextDecodeTime: 0,
            new ReadOnlyCollection<PlaybackIsoBmffTimedFragmentSample>(samples));

    private static PlaybackIsoBmffFragmentSampleByteRangeProjection ByteRanges(
        PlaybackIsoBmffFragmentSampleByteRange[] ranges,
        uint trackId = 7,
        uint sampleDescriptionIndex = 3) =>
        new(
            trackId,
            sampleDescriptionIndex,
            runDataStart: 0,
            runDataEndExclusive: 0,
            new ReadOnlyCollection<PlaybackIsoBmffFragmentSampleByteRange>(
                ranges));
}
