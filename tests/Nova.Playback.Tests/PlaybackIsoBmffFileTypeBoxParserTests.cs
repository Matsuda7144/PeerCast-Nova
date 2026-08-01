using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffFileTypeBoxParserTests
{
    private const uint Ftyp = 0x66747970;
    private const uint Moov = 0x6d6f6f76;
    private const uint Isom = 0x69736f6d;
    private const uint Iso6 = 0x69736f36;
    private const uint Mp41 = 0x6d703431;

    [Fact]
    public void Parse_OrdinaryFileTypeReturnsBoundedBrandProjection()
    {
        var box = CreateBox(Ftyp, Isom, 512, [Isom, Iso6, Mp41]);

        var result = PlaybackIsoBmffFileTypeBoxParser.Parse(box, out var fileType);

        Assert.Equal(PlaybackIsoBmffFileTypeBoxParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffFileTypeBox>(fileType);
        Assert.Equal(Isom, parsed.MajorBrand);
        Assert.Equal((uint)512, parsed.MinorVersion);
        Assert.Equal([Isom, Iso6, Mp41], parsed.CompatibleBrands);
        Assert.IsNotType<uint[]>(parsed.CompatibleBrands);
    }

    [Fact]
    public void Parse_ExtendedAndExtendsToEndHeadersAreSupported()
    {
        var extended = CreateBox(Ftyp, Iso6, 1, [Iso6], extended: true);
        var toEnd = CreateBox(Ftyp, Mp41, 2, [], extendsToEnd: true);

        var extendedResult = PlaybackIsoBmffFileTypeBoxParser.Parse(
            extended,
            out var extendedType);
        var toEndResult = PlaybackIsoBmffFileTypeBoxParser.Parse(
            toEnd,
            out var toEndType);

        Assert.Equal(PlaybackIsoBmffFileTypeBoxParseResult.Parsed, extendedResult);
        Assert.Equal(Iso6, Assert.IsType<PlaybackIsoBmffFileTypeBox>(
            extendedType).MajorBrand);
        Assert.Equal(PlaybackIsoBmffFileTypeBoxParseResult.Parsed, toEndResult);
        Assert.Equal(Mp41, Assert.IsType<PlaybackIsoBmffFileTypeBox>(
            toEndType).MajorBrand);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(257)]
    public void Parse_InvalidBrandLimitRejectsBeforeInputAccess(int maximum)
    {
        var result = PlaybackIsoBmffFileTypeBoxParser.Parse(
            [],
            out var fileType,
            new() { MaximumCompatibleBrands = maximum });

        Assert.Equal(
            PlaybackIsoBmffFileTypeBoxParseResult.InvalidConfiguration,
            result);
        Assert.Null(fileType);
    }

    [Fact]
    public void Parse_CompatibleBrandLimitRejectsBeforeProjectionAllocation()
    {
        var box = CreateBox(Ftyp, Isom, 0, [Isom, Iso6]);

        var result = PlaybackIsoBmffFileTypeBoxParser.Parse(
            box,
            out var fileType,
            new() { MaximumCompatibleBrands = 1 });

        Assert.Equal(
            PlaybackIsoBmffFileTypeBoxParseResult.BrandLimitExceeded,
            result);
        Assert.Null(fileType);
    }

    [Fact]
    public void Parse_WrongBoxTypeIsRejected()
    {
        var box = CreateBox(Moov, Isom, 0, []);

        var result = PlaybackIsoBmffFileTypeBoxParser.Parse(box, out var fileType);

        Assert.Equal(PlaybackIsoBmffFileTypeBoxParseResult.WrongBoxType, result);
        Assert.Null(fileType);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(15)]
    public void Parse_IncompleteHeaderNeedsMoreData(int length)
    {
        var box = new byte[length];
        if (length >= 8)
        {
            BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), 1);
            BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Ftyp);
        }

        var result = PlaybackIsoBmffFileTypeBoxParser.Parse(box, out var fileType);

        Assert.Equal(PlaybackIsoBmffFileTypeBoxParseResult.NeedMoreData, result);
        Assert.Null(fileType);
    }

    [Fact]
    public void Parse_DeclaredSizeBeyondInputNeedsMoreData()
    {
        var box = CreateBox(Ftyp, Isom, 0, []);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), (uint)box.Length + 1);

        var result = PlaybackIsoBmffFileTypeBoxParser.Parse(box, out var fileType);

        Assert.Equal(PlaybackIsoBmffFileTypeBoxParseResult.NeedMoreData, result);
        Assert.Null(fileType);
    }

    [Fact]
    public void Parse_DeclaredSizeBeforeInputRejectsTrailingData()
    {
        var original = CreateBox(Ftyp, Isom, 0, []);
        var box = new byte[original.Length + 1];
        original.CopyTo(box, 0);

        var result = PlaybackIsoBmffFileTypeBoxParser.Parse(box, out var fileType);

        Assert.Equal(
            PlaybackIsoBmffFileTypeBoxParseResult.BoxLengthMismatch,
            result);
        Assert.Null(fileType);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(9)]
    public void Parse_InvalidPayloadShapeIsRejected(int payloadLength)
    {
        var box = new byte[8 + payloadLength];
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Ftyp);

        var result = PlaybackIsoBmffFileTypeBoxParser.Parse(box, out var fileType);

        Assert.Equal(PlaybackIsoBmffFileTypeBoxParseResult.InvalidPayload, result);
        Assert.Null(fileType);
    }

    private static byte[] CreateBox(
        uint type,
        uint majorBrand,
        uint minorVersion,
        uint[] brands,
        bool extended = false,
        bool extendsToEnd = false)
    {
        var headerSize = extended ? 16 : 8;
        var box = new byte[headerSize + 8 + (brands.Length * 4)];
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            extendsToEnd ? 0 : extended ? 1 : (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), type);
        if (extended)
        {
            BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(8, 8), (ulong)box.Length);
        }

        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(headerSize, 4),
            majorBrand);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(headerSize + 4, 4),
            minorVersion);
        for (var index = 0; index < brands.Length; index++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                box.AsSpan(headerSize + 8 + (index * 4), 4),
                brands[index]);
        }

        return box;
    }
}
