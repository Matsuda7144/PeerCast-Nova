using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffEditIntervalProjectorTests
{
    [Fact]
    public void TryProject_ProjectsExactMovieAndMediaBounds()
    {
        var result = PlaybackIsoBmffEditIntervalProjector.TryProject(
            1,
            ParseEditList(
                (2UL, -1L, (short)1, (short)0),
                (4UL, 10L, (short)1, (short)0)),
            ParseMovieTiming(2),
            ParseMediaTiming(5),
            out var interval);

        Assert.Equal(
            PlaybackIsoBmffEditIntervalProjectionResult.Projected,
            result);
        Assert.Equal(
            new PlaybackIsoBmffEditInterval(1, 2, 6, 10, 20),
            interval);
    }

    [Fact]
    public void TryProject_PreservesNegativeMediaStart()
    {
        var result = PlaybackIsoBmffEditIntervalProjector.TryProject(
            0,
            ParseEditList((3UL, -5L, (short)1, (short)0)),
            ParseMovieTiming(1),
            ParseMediaTiming(2),
            out var interval);

        Assert.Equal(
            PlaybackIsoBmffEditIntervalProjectionResult.Projected,
            result);
        Assert.Equal((Int128)(-5), interval?.MediaStartUnits);
        Assert.Equal((Int128)1, interval?.MediaEndUnitsExclusive);
    }

    [Fact]
    public void TryProject_ZeroDurationProducesEmptyHalfOpenInterval()
    {
        var result = PlaybackIsoBmffEditIntervalProjector.TryProject(
            0,
            ParseEditList((0UL, 7L, (short)1, (short)0)),
            ParseMovieTiming(1),
            ParseMediaTiming(1),
            out var interval);

        Assert.Equal(
            PlaybackIsoBmffEditIntervalProjectionResult.Projected,
            result);
        Assert.Equal(interval?.MovieStartUnits, interval?.MovieEndUnitsExclusive);
        Assert.Equal(interval?.MediaStartUnits, interval?.MediaEndUnitsExclusive);
    }

    [Fact]
    public void TryProject_EmptyEditIsNotGivenMediaBounds()
    {
        var result = PlaybackIsoBmffEditIntervalProjector.TryProject(
            0,
            ParseEditList((1UL, -1L, (short)1, (short)0)),
            ParseMovieTiming(1),
            ParseMediaTiming(1),
            out var interval);

        Assert.Equal(
            PlaybackIsoBmffEditIntervalProjectionResult.EmptyEdit,
            result);
        Assert.Null(interval);
    }

    [Fact]
    public void TryProject_UnsupportedRateAndFractionalDurationFailClosed()
    {
        Assert.Equal(
            PlaybackIsoBmffEditIntervalProjectionResult.UnsupportedMediaRate,
            PlaybackIsoBmffEditIntervalProjector.TryProject(
                0,
                ParseEditList((1UL, 0L, (short)0, (short)0)),
                ParseMovieTiming(1),
                ParseMediaTiming(1),
                out var rateInterval));
        Assert.Null(rateInterval);

        Assert.Equal(
            PlaybackIsoBmffEditIntervalProjectionResult
                .NonIntegralMediaDuration,
            PlaybackIsoBmffEditIntervalProjector.TryProject(
                0,
                ParseEditList((1UL, 0L, (short)1, (short)0)),
                ParseMovieTiming(3),
                ParseMediaTiming(2),
                out var fractionalInterval));
        Assert.Null(fractionalInterval);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void TryProject_EntryIndexMustBeInRange(int entryIndex)
    {
        var result = PlaybackIsoBmffEditIntervalProjector.TryProject(
            entryIndex,
            ParseEditList((1UL, 0L, (short)1, (short)0)),
            ParseMovieTiming(1),
            ParseMediaTiming(1),
            out var interval);

        Assert.Equal(
            PlaybackIsoBmffEditIntervalProjectionResult.EntryIndexOutOfRange,
            result);
        Assert.Null(interval);
    }

    [Fact]
    public void TryProject_MissingInputsAreDistinct()
    {
        var editList = ParseEditList((1UL, 0L, (short)1, (short)0));
        var movieTiming = ParseMovieTiming(1);
        var mediaTiming = ParseMediaTiming(1);

        AssertMissing(
            PlaybackIsoBmffEditIntervalProjectionResult.MissingEditList,
            null,
            movieTiming,
            mediaTiming);
        AssertMissing(
            PlaybackIsoBmffEditIntervalProjectionResult.MissingMovieTiming,
            editList,
            null,
            mediaTiming);
        AssertMissing(
            PlaybackIsoBmffEditIntervalProjectionResult.MissingMediaTiming,
            editList,
            movieTiming,
            null);
    }

    private static void AssertMissing(
        PlaybackIsoBmffEditIntervalProjectionResult expected,
        PlaybackIsoBmffEditListBox? editList,
        PlaybackIsoBmffMovieHeaderTimingBox? movieTiming,
        PlaybackIsoBmffMediaHeaderTimingBox? mediaTiming)
    {
        var result = PlaybackIsoBmffEditIntervalProjector.TryProject(
            0,
            editList,
            movieTiming,
            mediaTiming,
            out var interval);
        Assert.Equal(expected, result);
        Assert.Null(interval);
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
