using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffMediaHeaderTimingBoxParserTests
{
    private const uint Mdhd = 0x6d646864;
    private const uint Mdia = 0x6d646961;

    [Fact]
    public void Parse_Version0ProjectsTimingAndLanguage()
    {
        var box = CreateBox(
            version: 0,
            creationTime: 1,
            modificationTime: uint.MaxValue,
            timescale: 48_000,
            duration: 96_000,
            language: "und");

        var result = PlaybackIsoBmffMediaHeaderTimingBoxParser.Parse(
            box,
            out var timing);

        Assert.Equal(PlaybackIsoBmffMediaHeaderTimingBoxParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffMediaHeaderTimingBox>(timing);
        Assert.Equal((byte)0, parsed.Version);
        Assert.Equal((ulong)1, parsed.CreationTime);
        Assert.Equal((ulong)uint.MaxValue, parsed.ModificationTime);
        Assert.Equal((uint)48_000, parsed.Timescale);
        Assert.Equal((ulong)96_000, parsed.DurationUnits);
        Assert.True(parsed.HasKnownDuration);
        Assert.Equal("und", parsed.Language);
    }

    [Fact]
    public void Parse_Version1ProjectsValuesBeyondThirtyTwoBits()
    {
        var box = CreateBox(
            version: 1,
            creationTime: 0x00000001_00000002,
            modificationTime: 0x00000003_00000004,
            timescale: 90_000,
            duration: 0x00000005_00000006,
            language: "jpn",
            extended: true);

        var result = PlaybackIsoBmffMediaHeaderTimingBoxParser.Parse(
            box,
            out var timing);

        Assert.Equal(PlaybackIsoBmffMediaHeaderTimingBoxParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffMediaHeaderTimingBox>(timing);
        Assert.Equal((byte)1, parsed.Version);
        Assert.Equal(0x00000001_00000002UL, parsed.CreationTime);
        Assert.Equal(0x00000003_00000004UL, parsed.ModificationTime);
        Assert.Equal((uint)90_000, parsed.Timescale);
        Assert.Equal(0x00000005_00000006UL, parsed.DurationUnits);
        Assert.Equal("jpn", parsed.Language);
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)1)]
    public void Parse_AllOnesDurationIsExplicitlyUnknown(byte version)
    {
        var box = CreateBox(
            version,
            0,
            0,
            1,
            version == 0 ? uint.MaxValue : ulong.MaxValue,
            "eng",
            extendsToEnd: true);

        var result = PlaybackIsoBmffMediaHeaderTimingBoxParser.Parse(
            box,
            out var timing);

        Assert.Equal(PlaybackIsoBmffMediaHeaderTimingBoxParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffMediaHeaderTimingBox>(timing);
        Assert.Null(parsed.DurationUnits);
        Assert.False(parsed.HasKnownDuration);
    }

    [Theory]
    [InlineData(0x0000)]
    [InlineData(0x8000)]
    [InlineData(0x7fff)]
    public void Parse_InvalidPackedLanguageIsRejected(ushort packedLanguage)
    {
        var box = CreateBox(0, 0, 0, 1, 10, "und");
        BinaryPrimitives.WriteUInt16BigEndian(
            box.AsSpan(28, 2),
            packedLanguage);

        var result = PlaybackIsoBmffMediaHeaderTimingBoxParser.Parse(
            box,
            out var timing);

        Assert.Equal(
            PlaybackIsoBmffMediaHeaderTimingBoxParseResult.InvalidLanguage,
            result);
        Assert.Null(timing);
    }

    [Fact]
    public void Parse_NonzeroPredefinedFieldIsRejected()
    {
        var box = CreateBox(0, 0, 0, 1, 10, "und");
        box[31] = 1;

        var result = PlaybackIsoBmffMediaHeaderTimingBoxParser.Parse(
            box,
            out var timing);

        Assert.Equal(
            PlaybackIsoBmffMediaHeaderTimingBoxParseResult.InvalidPayload,
            result);
        Assert.Null(timing);
    }

    [Fact]
    public void Parse_ZeroTimescaleIsRejected()
    {
        var box = CreateBox(0, 0, 0, 0, 10, "und");

        var result = PlaybackIsoBmffMediaHeaderTimingBoxParser.Parse(
            box,
            out var timing);

        Assert.Equal(
            PlaybackIsoBmffMediaHeaderTimingBoxParseResult.InvalidTimescale,
            result);
        Assert.Null(timing);
    }

    [Fact]
    public void Parse_NonzeroFlagsAreRejected()
    {
        var box = CreateBox(0, 0, 0, 1, 10, "und");
        box[11] = 1;

        var result = PlaybackIsoBmffMediaHeaderTimingBoxParser.Parse(
            box,
            out var timing);

        Assert.Equal(
            PlaybackIsoBmffMediaHeaderTimingBoxParseResult.NonzeroFlags,
            result);
        Assert.Null(timing);
    }

    [Fact]
    public void Parse_UnsupportedVersionIsRejected()
    {
        var box = CreateBox(0, 0, 0, 1, 10, "und");
        box[8] = 2;

        var result = PlaybackIsoBmffMediaHeaderTimingBoxParser.Parse(
            box,
            out var timing);

        Assert.Equal(
            PlaybackIsoBmffMediaHeaderTimingBoxParseResult.UnsupportedVersion,
            result);
        Assert.Null(timing);
    }

    [Fact]
    public void Parse_WrongBoxTypeIsRejected()
    {
        var box = CreateBox(0, 0, 0, 1, 10, "und");
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Mdia);

        var result = PlaybackIsoBmffMediaHeaderTimingBoxParser.Parse(
            box,
            out var timing);

        Assert.Equal(
            PlaybackIsoBmffMediaHeaderTimingBoxParseResult.WrongBoxType,
            result);
        Assert.Null(timing);
    }

    [Fact]
    public void Parse_DeclaredSizeBeyondInputNeedsMoreData()
    {
        var box = CreateBox(0, 0, 0, 1, 10, "und");
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            (uint)box.Length + 1);

        var result = PlaybackIsoBmffMediaHeaderTimingBoxParser.Parse(
            box,
            out var timing);

        Assert.Equal(
            PlaybackIsoBmffMediaHeaderTimingBoxParseResult.NeedMoreData,
            result);
        Assert.Null(timing);
    }

    [Fact]
    public void Parse_DeclaredSizeBeforeInputRejectsTrailingData()
    {
        var original = CreateBox(0, 0, 0, 1, 10, "und");
        var box = new byte[original.Length + 1];
        original.CopyTo(box, 0);

        var result = PlaybackIsoBmffMediaHeaderTimingBoxParser.Parse(
            box,
            out var timing);

        Assert.Equal(
            PlaybackIsoBmffMediaHeaderTimingBoxParseResult.BoxLengthMismatch,
            result);
        Assert.Null(timing);
    }

    [Theory]
    [InlineData((byte)0, 23)]
    [InlineData((byte)0, 25)]
    [InlineData((byte)1, 35)]
    [InlineData((byte)1, 37)]
    public void Parse_NonexactVersionedPayloadLengthIsRejected(
        byte version,
        int payloadLength)
    {
        var box = new byte[8 + payloadLength];
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Mdhd);
        box[8] = version;

        var result = PlaybackIsoBmffMediaHeaderTimingBoxParser.Parse(
            box,
            out var timing);

        Assert.Equal(
            PlaybackIsoBmffMediaHeaderTimingBoxParseResult.InvalidPayload,
            result);
        Assert.Null(timing);
    }

    private static byte[] CreateBox(
        byte version,
        ulong creationTime,
        ulong modificationTime,
        uint timescale,
        ulong duration,
        string language,
        bool extended = false,
        bool extendsToEnd = false)
    {
        var headerSize = extended ? 16 : 8;
        var payloadSize = version == 1 ? 36 : 24;
        var box = new byte[headerSize + payloadSize];
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            extendsToEnd ? 0 : extended ? 1 : (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Mdhd);
        if (extended)
        {
            BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(8, 8), (ulong)box.Length);
        }

        var payload = box.AsSpan(headerSize);
        payload[0] = version;
        int languageOffset;
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
            languageOffset = 20;
        }
        else
        {
            BinaryPrimitives.WriteUInt64BigEndian(payload.Slice(4, 8), creationTime);
            BinaryPrimitives.WriteUInt64BigEndian(payload.Slice(12, 8), modificationTime);
            BinaryPrimitives.WriteUInt32BigEndian(payload.Slice(20, 4), timescale);
            BinaryPrimitives.WriteUInt64BigEndian(payload.Slice(24, 8), duration);
            languageOffset = 32;
        }

        BinaryPrimitives.WriteUInt16BigEndian(
            payload.Slice(languageOffset, 2),
            PackLanguage(language));
        return box;
    }

    private static ushort PackLanguage(string language) =>
        checked((ushort)(
            ((language[0] - 'a' + 1) << 10) |
            ((language[1] - 'a' + 1) << 5) |
            (language[2] - 'a' + 1)));
}
