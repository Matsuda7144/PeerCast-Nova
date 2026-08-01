using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffMovieHeaderTimingBoxParserTests
{
    private const uint Mvhd = 0x6d766864;
    private const uint Moov = 0x6d6f6f76;

    [Fact]
    public void Parse_Version0ProjectsUnsignedTimingFields()
    {
        var box = CreateBox(
            version: 0,
            creationTime: 1,
            modificationTime: uint.MaxValue,
            timescale: 1_000,
            duration: 12_345);

        var result = PlaybackIsoBmffMovieHeaderTimingBoxParser.Parse(
            box,
            out var timing);

        Assert.Equal(PlaybackIsoBmffMovieHeaderTimingBoxParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffMovieHeaderTimingBox>(timing);
        Assert.Equal((byte)0, parsed.Version);
        Assert.Equal((ulong)1, parsed.CreationTime);
        Assert.Equal((ulong)uint.MaxValue, parsed.ModificationTime);
        Assert.Equal((uint)1_000, parsed.Timescale);
        Assert.Equal((ulong)12_345, parsed.DurationUnits);
        Assert.True(parsed.HasKnownDuration);
    }

    [Fact]
    public void Parse_Version1ProjectsValuesBeyondThirtyTwoBits()
    {
        var box = CreateBox(
            version: 1,
            creationTime: 0x00000001_00000002,
            modificationTime: 0x00000003_00000004,
            timescale: 48_000,
            duration: 0x00000005_00000006,
            extended: true);

        var result = PlaybackIsoBmffMovieHeaderTimingBoxParser.Parse(
            box,
            out var timing);

        Assert.Equal(PlaybackIsoBmffMovieHeaderTimingBoxParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffMovieHeaderTimingBox>(timing);
        Assert.Equal((byte)1, parsed.Version);
        Assert.Equal(0x00000001_00000002UL, parsed.CreationTime);
        Assert.Equal(0x00000003_00000004UL, parsed.ModificationTime);
        Assert.Equal((uint)48_000, parsed.Timescale);
        Assert.Equal(0x00000005_00000006UL, parsed.DurationUnits);
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)1)]
    public void Parse_AllOnesDurationIsExplicitlyUnknown(byte version)
    {
        var box = CreateBox(
            version,
            creationTime: 0,
            modificationTime: 0,
            timescale: 1,
            duration: version == 0 ? uint.MaxValue : ulong.MaxValue,
            extendsToEnd: true);

        var result = PlaybackIsoBmffMovieHeaderTimingBoxParser.Parse(
            box,
            out var timing);

        Assert.Equal(PlaybackIsoBmffMovieHeaderTimingBoxParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffMovieHeaderTimingBox>(timing);
        Assert.Null(parsed.DurationUnits);
        Assert.False(parsed.HasKnownDuration);
    }

    [Fact]
    public void Parse_ZeroTimescaleIsRejected()
    {
        var box = CreateBox(0, 0, 0, 0, 10);

        var result = PlaybackIsoBmffMovieHeaderTimingBoxParser.Parse(
            box,
            out var timing);

        Assert.Equal(
            PlaybackIsoBmffMovieHeaderTimingBoxParseResult.InvalidTimescale,
            result);
        Assert.Null(timing);
    }

    [Fact]
    public void Parse_NonzeroFlagsAreRejected()
    {
        var box = CreateBox(0, 0, 0, 1, 10);
        box[11] = 1;

        var result = PlaybackIsoBmffMovieHeaderTimingBoxParser.Parse(
            box,
            out var timing);

        Assert.Equal(
            PlaybackIsoBmffMovieHeaderTimingBoxParseResult.NonzeroFlags,
            result);
        Assert.Null(timing);
    }

    [Fact]
    public void Parse_UnsupportedVersionIsRejectedBeforePayloadShape()
    {
        var box = CreateBox(0, 0, 0, 1, 10);
        box[8] = 2;

        var result = PlaybackIsoBmffMovieHeaderTimingBoxParser.Parse(
            box,
            out var timing);

        Assert.Equal(
            PlaybackIsoBmffMovieHeaderTimingBoxParseResult.UnsupportedVersion,
            result);
        Assert.Null(timing);
    }

    [Fact]
    public void Parse_WrongBoxTypeIsRejected()
    {
        var box = CreateBox(0, 0, 0, 1, 10);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Moov);

        var result = PlaybackIsoBmffMovieHeaderTimingBoxParser.Parse(
            box,
            out var timing);

        Assert.Equal(
            PlaybackIsoBmffMovieHeaderTimingBoxParseResult.WrongBoxType,
            result);
        Assert.Null(timing);
    }

    [Fact]
    public void Parse_DeclaredSizeBeyondInputNeedsMoreData()
    {
        var box = CreateBox(0, 0, 0, 1, 10);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            (uint)box.Length + 1);

        var result = PlaybackIsoBmffMovieHeaderTimingBoxParser.Parse(
            box,
            out var timing);

        Assert.Equal(
            PlaybackIsoBmffMovieHeaderTimingBoxParseResult.NeedMoreData,
            result);
        Assert.Null(timing);
    }

    [Fact]
    public void Parse_DeclaredSizeBeforeInputRejectsTrailingData()
    {
        var original = CreateBox(0, 0, 0, 1, 10);
        var box = new byte[original.Length + 1];
        original.CopyTo(box, 0);

        var result = PlaybackIsoBmffMovieHeaderTimingBoxParser.Parse(
            box,
            out var timing);

        Assert.Equal(
            PlaybackIsoBmffMovieHeaderTimingBoxParseResult.BoxLengthMismatch,
            result);
        Assert.Null(timing);
    }

    [Theory]
    [InlineData((byte)0, 99)]
    [InlineData((byte)0, 101)]
    [InlineData((byte)1, 111)]
    [InlineData((byte)1, 113)]
    public void Parse_NonexactVersionedPayloadLengthIsRejected(
        byte version,
        int payloadLength)
    {
        var box = new byte[8 + payloadLength];
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Mvhd);
        box[8] = version;

        var result = PlaybackIsoBmffMovieHeaderTimingBoxParser.Parse(
            box,
            out var timing);

        Assert.Equal(
            PlaybackIsoBmffMovieHeaderTimingBoxParseResult.InvalidPayload,
            result);
        Assert.Null(timing);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void Parse_TruncatedBoxHeaderNeedsMoreData(int length)
    {
        var result = PlaybackIsoBmffMovieHeaderTimingBoxParser.Parse(
            new byte[length],
            out var timing);

        Assert.Equal(
            PlaybackIsoBmffMovieHeaderTimingBoxParseResult.NeedMoreData,
            result);
        Assert.Null(timing);
    }

    private static byte[] CreateBox(
        byte version,
        ulong creationTime,
        ulong modificationTime,
        uint timescale,
        ulong duration,
        bool extended = false,
        bool extendsToEnd = false)
    {
        var headerSize = extended ? 16 : 8;
        var payloadSize = version == 1 ? 112 : 100;
        var box = new byte[headerSize + payloadSize];
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            extendsToEnd ? 0 : extended ? 1 : (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Mvhd);
        if (extended)
        {
            BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(8, 8), (ulong)box.Length);
        }

        var payload = box.AsSpan(headerSize);
        payload[0] = version;
        if (version == 0)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                payload.Slice(4, 4),
                checked((uint)creationTime));
            BinaryPrimitives.WriteUInt32BigEndian(
                payload.Slice(8, 4),
                checked((uint)modificationTime));
            BinaryPrimitives.WriteUInt32BigEndian(payload.Slice(12, 4), timescale);
            BinaryPrimitives.WriteUInt32BigEndian(
                payload.Slice(16, 4),
                checked((uint)duration));
        }
        else
        {
            BinaryPrimitives.WriteUInt64BigEndian(payload.Slice(4, 8), creationTime);
            BinaryPrimitives.WriteUInt64BigEndian(payload.Slice(12, 8), modificationTime);
            BinaryPrimitives.WriteUInt32BigEndian(payload.Slice(20, 4), timescale);
            BinaryPrimitives.WriteUInt64BigEndian(payload.Slice(24, 8), duration);
        }

        return box;
    }
}
