using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffSampleEditOverlapLookupTests
{
    [Fact]
    public void Lookup_PreservesMultiplePartialOverlaps()
    {
        var sample = Sample(5, 15);
        var result = PlaybackIsoBmffSampleEditOverlapLookup.Lookup(
            sample,
            ParseEditList(
                (10UL, 0L, (short)1, (short)0),
                (10UL, 10L, (short)1, (short)0)),
            ParseMovieTiming(1),
            ParseMediaTiming(1),
            out var overlapSet);

        Assert.Equal(PlaybackIsoBmffSampleEditOverlapLookupResult.Found, result);
        var found = Assert.IsType<PlaybackIsoBmffSampleEditOverlapSet>(overlapSet);
        Assert.Equal(sample, found.Sample);
        Assert.Equal(
            [
                new PlaybackIsoBmffSampleEditOverlap(0, 5, 10, false),
                new PlaybackIsoBmffSampleEditOverlap(1, 10, 15, false),
            ],
            found.Overlaps);
    }

    [Fact]
    public void Lookup_ReportsCompleteSampleContainment()
    {
        var result = PlaybackIsoBmffSampleEditOverlapLookup.Lookup(
            Sample(2, 4),
            ParseEditList((10UL, 0L, (short)1, (short)0)),
            ParseMovieTiming(1),
            ParseMediaTiming(1),
            out var overlapSet);

        Assert.Equal(PlaybackIsoBmffSampleEditOverlapLookupResult.Found, result);
        Assert.True(Assert.Single(
            Assert.IsType<PlaybackIsoBmffSampleEditOverlapSet>(overlapSet)
                .Overlaps).ContainsCompleteSample);
    }

    [Fact]
    public void Lookup_EmptyEditsAndHalfOpenBoundariesDoNotOverlap()
    {
        var result = PlaybackIsoBmffSampleEditOverlapLookup.Lookup(
            Sample(10, 12),
            ParseEditList(
                (10UL, -1L, (short)1, (short)0),
                (10UL, 0L, (short)1, (short)0)),
            ParseMovieTiming(1),
            ParseMediaTiming(1),
            out var overlapSet);

        Assert.Equal(
            PlaybackIsoBmffSampleEditOverlapLookupResult.NotPresented,
            result);
        Assert.Null(overlapSet);
    }

    [Fact]
    public void Lookup_ZeroDurationSampleIsNotPresented()
    {
        var result = PlaybackIsoBmffSampleEditOverlapLookup.Lookup(
            Sample(3, 3),
            ParseEditList((10UL, 0L, (short)1, (short)0)),
            ParseMovieTiming(1),
            ParseMediaTiming(1),
            out var overlapSet);

        Assert.Equal(
            PlaybackIsoBmffSampleEditOverlapLookupResult.NotPresented,
            result);
        Assert.Null(overlapSet);
    }

    [Fact]
    public void Lookup_InvalidSampleIntervalFailsClosed()
    {
        var result = PlaybackIsoBmffSampleEditOverlapLookup.Lookup(
            Sample(4, 3),
            ParseEditList((10UL, 0L, (short)1, (short)0)),
            ParseMovieTiming(1),
            ParseMediaTiming(1),
            out var overlapSet);

        Assert.Equal(
            PlaybackIsoBmffSampleEditOverlapLookupResult.InvalidSampleInterval,
            result);
        Assert.Null(overlapSet);
    }

    [Fact]
    public void Lookup_UnsupportedRateAndFractionalDurationPropagate()
    {
        Assert.Equal(
            PlaybackIsoBmffSampleEditOverlapLookupResult.UnsupportedMediaRate,
            PlaybackIsoBmffSampleEditOverlapLookup.Lookup(
                Sample(0, 1),
                ParseEditList((1UL, 0L, (short)0, (short)0)),
                ParseMovieTiming(1),
                ParseMediaTiming(1),
                out var rateSet));
        Assert.Null(rateSet);

        Assert.Equal(
            PlaybackIsoBmffSampleEditOverlapLookupResult
                .NonIntegralMediaDuration,
            PlaybackIsoBmffSampleEditOverlapLookup.Lookup(
                Sample(0, 1),
                ParseEditList((1UL, 0L, (short)1, (short)0)),
                ParseMovieTiming(3),
                ParseMediaTiming(2),
                out var fractionalSet));
        Assert.Null(fractionalSet);
    }

    [Fact]
    public void Lookup_OverlapLimitIsEnforcedBeforeAddition()
    {
        var result = PlaybackIsoBmffSampleEditOverlapLookup.Lookup(
            Sample(0, 1),
            ParseEditList((1UL, 0L, (short)1, (short)0)),
            ParseMovieTiming(1),
            ParseMediaTiming(1),
            out var overlapSet,
            new() { MaximumOverlapCount = 0 });

        Assert.Equal(
            PlaybackIsoBmffSampleEditOverlapLookupResult.OverlapLimitExceeded,
            result);
        Assert.Null(overlapSet);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65_537)]
    public void Lookup_InvalidLimitRejectsBeforeInputAccess(int maximum)
    {
        var result = PlaybackIsoBmffSampleEditOverlapLookup.Lookup(
            null,
            null,
            null,
            null,
            out var overlapSet,
            new() { MaximumOverlapCount = maximum });

        Assert.Equal(
            PlaybackIsoBmffSampleEditOverlapLookupResult.InvalidConfiguration,
            result);
        Assert.Null(overlapSet);
    }

    [Fact]
    public void Lookup_MissingInputsAreDistinct()
    {
        var sample = Sample(0, 1);
        var editList = ParseEditList((1UL, 0L, (short)1, (short)0));
        var movieTiming = ParseMovieTiming(1);
        var mediaTiming = ParseMediaTiming(1);

        AssertMissing(
            PlaybackIsoBmffSampleEditOverlapLookupResult
                .MissingSamplePresentation,
            null,
            editList,
            movieTiming,
            mediaTiming);
        AssertMissing(
            PlaybackIsoBmffSampleEditOverlapLookupResult.MissingEditList,
            sample,
            null,
            movieTiming,
            mediaTiming);
        AssertMissing(
            PlaybackIsoBmffSampleEditOverlapLookupResult.MissingMovieTiming,
            sample,
            editList,
            null,
            mediaTiming);
        AssertMissing(
            PlaybackIsoBmffSampleEditOverlapLookupResult.MissingMediaTiming,
            sample,
            editList,
            movieTiming,
            null);
    }

    private static PlaybackIsoBmffSamplePresentationTime Sample(
        long start,
        long end) => new(start, 0, end, 0, false);

    private static void AssertMissing(
        PlaybackIsoBmffSampleEditOverlapLookupResult expected,
        PlaybackIsoBmffSamplePresentationTime? sample,
        PlaybackIsoBmffEditListBox? editList,
        PlaybackIsoBmffMovieHeaderTimingBox? movieTiming,
        PlaybackIsoBmffMediaHeaderTimingBox? mediaTiming)
    {
        var result = PlaybackIsoBmffSampleEditOverlapLookup.Lookup(
            sample,
            editList,
            movieTiming,
            mediaTiming,
            out var overlapSet);
        Assert.Equal(expected, result);
        Assert.Null(overlapSet);
    }

    private static PlaybackIsoBmffEditListBox ParseEditList(
        params (ulong Duration, long MediaTime, short RateInteger,
            short RateFraction)[] entries)
    {
        var box = new byte[16 + (entries.Length * 20)];
        WriteBoxHeader(box, 0x656C7374);
        box[8] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4),
            (uint)entries.Length);
        for (var index = 0; index < entries.Length; index++)
        {
            var offset = 16 + (index * 20);
            var entry = entries[index];
            BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(offset, 8),
                entry.Duration);
            BinaryPrimitives.WriteInt64BigEndian(box.AsSpan(offset + 8, 8),
                entry.MediaTime);
            BinaryPrimitives.WriteInt16BigEndian(box.AsSpan(offset + 16, 2),
                entry.RateInteger);
            BinaryPrimitives.WriteInt16BigEndian(box.AsSpan(offset + 18, 2),
                entry.RateFraction);
        }

        Assert.Equal(
            PlaybackIsoBmffEditListBoxParseResult.Parsed,
            PlaybackIsoBmffEditListBoxParser.Parse(box, out var editList));
        return Assert.IsType<PlaybackIsoBmffEditListBox>(editList);
    }

    private static PlaybackIsoBmffMovieHeaderTimingBox ParseMovieTiming(
        uint timescale)
    {
        var box = new byte[108];
        WriteBoxHeader(box, 0x6D766864);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(20, 4), timescale);
        Assert.Equal(
            PlaybackIsoBmffMovieHeaderTimingBoxParseResult.Parsed,
            PlaybackIsoBmffMovieHeaderTimingBoxParser.Parse(box, out var timing));
        return Assert.IsType<PlaybackIsoBmffMovieHeaderTimingBox>(timing);
    }

    private static PlaybackIsoBmffMediaHeaderTimingBox ParseMediaTiming(
        uint timescale)
    {
        var box = new byte[32];
        WriteBoxHeader(box, 0x6D646864);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(20, 4), timescale);
        BinaryPrimitives.WriteUInt16BigEndian(box.AsSpan(28, 2), 0x55C4);
        Assert.Equal(
            PlaybackIsoBmffMediaHeaderTimingBoxParseResult.Parsed,
            PlaybackIsoBmffMediaHeaderTimingBoxParser.Parse(box, out var timing));
        return Assert.IsType<PlaybackIsoBmffMediaHeaderTimingBox>(timing);
    }

    private static void WriteBoxHeader(byte[] box, uint type)
    {
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4),
            (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), type);
    }
}
