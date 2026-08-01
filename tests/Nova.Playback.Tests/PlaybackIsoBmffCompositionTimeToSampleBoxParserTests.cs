using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffCompositionTimeToSampleBoxParserTests
{
    private const uint Ctts = 0x63747473;
    private const uint Stbl = 0x7374626c;

    [Fact]
    public void Parse_Version0ProjectsUnsignedOffsets()
    {
        var box = CreateBox(0, (3, 0), (2, uint.MaxValue));

        var result = PlaybackIsoBmffCompositionTimeToSampleBoxParser.Parse(
            box,
            out var compositionTime);

        Assert.Equal(
            PlaybackIsoBmffCompositionTimeToSampleBoxParseResult.Parsed,
            result);
        var parsed = Assert.IsType<PlaybackIsoBmffCompositionTimeToSampleBox>(
            compositionTime);
        Assert.Equal((byte)0, parsed.Version);
        Assert.Equal(
            [
                new PlaybackIsoBmffCompositionTimeToSampleEntry(3, 0),
                new PlaybackIsoBmffCompositionTimeToSampleEntry(2, uint.MaxValue),
            ],
            parsed.Entries);
        Assert.IsNotType<PlaybackIsoBmffCompositionTimeToSampleEntry[]>(
            parsed.Entries);
        Assert.Equal((ulong)5, parsed.TotalSampleCount);
    }

    [Fact]
    public void Parse_Version1SignExtendsOffsets()
    {
        var box = CreateBox(
            1,
            (1, unchecked((uint)int.MinValue)),
            (2, unchecked((uint)-1)),
            (3, int.MaxValue));

        var result = PlaybackIsoBmffCompositionTimeToSampleBoxParser.Parse(
            box,
            out var compositionTime);

        Assert.Equal(
            PlaybackIsoBmffCompositionTimeToSampleBoxParseResult.Parsed,
            result);
        var parsed = Assert.IsType<PlaybackIsoBmffCompositionTimeToSampleBox>(
            compositionTime);
        Assert.Equal((byte)1, parsed.Version);
        Assert.Equal((long)int.MinValue, parsed.Entries[0].SampleOffset);
        Assert.Equal((long)-1, parsed.Entries[1].SampleOffset);
        Assert.Equal((long)int.MaxValue, parsed.Entries[2].SampleOffset);
        Assert.Equal((ulong)6, parsed.TotalSampleCount);
    }

    [Fact]
    public void Parse_EmptyTableIsValid()
    {
        var result = PlaybackIsoBmffCompositionTimeToSampleBoxParser.Parse(
            CreateBox(1),
            out var compositionTime);

        Assert.Equal(
            PlaybackIsoBmffCompositionTimeToSampleBoxParseResult.Parsed,
            result);
        var parsed = Assert.IsType<PlaybackIsoBmffCompositionTimeToSampleBox>(
            compositionTime);
        Assert.Empty(parsed.Entries);
        Assert.Equal((ulong)0, parsed.TotalSampleCount);
    }

    [Fact]
    public void Parse_ExtendedAndExtendsToEndHeadersAreSupported()
    {
        var extended = CreateBox(0, [(1, 2)], extended: true);
        var toEnd = CreateBox(1, [(2, unchecked((uint)-2))], extendsToEnd: true);

        Assert.Equal(
            PlaybackIsoBmffCompositionTimeToSampleBoxParseResult.Parsed,
            PlaybackIsoBmffCompositionTimeToSampleBoxParser.Parse(
                extended,
                out var extendedTable));
        Assert.Single(Assert.IsType<PlaybackIsoBmffCompositionTimeToSampleBox>(
            extendedTable).Entries);
        Assert.Equal(
            PlaybackIsoBmffCompositionTimeToSampleBoxParseResult.Parsed,
            PlaybackIsoBmffCompositionTimeToSampleBoxParser.Parse(
                toEnd,
                out var toEndTable));
        Assert.Equal(
            (long)-2,
            Assert.IsType<PlaybackIsoBmffCompositionTimeToSampleBox>(
                toEndTable).Entries[0].SampleOffset);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65537)]
    public void Parse_InvalidEntryLimitRejectsBeforeInputAccess(int maximum)
    {
        var result = PlaybackIsoBmffCompositionTimeToSampleBoxParser.Parse(
            [],
            out var table,
            new() { MaximumEntryCount = maximum });

        Assert.Equal(
            PlaybackIsoBmffCompositionTimeToSampleBoxParseResult
                .InvalidConfiguration,
            result);
        Assert.Null(table);
    }

    [Fact]
    public void Parse_EntryLimitAndZeroSampleCountAreRejected()
    {
        var saturated = CreateBox(0, (1, 1), (1, 1));
        var zeroCount = CreateBox(0, (0, 1));

        Assert.Equal(
            PlaybackIsoBmffCompositionTimeToSampleBoxParseResult.EntryLimitExceeded,
            PlaybackIsoBmffCompositionTimeToSampleBoxParser.Parse(
                saturated,
                out var saturatedTable,
                new() { MaximumEntryCount = 1 }));
        Assert.Null(saturatedTable);
        Assert.Equal(
            PlaybackIsoBmffCompositionTimeToSampleBoxParseResult.InvalidSampleCount,
            PlaybackIsoBmffCompositionTimeToSampleBoxParser.Parse(
                zeroCount,
                out var zeroCountTable));
        Assert.Null(zeroCountTable);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Parse_EntryPayloadLengthMustBeExact(int adjustment)
    {
        var original = CreateBox(0, (1, 1));
        var box = new byte[original.Length + adjustment];
        original.AsSpan(0, Math.Min(original.Length, box.Length)).CopyTo(box);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), (uint)box.Length);

        var result = PlaybackIsoBmffCompositionTimeToSampleBoxParser.Parse(
            box,
            out var table);

        Assert.Equal(
            PlaybackIsoBmffCompositionTimeToSampleBoxParseResult.InvalidPayload,
            result);
        Assert.Null(table);
    }

    [Fact]
    public void Parse_UnsupportedVersionAndNonzeroFlagsAreDistinct()
    {
        var version = CreateBox(0);
        version[8] = 2;
        var flags = CreateBox(0);
        flags[11] = 1;

        Assert.Equal(
            PlaybackIsoBmffCompositionTimeToSampleBoxParseResult.UnsupportedVersion,
            PlaybackIsoBmffCompositionTimeToSampleBoxParser.Parse(
                version,
                out var versionTable));
        Assert.Null(versionTable);
        Assert.Equal(
            PlaybackIsoBmffCompositionTimeToSampleBoxParseResult.NonzeroFlags,
            PlaybackIsoBmffCompositionTimeToSampleBoxParser.Parse(
                flags,
                out var flagsTable));
        Assert.Null(flagsTable);
    }

    [Fact]
    public void Parse_WrongTypeAndCompleteLengthFailuresAreDistinct()
    {
        var wrongType = CreateBox(0);
        BinaryPrimitives.WriteUInt32BigEndian(wrongType.AsSpan(4, 4), Stbl);
        var shortInput = CreateBox(0);
        BinaryPrimitives.WriteUInt32BigEndian(
            shortInput.AsSpan(0, 4),
            (uint)shortInput.Length + 1);
        var original = CreateBox(0);
        var trailingInput = new byte[original.Length + 1];
        original.CopyTo(trailingInput, 0);

        Assert.Equal(
            PlaybackIsoBmffCompositionTimeToSampleBoxParseResult.WrongBoxType,
            PlaybackIsoBmffCompositionTimeToSampleBoxParser.Parse(
                wrongType,
                out var wrongTable));
        Assert.Null(wrongTable);
        Assert.Equal(
            PlaybackIsoBmffCompositionTimeToSampleBoxParseResult.NeedMoreData,
            PlaybackIsoBmffCompositionTimeToSampleBoxParser.Parse(
                shortInput,
                out var shortTable));
        Assert.Null(shortTable);
        Assert.Equal(
            PlaybackIsoBmffCompositionTimeToSampleBoxParseResult.BoxLengthMismatch,
            PlaybackIsoBmffCompositionTimeToSampleBoxParser.Parse(
                trailingInput,
                out var trailingTable));
        Assert.Null(trailingTable);
    }

    private static byte[] CreateBox(
        byte version,
        params (uint Count, uint Offset)[] entries) =>
        CreateBox(version, entries, extended: false, extendsToEnd: false);

    private static byte[] CreateBox(
        byte version,
        (uint Count, uint Offset)[] entries,
        bool extended = false,
        bool extendsToEnd = false)
    {
        var headerSize = extended ? 16 : 8;
        var box = new byte[headerSize + 8 + (entries.Length * 8)];
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            extendsToEnd ? 0 : extended ? 1 : (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Ctts);
        if (extended)
        {
            BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(8, 8), (ulong)box.Length);
        }

        box[headerSize] = version;
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(headerSize + 4, 4),
            (uint)entries.Length);
        for (var index = 0; index < entries.Length; index++)
        {
            var offset = headerSize + 8 + (index * 8);
            BinaryPrimitives.WriteUInt32BigEndian(
                box.AsSpan(offset, 4),
                entries[index].Count);
            BinaryPrimitives.WriteUInt32BigEndian(
                box.AsSpan(offset + 4, 4),
                entries[index].Offset);
        }

        return box;
    }
}
