using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffTimeToSampleBoxParserTests
{
    private const uint Stts = 0x73747473;
    private const uint Stbl = 0x7374626c;

    [Fact]
    public void Parse_ProjectsEntriesAndExactAggregates()
    {
        var box = CreateBox((3, 1_000), (2, 2_000));

        var result = PlaybackIsoBmffTimeToSampleBoxParser.Parse(
            box,
            out var timeToSample);

        Assert.Equal(PlaybackIsoBmffTimeToSampleBoxParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffTimeToSampleBox>(timeToSample);
        Assert.Equal(
            [
                new PlaybackIsoBmffTimeToSampleEntry(3, 1_000),
                new PlaybackIsoBmffTimeToSampleEntry(2, 2_000),
            ],
            parsed.Entries);
        Assert.IsNotType<PlaybackIsoBmffTimeToSampleEntry[]>(parsed.Entries);
        Assert.Equal((ulong)5, parsed.TotalSampleCount);
        Assert.Equal((ulong)7_000, parsed.TotalDurationUnits);
    }

    [Fact]
    public void Parse_EmptyTableHasZeroAggregates()
    {
        var result = PlaybackIsoBmffTimeToSampleBoxParser.Parse(
            CreateBox(),
            out var timeToSample);

        Assert.Equal(PlaybackIsoBmffTimeToSampleBoxParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffTimeToSampleBox>(timeToSample);
        Assert.Empty(parsed.Entries);
        Assert.Equal((ulong)0, parsed.TotalSampleCount);
        Assert.Equal((ulong)0, parsed.TotalDurationUnits);
    }

    [Fact]
    public void Parse_ZeroDeltaIsRetained()
    {
        var result = PlaybackIsoBmffTimeToSampleBoxParser.Parse(
            CreateBox((1, 0)),
            out var timeToSample);

        Assert.Equal(PlaybackIsoBmffTimeToSampleBoxParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffTimeToSampleBox>(timeToSample);
        Assert.Equal((uint)0, parsed.Entries[0].SampleDelta);
        Assert.Equal((ulong)0, parsed.TotalDurationUnits);
    }

    [Fact]
    public void Parse_ExtendedAndExtendsToEndHeadersAreSupported()
    {
        var extended = CreateBox([(1, 1)], extended: true);
        var toEnd = CreateBox([(2, 3)], extendsToEnd: true);

        Assert.Equal(
            PlaybackIsoBmffTimeToSampleBoxParseResult.Parsed,
            PlaybackIsoBmffTimeToSampleBoxParser.Parse(
                extended,
                out var extendedTable));
        Assert.Single(
            Assert.IsType<PlaybackIsoBmffTimeToSampleBox>(extendedTable).Entries);
        Assert.Equal(
            PlaybackIsoBmffTimeToSampleBoxParseResult.Parsed,
            PlaybackIsoBmffTimeToSampleBoxParser.Parse(
                toEnd,
                out var toEndTable));
        Assert.Equal(
            (ulong)6,
            Assert.IsType<PlaybackIsoBmffTimeToSampleBox>(
                toEndTable).TotalDurationUnits);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65537)]
    public void Parse_InvalidEntryLimitRejectsBeforeInputAccess(int maximum)
    {
        var result = PlaybackIsoBmffTimeToSampleBoxParser.Parse(
            [],
            out var table,
            new() { MaximumEntryCount = maximum });

        Assert.Equal(
            PlaybackIsoBmffTimeToSampleBoxParseResult.InvalidConfiguration,
            result);
        Assert.Null(table);
    }

    [Fact]
    public void Parse_EntryLimitRejectsBeforeProjection()
    {
        var box = CreateBox((1, 1), (1, 1));

        var result = PlaybackIsoBmffTimeToSampleBoxParser.Parse(
            box,
            out var table,
            new() { MaximumEntryCount = 1 });

        Assert.Equal(
            PlaybackIsoBmffTimeToSampleBoxParseResult.EntryLimitExceeded,
            result);
        Assert.Null(table);
    }

    [Fact]
    public void Parse_ZeroSampleCountIsRejected()
    {
        var box = CreateBox((0, 1));

        var result = PlaybackIsoBmffTimeToSampleBoxParser.Parse(
            box,
            out var table);

        Assert.Equal(
            PlaybackIsoBmffTimeToSampleBoxParseResult.InvalidSampleCount,
            result);
        Assert.Null(table);
    }

    [Fact]
    public void Parse_TotalDurationOverflowIsRejected()
    {
        var box = CreateBox(
            (uint.MaxValue, uint.MaxValue),
            (uint.MaxValue, uint.MaxValue));

        var result = PlaybackIsoBmffTimeToSampleBoxParser.Parse(
            box,
            out var table);

        Assert.Equal(
            PlaybackIsoBmffTimeToSampleBoxParseResult.AggregateOverflow,
            result);
        Assert.Null(table);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Parse_EntryPayloadLengthMustBeExact(int adjustment)
    {
        var original = CreateBox((1, 1));
        var box = new byte[original.Length + adjustment];
        original.AsSpan(0, Math.Min(original.Length, box.Length)).CopyTo(box);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), (uint)box.Length);

        var result = PlaybackIsoBmffTimeToSampleBoxParser.Parse(
            box,
            out var table);

        Assert.Equal(PlaybackIsoBmffTimeToSampleBoxParseResult.InvalidPayload, result);
        Assert.Null(table);
    }

    [Fact]
    public void Parse_UnsupportedVersionAndNonzeroFlagsAreDistinct()
    {
        var version = CreateBox();
        version[8] = 1;
        var flags = CreateBox();
        flags[11] = 1;

        Assert.Equal(
            PlaybackIsoBmffTimeToSampleBoxParseResult.UnsupportedVersion,
            PlaybackIsoBmffTimeToSampleBoxParser.Parse(
                version,
                out var versionTable));
        Assert.Null(versionTable);
        Assert.Equal(
            PlaybackIsoBmffTimeToSampleBoxParseResult.NonzeroFlags,
            PlaybackIsoBmffTimeToSampleBoxParser.Parse(
                flags,
                out var flagsTable));
        Assert.Null(flagsTable);
    }

    [Fact]
    public void Parse_WrongTypeAndCompleteLengthFailuresAreDistinct()
    {
        var wrongType = CreateBox();
        BinaryPrimitives.WriteUInt32BigEndian(wrongType.AsSpan(4, 4), Stbl);
        var shortInput = CreateBox();
        BinaryPrimitives.WriteUInt32BigEndian(
            shortInput.AsSpan(0, 4),
            (uint)shortInput.Length + 1);
        var original = CreateBox();
        var trailingInput = new byte[original.Length + 1];
        original.CopyTo(trailingInput, 0);

        Assert.Equal(
            PlaybackIsoBmffTimeToSampleBoxParseResult.WrongBoxType,
            PlaybackIsoBmffTimeToSampleBoxParser.Parse(
                wrongType,
                out var wrongTable));
        Assert.Null(wrongTable);
        Assert.Equal(
            PlaybackIsoBmffTimeToSampleBoxParseResult.NeedMoreData,
            PlaybackIsoBmffTimeToSampleBoxParser.Parse(
                shortInput,
                out var shortTable));
        Assert.Null(shortTable);
        Assert.Equal(
            PlaybackIsoBmffTimeToSampleBoxParseResult.BoxLengthMismatch,
            PlaybackIsoBmffTimeToSampleBoxParser.Parse(
                trailingInput,
                out var trailingTable));
        Assert.Null(trailingTable);
    }

    private static byte[] CreateBox(params (uint Count, uint Delta)[] entries) =>
        CreateBox(entries, extended: false, extendsToEnd: false);

    private static byte[] CreateBox(
        (uint Count, uint Delta)[] entries,
        bool extended = false,
        bool extendsToEnd = false)
    {
        var headerSize = extended ? 16 : 8;
        var box = new byte[headerSize + 8 + (entries.Length * 8)];
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            extendsToEnd ? 0 : extended ? 1 : (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Stts);
        if (extended)
        {
            BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(8, 8), (ulong)box.Length);
        }

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
                entries[index].Delta);
        }

        return box;
    }
}
