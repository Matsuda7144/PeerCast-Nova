using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffMediaTimeConverterTests
{
    private const uint Mdhd = 0x6D646864;

    [Fact]
    public void TryConvert_PositiveAndNegativeUnitsUseMediaTimescale()
    {
        var timing = ParseMediaTiming(90_000);

        Assert.Equal(
            PlaybackIsoBmffMediaTimeConversionResult.Converted,
            PlaybackIsoBmffMediaTimeConverter.TryConvert(
                90_000,
                timing,
                out var positive));
        Assert.Equal(TimeSpan.FromSeconds(1), positive);
        Assert.Equal(
            PlaybackIsoBmffMediaTimeConversionResult.Converted,
            PlaybackIsoBmffMediaTimeConverter.TryConvert(
                -45_000,
                timing,
                out var negative));
        Assert.Equal(TimeSpan.FromMilliseconds(-500), negative);
    }

    [Theory]
    [InlineData(1, 3_333_333)]
    [InlineData(-1, -3_333_333)]
    public void TryConvert_SubTickFractionTruncatesTowardZero(
        long units,
        long expectedTicks)
    {
        var result = PlaybackIsoBmffMediaTimeConverter.TryConvert(
            units,
            ParseMediaTiming(3),
            out var time);

        Assert.Equal(PlaybackIsoBmffMediaTimeConversionResult.Converted, result);
        Assert.Equal(TimeSpan.FromTicks(expectedTicks), time);
    }

    [Fact]
    public void TryConvert_Int128ScalingFailsClosedAtMagnitudeLimit()
    {
        var result = PlaybackIsoBmffMediaTimeConverter.TryConvert(
            Int128.MaxValue,
            ParseMediaTiming(1),
            out var time);

        Assert.Equal(
            PlaybackIsoBmffMediaTimeConversionResult.MagnitudeExceeded,
            result);
        Assert.Null(time);
    }

    [Fact]
    public void TryConvert_ExactConfiguredMagnitudeIsAccepted()
    {
        var options = new PlaybackIsoBmffMediaTimeConversionOptions
        {
            MaximumMagnitude = TimeSpan.FromSeconds(2),
        };

        var result = PlaybackIsoBmffMediaTimeConverter.TryConvert(
            -2,
            ParseMediaTiming(1),
            out var time,
            options);

        Assert.Equal(PlaybackIsoBmffMediaTimeConversionResult.Converted, result);
        Assert.Equal(TimeSpan.FromSeconds(-2), time);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3_651)]
    public void TryConvert_InvalidMagnitudeConfigurationIsRejected(int days)
    {
        var options = new PlaybackIsoBmffMediaTimeConversionOptions
        {
            MaximumMagnitude = TimeSpan.FromDays(days),
        };

        var result = PlaybackIsoBmffMediaTimeConverter.TryConvert(
            0,
            null,
            out var time,
            options);

        Assert.Equal(
            PlaybackIsoBmffMediaTimeConversionResult.InvalidConfiguration,
            result);
        Assert.Null(time);
    }

    [Fact]
    public void TryConvert_MissingMediaTimingIsRejected()
    {
        var result = PlaybackIsoBmffMediaTimeConverter.TryConvert(
            0,
            null,
            out var time);

        Assert.Equal(
            PlaybackIsoBmffMediaTimeConversionResult.MissingMediaTiming,
            result);
        Assert.Null(time);
    }

    private static PlaybackIsoBmffMediaHeaderTimingBox ParseMediaTiming(
        uint timescale)
    {
        var box = new byte[32];
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Mdhd);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(20, 4), timescale);
        BinaryPrimitives.WriteUInt16BigEndian(
            box.AsSpan(28, 2),
            PackLanguage('u', 'n', 'd'));

        Assert.Equal(
            PlaybackIsoBmffMediaHeaderTimingBoxParseResult.Parsed,
            PlaybackIsoBmffMediaHeaderTimingBoxParser.Parse(box, out var parsed));
        return Assert.IsType<PlaybackIsoBmffMediaHeaderTimingBox>(parsed);
    }

    private static ushort PackLanguage(char first, char second, char third) =>
        (ushort)(((first - 'a' + 1) << 10) |
            ((second - 'a' + 1) << 5) |
            (third - 'a' + 1));
}
