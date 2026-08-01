using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffEditListBoxParserTests
{
    private const uint Elst = 0x656C7374;
    private const uint Edts = 0x65647473;

    [Fact]
    public void Parse_VersionZeroProjectsEntriesRatesAndAggregate()
    {
        var result = PlaybackIsoBmffEditListBoxParser.Parse(
            CreateBox(
                0,
                (10, -1, 1, 0),
                (20, 5, 0, short.MinValue)),
            out var editList);

        Assert.Equal(PlaybackIsoBmffEditListBoxParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffEditListBox>(editList);
        Assert.Equal((byte)0, parsed.Version);
        Assert.Equal(
            [
                new PlaybackIsoBmffEditListEntry(10, -1, 1, 0),
                new PlaybackIsoBmffEditListEntry(20, 5, 0, short.MinValue),
            ],
            parsed.Entries);
        Assert.True(parsed.Entries[0].IsEmptyEdit);
        Assert.False(parsed.Entries[1].IsEmptyEdit);
        Assert.Equal((ulong)30, parsed.TotalSegmentDurationUnits);
    }

    [Fact]
    public void Parse_VersionOneProjectsWideUnsignedAndSignedValues()
    {
        var result = PlaybackIsoBmffEditListBoxParser.Parse(
            CreateBox(1, (ulong.MaxValue, long.MinValue, short.MaxValue, -1)),
            out var editList);

        Assert.Equal(PlaybackIsoBmffEditListBoxParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffEditListBox>(editList);
        Assert.Equal((byte)1, parsed.Version);
        Assert.Equal(
            new PlaybackIsoBmffEditListEntry(
                ulong.MaxValue,
                long.MinValue,
                short.MaxValue,
                -1),
            Assert.Single(parsed.Entries));
        Assert.Equal(ulong.MaxValue, parsed.TotalSegmentDurationUnits);
    }

    [Fact]
    public void Parse_EmptyTableHasZeroAggregate()
    {
        var result = PlaybackIsoBmffEditListBoxParser.Parse(
            CreateBox(0),
            out var editList);

        Assert.Equal(PlaybackIsoBmffEditListBoxParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffEditListBox>(editList);
        Assert.Empty(parsed.Entries);
        Assert.Equal((ulong)0, parsed.TotalSegmentDurationUnits);
    }

    [Fact]
    public void Parse_ExtendedAndExtendsToEndHeadersAreSupported()
    {
        var extended = CreateBox(
            0,
            [(1, 0, 1, 0)],
            extended: true);
        var toEnd = CreateBox(
            1,
            [(2, -1, 1, 0)],
            extendsToEnd: true);

        Assert.Equal(
            PlaybackIsoBmffEditListBoxParseResult.Parsed,
            PlaybackIsoBmffEditListBoxParser.Parse(
                extended,
                out var extendedList));
        Assert.Single(
            Assert.IsType<PlaybackIsoBmffEditListBox>(extendedList).Entries);
        Assert.Equal(
            PlaybackIsoBmffEditListBoxParseResult.Parsed,
            PlaybackIsoBmffEditListBoxParser.Parse(toEnd, out var toEndList));
        Assert.True(
            Assert.IsType<PlaybackIsoBmffEditListBox>(toEndList)
                .Entries[0].IsEmptyEdit);
    }

    [Fact]
    public void Parse_SegmentDurationAggregateOverflowIsRejected()
    {
        var result = PlaybackIsoBmffEditListBoxParser.Parse(
            CreateBox(
                1,
                (ulong.MaxValue, 0, 1, 0),
                (1, 0, 1, 0)),
            out var editList);

        Assert.Equal(
            PlaybackIsoBmffEditListBoxParseResult.AggregateOverflow,
            result);
        Assert.Null(editList);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65_537)]
    public void Parse_InvalidEntryLimitRejectsBeforeInputAccess(int maximum)
    {
        var result = PlaybackIsoBmffEditListBoxParser.Parse(
            [],
            out var editList,
            new() { MaximumEntryCount = maximum });

        Assert.Equal(
            PlaybackIsoBmffEditListBoxParseResult.InvalidConfiguration,
            result);
        Assert.Null(editList);
    }

    [Fact]
    public void Parse_EntryLimitIsEnforcedBeforeProjection()
    {
        var result = PlaybackIsoBmffEditListBoxParser.Parse(
            CreateBox(0, (1, 0, 1, 0), (1, 0, 1, 0)),
            out var editList,
            new() { MaximumEntryCount = 1 });

        Assert.Equal(
            PlaybackIsoBmffEditListBoxParseResult.EntryLimitExceeded,
            result);
        Assert.Null(editList);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Parse_EntryPayloadLengthMustBeExact(int adjustment)
    {
        var original = CreateBox(1, (1, 0, 1, 0));
        var box = new byte[original.Length + adjustment];
        original.AsSpan(0, Math.Min(original.Length, box.Length)).CopyTo(box);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), (uint)box.Length);

        var result = PlaybackIsoBmffEditListBoxParser.Parse(
            box,
            out var editList);

        Assert.Equal(PlaybackIsoBmffEditListBoxParseResult.InvalidPayload, result);
        Assert.Null(editList);
    }

    [Fact]
    public void Parse_UnsupportedVersionAndNonzeroFlagsAreDistinct()
    {
        var version = CreateBox(0);
        version[8] = 2;
        var flags = CreateBox(0);
        flags[11] = 1;

        Assert.Equal(
            PlaybackIsoBmffEditListBoxParseResult.UnsupportedVersion,
            PlaybackIsoBmffEditListBoxParser.Parse(version, out var versionList));
        Assert.Null(versionList);
        Assert.Equal(
            PlaybackIsoBmffEditListBoxParseResult.NonzeroFlags,
            PlaybackIsoBmffEditListBoxParser.Parse(flags, out var flagsList));
        Assert.Null(flagsList);
    }

    [Fact]
    public void Parse_WrongTypeAndCompleteLengthFailuresAreDistinct()
    {
        var wrongType = CreateBox(0);
        BinaryPrimitives.WriteUInt32BigEndian(wrongType.AsSpan(4, 4), Edts);
        var shortInput = CreateBox(0);
        BinaryPrimitives.WriteUInt32BigEndian(
            shortInput.AsSpan(0, 4),
            (uint)shortInput.Length + 1);
        var original = CreateBox(0);
        var trailingInput = new byte[original.Length + 1];
        original.CopyTo(trailingInput, 0);

        Assert.Equal(
            PlaybackIsoBmffEditListBoxParseResult.WrongBoxType,
            PlaybackIsoBmffEditListBoxParser.Parse(wrongType, out var wrongList));
        Assert.Null(wrongList);
        Assert.Equal(
            PlaybackIsoBmffEditListBoxParseResult.NeedMoreData,
            PlaybackIsoBmffEditListBoxParser.Parse(shortInput, out var shortList));
        Assert.Null(shortList);
        Assert.Equal(
            PlaybackIsoBmffEditListBoxParseResult.BoxLengthMismatch,
            PlaybackIsoBmffEditListBoxParser.Parse(
                trailingInput,
                out var trailingList));
        Assert.Null(trailingList);
    }

    private static byte[] CreateBox(
        byte version,
        params (ulong Duration, long MediaTime, short RateInteger,
            short RateFraction)[] entries) =>
        CreateBox(version, entries, extended: false, extendsToEnd: false);

    private static byte[] CreateBox(
        byte version,
        (ulong Duration, long MediaTime, short RateInteger,
            short RateFraction)[] entries,
        bool extended = false,
        bool extendsToEnd = false)
    {
        var headerSize = extended ? 16 : 8;
        var entrySize = version == 0 ? 12 : 20;
        var box = new byte[headerSize + 8 + (entries.Length * entrySize)];
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            extendsToEnd ? 0 : extended ? 1 : (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Elst);
        if (extended)
        {
            BinaryPrimitives.WriteUInt64BigEndian(
                box.AsSpan(8, 8),
                (ulong)box.Length);
        }

        box[headerSize] = version;
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(headerSize + 4, 4),
            (uint)entries.Length);
        for (var index = 0; index < entries.Length; index++)
        {
            var offset = headerSize + 8 + (index * entrySize);
            var entry = entries[index];
            int rateOffset;
            if (version == 0)
            {
                BinaryPrimitives.WriteUInt32BigEndian(
                    box.AsSpan(offset, 4),
                    checked((uint)entry.Duration));
                BinaryPrimitives.WriteInt32BigEndian(
                    box.AsSpan(offset + 4, 4),
                    checked((int)entry.MediaTime));
                rateOffset = offset + 8;
            }
            else
            {
                BinaryPrimitives.WriteUInt64BigEndian(
                    box.AsSpan(offset, 8),
                    entry.Duration);
                BinaryPrimitives.WriteInt64BigEndian(
                    box.AsSpan(offset + 8, 8),
                    entry.MediaTime);
                rateOffset = offset + 16;
            }

            BinaryPrimitives.WriteInt16BigEndian(
                box.AsSpan(rateOffset, 2),
                entry.RateInteger);
            BinaryPrimitives.WriteInt16BigEndian(
                box.AsSpan(rateOffset + 2, 2),
                entry.RateFraction);
        }

        return box;
    }
}
