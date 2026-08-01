using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffBoxHeaderParserTests
{
    private const uint Ftyp = 0x66747970;
    private const uint Uuid = 0x75756964;

    [Fact]
    public void Parse_OrdinaryHeaderDoesNotRequirePayload()
    {
        var input = Header(size: 100, Ftyp);

        var result = PlaybackIsoBmffBoxHeaderParser.Parse(input, out var header);

        Assert.Equal(PlaybackIsoBmffBoxHeaderParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffBoxHeader>(header);
        Assert.Equal(Ftyp, parsed.Type);
        Assert.Equal((ulong)100, parsed.BoxSize);
        Assert.Equal(8, parsed.HeaderSize);
        Assert.Equal((ulong)92, parsed.PayloadSize);
        Assert.False(parsed.ExtendsToEnd);
        Assert.Null(parsed.UserType);
    }

    [Fact]
    public void Parse_ExtendedSizeUsesUnsignedBigEndianValue()
    {
        var input = ExtendedHeader(0x00000001_00000020, Ftyp);

        var result = PlaybackIsoBmffBoxHeaderParser.Parse(input, out var header);

        Assert.Equal(PlaybackIsoBmffBoxHeaderParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffBoxHeader>(header);
        Assert.Equal(0x00000001_00000020UL, parsed.BoxSize);
        Assert.Equal(16, parsed.HeaderSize);
        Assert.Equal(0x00000001_00000010UL, parsed.PayloadSize);
    }

    [Fact]
    public void Parse_ZeroSizeIsRepresentedAsExtendingToEnd()
    {
        var input = Header(size: 0, Ftyp);

        var result = PlaybackIsoBmffBoxHeaderParser.Parse(input, out var header);

        Assert.Equal(PlaybackIsoBmffBoxHeaderParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffBoxHeader>(header);
        Assert.True(parsed.ExtendsToEnd);
        Assert.Null(parsed.BoxSize);
        Assert.Null(parsed.PayloadSize);
        Assert.Equal(8, parsed.HeaderSize);
    }

    [Theory]
    [InlineData(false, 24)]
    [InlineData(true, 32)]
    public void Parse_UuidHeaderIncludesExactUserType(bool extended, int headerSize)
    {
        var input = extended
            ? ExtendedHeader((ulong)headerSize, Uuid, includeUserType: true)
            : Header((uint)headerSize, Uuid, includeUserType: true);

        var result = PlaybackIsoBmffBoxHeaderParser.Parse(input, out var header);

        Assert.Equal(PlaybackIsoBmffBoxHeaderParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffBoxHeader>(header);
        Assert.Equal(headerSize, parsed.HeaderSize);
        Assert.Equal((ulong)headerSize, parsed.BoxSize);
        Assert.Equal((ulong)0, parsed.PayloadSize);
        Assert.Equal(
            new PlaybackIsoBmffUserType(
                0x0102030405060708,
                0x1112131415161718),
            parsed.UserType);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    public void Parse_IncompleteBaseHeaderNeedsMoreData(int length)
    {
        var result = PlaybackIsoBmffBoxHeaderParser.Parse(
            new byte[length],
            out var header);

        Assert.Equal(PlaybackIsoBmffBoxHeaderParseResult.NeedMoreData, result);
        Assert.Null(header);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(15)]
    public void Parse_IncompleteExtendedHeaderNeedsMoreData(int length)
    {
        var input = new byte[length];
        BinaryPrimitives.WriteUInt32BigEndian(input.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(input.AsSpan(4, 4), Ftyp);

        var result = PlaybackIsoBmffBoxHeaderParser.Parse(input, out var header);

        Assert.Equal(PlaybackIsoBmffBoxHeaderParseResult.NeedMoreData, result);
        Assert.Null(header);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Parse_IncompleteUuidHeaderNeedsMoreData(bool extended)
    {
        var input = extended
            ? ExtendedHeader(32, Uuid)
            : Header(24, Uuid);

        var result = PlaybackIsoBmffBoxHeaderParser.Parse(input, out var header);

        Assert.Equal(PlaybackIsoBmffBoxHeaderParseResult.NeedMoreData, result);
        Assert.Null(header);
    }

    [Theory]
    [InlineData(2U, 0x66747970U)]
    [InlineData(7U, 0x66747970U)]
    [InlineData(8U, 0x75756964U)]
    [InlineData(23U, 0x75756964U)]
    public void Parse_UndersizedOrdinaryBoxIsRejected(uint size, uint type)
    {
        var result = PlaybackIsoBmffBoxHeaderParser.Parse(
            Header(size, type),
            out var header);

        Assert.Equal(PlaybackIsoBmffBoxHeaderParseResult.InvalidSize, result);
        Assert.Null(header);
    }

    [Theory]
    [InlineData(0UL, 0x66747970U)]
    [InlineData(15UL, 0x66747970U)]
    [InlineData(16UL, 0x75756964U)]
    [InlineData(31UL, 0x75756964U)]
    public void Parse_UndersizedExtendedBoxIsRejected(ulong size, uint type)
    {
        var result = PlaybackIsoBmffBoxHeaderParser.Parse(
            ExtendedHeader(size, type),
            out var header);

        Assert.Equal(PlaybackIsoBmffBoxHeaderParseResult.InvalidSize, result);
        Assert.Null(header);
    }

    private static byte[] Header(
        uint size,
        uint type,
        bool includeUserType = false)
    {
        var input = new byte[8 + (includeUserType ? 16 : 0)];
        BinaryPrimitives.WriteUInt32BigEndian(input.AsSpan(0, 4), size);
        BinaryPrimitives.WriteUInt32BigEndian(input.AsSpan(4, 4), type);
        if (includeUserType)
        {
            WriteUserType(input.AsSpan(8));
        }

        return input;
    }

    private static byte[] ExtendedHeader(
        ulong size,
        uint type,
        bool includeUserType = false)
    {
        var input = new byte[16 + (includeUserType ? 16 : 0)];
        BinaryPrimitives.WriteUInt32BigEndian(input.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(input.AsSpan(4, 4), type);
        BinaryPrimitives.WriteUInt64BigEndian(input.AsSpan(8, 8), size);
        if (includeUserType)
        {
            WriteUserType(input.AsSpan(16));
        }

        return input;
    }

    private static void WriteUserType(Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt64BigEndian(
            destination[..8],
            0x0102030405060708);
        BinaryPrimitives.WriteUInt64BigEndian(
            destination[8..16],
            0x1112131415161718);
    }
}
