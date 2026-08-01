using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffCompactSampleSizeBoxParserTests
{
    private const uint Stz2 = 0x73747A32;
    private const uint Stbl = 0x7374626C;

    [Theory]
    [InlineData(4, new ushort[] { 1, 2, 15 })]
    [InlineData(8, new ushort[] { 0, 128, 255 })]
    [InlineData(16, new ushort[] { 0, 256, ushort.MaxValue })]
    public void Parse_ProjectsSupportedPackedSizes(
        byte fieldSize,
        ushort[] sizes)
    {
        var result = PlaybackIsoBmffCompactSampleSizeBoxParser.Parse(
            CreateBox(fieldSize, sizes),
            out var compactSampleSize);

        Assert.Equal(
            PlaybackIsoBmffCompactSampleSizeBoxParseResult.Parsed,
            result);
        var parsed = Assert.IsType<PlaybackIsoBmffCompactSampleSizeBox>(
            compactSampleSize);
        Assert.Equal(fieldSize, parsed.FieldSize);
        Assert.Equal((uint)sizes.Length, parsed.SampleCount);
        Assert.Equal(sizes, parsed.SampleSizes);
        Assert.IsNotType<ushort[]>(parsed.SampleSizes);
        Assert.Equal(
            sizes.Aggregate<ushort, ulong>(0, (total, size) => total + size),
            parsed.TotalSampleBytes);
    }

    [Fact]
    public void Parse_OddFourBitTableIgnoresUnusedLowNibble()
    {
        var box = CreateBox(4, [1]);
        box[^1] |= 0x0f;

        var result = PlaybackIsoBmffCompactSampleSizeBoxParser.Parse(
            box,
            out var compactSampleSize);

        Assert.Equal(
            PlaybackIsoBmffCompactSampleSizeBoxParseResult.Parsed,
            result);
        Assert.Equal(
            [1U],
            Assert.IsType<PlaybackIsoBmffCompactSampleSizeBox>(
                compactSampleSize).SampleSizes.Select(value => (uint)value));
    }

    [Fact]
    public void Parse_EmptyTableIsValid()
    {
        var result = PlaybackIsoBmffCompactSampleSizeBoxParser.Parse(
            CreateBox(4, []),
            out var compactSampleSize);

        Assert.Equal(
            PlaybackIsoBmffCompactSampleSizeBoxParseResult.Parsed,
            result);
        Assert.Empty(
            Assert.IsType<PlaybackIsoBmffCompactSampleSizeBox>(
                compactSampleSize).SampleSizes);
    }

    [Fact]
    public void Parse_ExtendedAndExtendsToEndHeadersAreSupported()
    {
        var extended = CreateBox(8, [1, 2], extended: true);
        var toEnd = CreateBox(16, [3, 4], extendsToEnd: true);

        Assert.Equal(
            PlaybackIsoBmffCompactSampleSizeBoxParseResult.Parsed,
            PlaybackIsoBmffCompactSampleSizeBoxParser.Parse(
                extended,
                out var extendedTable));
        Assert.Equal(
            [1U, 2U],
            Assert.IsType<PlaybackIsoBmffCompactSampleSizeBox>(
                extendedTable).SampleSizes.Select(value => (uint)value));
        Assert.Equal(
            PlaybackIsoBmffCompactSampleSizeBoxParseResult.Parsed,
            PlaybackIsoBmffCompactSampleSizeBoxParser.Parse(
                toEnd,
                out var toEndTable));
        Assert.Equal(
            [3U, 4U],
            Assert.IsType<PlaybackIsoBmffCompactSampleSizeBox>(
                toEndTable).SampleSizes.Select(value => (uint)value));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65537)]
    public void Parse_InvalidEntryLimitRejectsBeforeInputAccess(int maximum)
    {
        var result = PlaybackIsoBmffCompactSampleSizeBoxParser.Parse(
            [],
            out var compactSampleSize,
            new() { MaximumEntryCount = maximum });

        Assert.Equal(
            PlaybackIsoBmffCompactSampleSizeBoxParseResult.InvalidConfiguration,
            result);
        Assert.Null(compactSampleSize);
    }

    [Fact]
    public void Parse_EntryLimitRejectsBeforeProjection()
    {
        var result = PlaybackIsoBmffCompactSampleSizeBoxParser.Parse(
            CreateBox(8, [1, 2]),
            out var compactSampleSize,
            new() { MaximumEntryCount = 1 });

        Assert.Equal(
            PlaybackIsoBmffCompactSampleSizeBoxParseResult.EntryLimitExceeded,
            result);
        Assert.Null(compactSampleSize);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Parse_PayloadLengthMustBeExact(int adjustment)
    {
        var original = CreateBox(16, [1]);
        var box = ResizeCompleteBox(original, adjustment);

        var result = PlaybackIsoBmffCompactSampleSizeBoxParser.Parse(
            box,
            out var compactSampleSize);

        Assert.Equal(
            PlaybackIsoBmffCompactSampleSizeBoxParseResult.InvalidPayload,
            result);
        Assert.Null(compactSampleSize);
    }

    [Fact]
    public void Parse_NonzeroReservedAndUnsupportedFieldSizeAreDistinct()
    {
        var reserved = CreateBox(8, []);
        reserved[12] = 1;
        var fieldSize = CreateBox(8, []);
        fieldSize[15] = 32;

        Assert.Equal(
            PlaybackIsoBmffCompactSampleSizeBoxParseResult.NonzeroReservedFields,
            PlaybackIsoBmffCompactSampleSizeBoxParser.Parse(
                reserved,
                out var reservedTable));
        Assert.Null(reservedTable);
        Assert.Equal(
            PlaybackIsoBmffCompactSampleSizeBoxParseResult.UnsupportedFieldSize,
            PlaybackIsoBmffCompactSampleSizeBoxParser.Parse(
                fieldSize,
                out var fieldSizeTable));
        Assert.Null(fieldSizeTable);
    }

    [Fact]
    public void Parse_UnsupportedVersionAndNonzeroFlagsAreDistinct()
    {
        var version = CreateBox(8, []);
        version[8] = 1;
        var flags = CreateBox(8, []);
        flags[11] = 1;

        Assert.Equal(
            PlaybackIsoBmffCompactSampleSizeBoxParseResult.UnsupportedVersion,
            PlaybackIsoBmffCompactSampleSizeBoxParser.Parse(
                version,
                out var versionTable));
        Assert.Null(versionTable);
        Assert.Equal(
            PlaybackIsoBmffCompactSampleSizeBoxParseResult.NonzeroFlags,
            PlaybackIsoBmffCompactSampleSizeBoxParser.Parse(
                flags,
                out var flagsTable));
        Assert.Null(flagsTable);
    }

    [Fact]
    public void Parse_WrongTypeAndCompleteLengthFailuresAreDistinct()
    {
        var wrongType = CreateBox(8, []);
        BinaryPrimitives.WriteUInt32BigEndian(wrongType.AsSpan(4, 4), Stbl);
        var shortInput = CreateBox(8, []);
        BinaryPrimitives.WriteUInt32BigEndian(
            shortInput.AsSpan(0, 4),
            (uint)shortInput.Length + 1);
        var original = CreateBox(8, []);
        var trailingInput = new byte[original.Length + 1];
        original.CopyTo(trailingInput, 0);

        Assert.Equal(
            PlaybackIsoBmffCompactSampleSizeBoxParseResult.WrongBoxType,
            PlaybackIsoBmffCompactSampleSizeBoxParser.Parse(
                wrongType,
                out var wrongTable));
        Assert.Null(wrongTable);
        Assert.Equal(
            PlaybackIsoBmffCompactSampleSizeBoxParseResult.NeedMoreData,
            PlaybackIsoBmffCompactSampleSizeBoxParser.Parse(
                shortInput,
                out var shortTable));
        Assert.Null(shortTable);
        Assert.Equal(
            PlaybackIsoBmffCompactSampleSizeBoxParseResult.BoxLengthMismatch,
            PlaybackIsoBmffCompactSampleSizeBoxParser.Parse(
                trailingInput,
                out var trailingTable));
        Assert.Null(trailingTable);
    }

    private static byte[] CreateBox(
        byte fieldSize,
        ushort[] sizes,
        bool extended = false,
        bool extendsToEnd = false)
    {
        var headerSize = extended ? 16 : 8;
        var entryByteCount = fieldSize switch
        {
            4 => (sizes.Length + 1) / 2,
            8 => sizes.Length,
            _ => sizes.Length * 2,
        };
        var box = new byte[headerSize + 12 + entryByteCount];
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            extendsToEnd ? 0 : extended ? 1 : (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Stz2);
        if (extended)
        {
            BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(8, 8), (ulong)box.Length);
        }

        box[headerSize + 7] = fieldSize;
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(headerSize + 8, 4),
            (uint)sizes.Length);
        for (var index = 0; index < sizes.Length; index++)
        {
            if (fieldSize == 4)
            {
                box[headerSize + 12 + (index / 2)] |=
                    (byte)(sizes[index] << (index % 2 == 0 ? 4 : 0));
            }
            else if (fieldSize == 8)
            {
                box[headerSize + 12 + index] = (byte)sizes[index];
            }
            else
            {
                BinaryPrimitives.WriteUInt16BigEndian(
                    box.AsSpan(headerSize + 12 + (index * 2), 2),
                    sizes[index]);
            }
        }

        return box;
    }

    private static byte[] ResizeCompleteBox(byte[] original, int adjustment)
    {
        var box = new byte[original.Length + adjustment];
        original.AsSpan(0, Math.Min(original.Length, box.Length)).CopyTo(box);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), (uint)box.Length);
        return box;
    }
}
