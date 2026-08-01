using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffEditTimeMapperTests
{
    [Fact]
    public void TryMap_ConvertsSegmentOffsetAcrossTimescales()
    {
        var result = PlaybackIsoBmffEditTimeMapper.TryMap(
            7,
            ParseEditList(
                (2UL, -1L, (short)1, (short)0),
                (10UL, 100L, (short)1, (short)0)),
            ParseMovieTiming(10),
            ParseMediaTiming(100),
            out var mapping);

        Assert.Equal(PlaybackIsoBmffEditTimeMappingResult.Mapped, result);
        var mapped = Assert.IsType<PlaybackIsoBmffEditTimeMapping>(mapping);
        Assert.Equal(1, mapped.Segment.EntryIndex);
        Assert.Equal((Int128)150, mapped.MediaTimeUnits);
    }

    [Fact]
    public void TryMap_PreservesSignedMediaOrigin()
    {
        var result = PlaybackIsoBmffEditTimeMapper.TryMap(
            1,
            ParseEditList((3UL, -5L, (short)1, (short)0)),
            ParseMovieTiming(1),
            ParseMediaTiming(2),
            out var mapping);

        Assert.Equal(PlaybackIsoBmffEditTimeMappingResult.Mapped, result);
        Assert.Equal(
            (Int128)(-3),
            Assert.IsType<PlaybackIsoBmffEditTimeMapping>(mapping)
                .MediaTimeUnits);
    }

    [Fact]
    public void TryMap_EmptyEditFailsWithoutInventingMediaTime()
    {
        var result = PlaybackIsoBmffEditTimeMapper.TryMap(
            0,
            ParseEditList((1UL, -1L, (short)1, (short)0)),
            ParseMovieTiming(1),
            ParseMediaTiming(1),
            out var mapping);

        Assert.Equal(PlaybackIsoBmffEditTimeMappingResult.EmptyEdit, result);
        Assert.Null(mapping);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(-1, 0)]
    public void TryMap_NonNormalRatesAreRejected(
        short rateInteger,
        short rateFraction)
    {
        var result = PlaybackIsoBmffEditTimeMapper.TryMap(
            0,
            ParseEditList((1UL, 0L, rateInteger, rateFraction)),
            ParseMovieTiming(1),
            ParseMediaTiming(1),
            out var mapping);

        Assert.Equal(
            PlaybackIsoBmffEditTimeMappingResult.UnsupportedMediaRate,
            result);
        Assert.Null(mapping);
    }

    [Fact]
    public void TryMap_FractionalMediaUnitFailsClosed()
    {
        var result = PlaybackIsoBmffEditTimeMapper.TryMap(
            1,
            ParseEditList((3UL, 0L, (short)1, (short)0)),
            ParseMovieTiming(3),
            ParseMediaTiming(2),
            out var mapping);

        Assert.Equal(
            PlaybackIsoBmffEditTimeMappingResult.NonIntegralMediaTime,
            result);
        Assert.Null(mapping);
    }

    [Fact]
    public void TryMap_MissingInputsAndRangeFailuresAreDistinct()
    {
        var editList = ParseEditList((1UL, 0L, (short)1, (short)0));
        var movieTiming = ParseMovieTiming(1);
        var mediaTiming = ParseMediaTiming(1);

        AssertResult(
            PlaybackIsoBmffEditTimeMappingResult.MissingEditList,
            null,
            movieTiming,
            mediaTiming);
        AssertResult(
            PlaybackIsoBmffEditTimeMappingResult.MissingMovieTiming,
            editList,
            null,
            mediaTiming);
        AssertResult(
            PlaybackIsoBmffEditTimeMappingResult.MissingMediaTiming,
            editList,
            movieTiming,
            null);

        var rangeResult = PlaybackIsoBmffEditTimeMapper.TryMap(
            1,
            editList,
            movieTiming,
            mediaTiming,
            out var rangeMapping);
        Assert.Equal(
            PlaybackIsoBmffEditTimeMappingResult.MovieTimeOutOfRange,
            rangeResult);
        Assert.Null(rangeMapping);
    }

    private static void AssertResult(
        PlaybackIsoBmffEditTimeMappingResult expected,
        PlaybackIsoBmffEditListBox? editList,
        PlaybackIsoBmffMovieHeaderTimingBox? movieTiming,
        PlaybackIsoBmffMediaHeaderTimingBox? mediaTiming)
    {
        var result = PlaybackIsoBmffEditTimeMapper.TryMap(
            0,
            editList,
            movieTiming,
            mediaTiming,
            out var mapping);
        Assert.Equal(expected, result);
        Assert.Null(mapping);
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
