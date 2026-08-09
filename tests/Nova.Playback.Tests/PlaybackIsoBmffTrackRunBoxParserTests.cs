using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffTrackRunBoxParserTests
{
    private const uint Trun = 0x7472756E;
    private const uint Traf = 0x74726166;
    private const uint AllEntryFields = 0x000F00;

    [Fact]
    public void Parse_ProjectsSignedDataOffsetAndAllVersionZeroEntryFields()
    {
        var entries = new[]
        {
            new PlaybackIsoBmffTrackRunEntry(1, 2, 3, uint.MaxValue),
            new PlaybackIsoBmffTrackRunEntry(4, 5, 6, 7),
        };
        var box = CreateBox(
            0,
            0x000001 | AllEntryFields,
            entries,
            dataOffset: -17);

        Assert.Equal(
            PlaybackIsoBmffTrackRunBoxParseResult.Parsed,
            PlaybackIsoBmffTrackRunBoxParser.Parse(box, out var trackRun));
        var parsed = Assert.IsType<PlaybackIsoBmffTrackRunBox>(trackRun);
        Assert.Equal((byte)0, parsed.Version);
        Assert.Equal(2U, parsed.SampleCount);
        Assert.Equal(-17, parsed.DataOffset);
        Assert.Null(parsed.FirstSampleFlags);
        Assert.Equal(entries, parsed.Entries);
        Assert.IsNotType<PlaybackIsoBmffTrackRunEntry[]>(parsed.Entries);
    }

    [Fact]
    public void Parse_VersionOneProjectsSignedCompositionOffsets()
    {
        var entries = new[]
        {
            new PlaybackIsoBmffTrackRunEntry(null, null, null, int.MinValue),
            new PlaybackIsoBmffTrackRunEntry(null, null, null, int.MaxValue),
        };

        Assert.Equal(
            PlaybackIsoBmffTrackRunBoxParseResult.Parsed,
            PlaybackIsoBmffTrackRunBoxParser.Parse(
                CreateBox(1, 0x000800, entries),
                out var trackRun));
        Assert.Equal(
            entries,
            Assert.IsType<PlaybackIsoBmffTrackRunBox>(trackRun).Entries);
    }

    [Fact]
    public void Parse_FirstSampleFlagsRemainSeparateFromAbsentEntryFlags()
    {
        Assert.Equal(
            PlaybackIsoBmffTrackRunBoxParseResult.Parsed,
            PlaybackIsoBmffTrackRunBoxParser.Parse(
                CreateBox(
                    0,
                    0x000004,
                    [new(null, null, null, null)],
                    firstSampleFlags: uint.MaxValue),
                out var trackRun));
        var parsed = Assert.IsType<PlaybackIsoBmffTrackRunBox>(trackRun);
        Assert.Equal(uint.MaxValue, parsed.FirstSampleFlags);
        Assert.Equal(
            new PlaybackIsoBmffTrackRunEntry(null, null, null, null),
            parsed.Entries[0]);
    }

    [Fact]
    public void Parse_EmptyRunAndExtendedHeadersAreSupported()
    {
        Assert.Equal(
            PlaybackIsoBmffTrackRunBoxParseResult.Parsed,
            PlaybackIsoBmffTrackRunBoxParser.Parse(
                CreateBox(0, 0, [], extended: true),
                out var extended));
        Assert.Empty(Assert.IsType<PlaybackIsoBmffTrackRunBox>(extended).Entries);
        Assert.Equal(
            PlaybackIsoBmffTrackRunBoxParseResult.Parsed,
            PlaybackIsoBmffTrackRunBoxParser.Parse(
                CreateBox(1, 0, [], extendsToEnd: true),
                out var toEnd));
        Assert.Empty(Assert.IsType<PlaybackIsoBmffTrackRunBox>(toEnd).Entries);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65537)]
    public void Parse_InvalidEntryLimitRejectsBeforeInputAccess(int maximum)
    {
        Assert.Equal(
            PlaybackIsoBmffTrackRunBoxParseResult.InvalidConfiguration,
            PlaybackIsoBmffTrackRunBoxParser.Parse(
                [],
                out var trackRun,
                new() { MaximumEntryCount = maximum }));
        Assert.Null(trackRun);
    }

    [Fact]
    public void Parse_EntryLimitRejectsBeforeProjection()
    {
        Assert.Equal(
            PlaybackIsoBmffTrackRunBoxParseResult.EntryLimitExceeded,
            PlaybackIsoBmffTrackRunBoxParser.Parse(
                CreateBox(
                    0,
                    0,
                    [new(null, null, null, null), new(null, null, null, null)]),
                out var trackRun,
                new() { MaximumEntryCount = 1 }));
        Assert.Null(trackRun);
    }

    [Fact]
    public void Parse_FirstAndPerSampleFlagsCannotBothBeDeclared()
    {
        Assert.Equal(
            PlaybackIsoBmffTrackRunBoxParseResult.InvalidFlagCombination,
            PlaybackIsoBmffTrackRunBoxParser.Parse(
                CreateBox(
                    0,
                    0x000404,
                    [new(null, null, 1, null)],
                    firstSampleFlags: 2),
                out var trackRun));
        Assert.Null(trackRun);
    }

    [Fact]
    public void Parse_UnsupportedVersionAndUnknownFlagsAreDistinct()
    {
        var version = CreateBox(0, 0, []);
        version[8] = 2;
        var flags = CreateBox(0, 0x000002, []);

        Assert.Equal(
            PlaybackIsoBmffTrackRunBoxParseResult.UnsupportedVersion,
            PlaybackIsoBmffTrackRunBoxParser.Parse(version, out var first));
        Assert.Null(first);
        Assert.Equal(
            PlaybackIsoBmffTrackRunBoxParseResult.UnknownFlags,
            PlaybackIsoBmffTrackRunBoxParser.Parse(flags, out var second));
        Assert.Null(second);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Parse_PayloadLengthMustMatchFlagsAndCount(int adjustment)
    {
        var original = CreateBox(
            0,
            0x000300,
            [new(1, 2, null, null)]);
        var box = new byte[original.Length + adjustment];
        original.AsSpan(0, Math.Min(original.Length, box.Length)).CopyTo(box);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), (uint)box.Length);

        Assert.Equal(
            PlaybackIsoBmffTrackRunBoxParseResult.InvalidPayload,
            PlaybackIsoBmffTrackRunBoxParser.Parse(box, out var trackRun));
        Assert.Null(trackRun);
    }

    [Fact]
    public void Parse_WrongTypeAndCompleteLengthFailuresAreDistinct()
    {
        var wrongType = CreateBox(0, 0, []);
        BinaryPrimitives.WriteUInt32BigEndian(wrongType.AsSpan(4, 4), Traf);
        var shortInput = CreateBox(0, 0, []);
        BinaryPrimitives.WriteUInt32BigEndian(
            shortInput.AsSpan(0, 4),
            (uint)shortInput.Length + 1);
        var original = CreateBox(0, 0, []);
        var trailing = new byte[original.Length + 1];
        original.CopyTo(trailing, 0);

        Assert.Equal(
            PlaybackIsoBmffTrackRunBoxParseResult.WrongBoxType,
            PlaybackIsoBmffTrackRunBoxParser.Parse(wrongType, out var wrong));
        Assert.Null(wrong);
        Assert.Equal(
            PlaybackIsoBmffTrackRunBoxParseResult.NeedMoreData,
            PlaybackIsoBmffTrackRunBoxParser.Parse(
                shortInput,
                out var shortResult));
        Assert.Null(shortResult);
        Assert.Equal(
            PlaybackIsoBmffTrackRunBoxParseResult.BoxLengthMismatch,
            PlaybackIsoBmffTrackRunBoxParser.Parse(
                trailing,
                out var trailingResult));
        Assert.Null(trailingResult);
    }

    private static byte[] CreateBox(
        byte version,
        uint flags,
        PlaybackIsoBmffTrackRunEntry[] entries,
        int? dataOffset = null,
        uint? firstSampleFlags = null,
        bool extended = false,
        bool extendsToEnd = false)
    {
        var headerSize = extended ? 16 : 8;
        var optionalHeaderSize =
            ((flags & 0x000001) != 0 ? 4 : 0) +
            ((flags & 0x000004) != 0 ? 4 : 0);
        var entrySize =
            ((flags & 0x000100) != 0 ? 4 : 0) +
            ((flags & 0x000200) != 0 ? 4 : 0) +
            ((flags & 0x000400) != 0 ? 4 : 0) +
            ((flags & 0x000800) != 0 ? 4 : 0);
        var box = new byte[
            headerSize + 8 + optionalHeaderSize + (entrySize * entries.Length)];
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            extendsToEnd ? 0 : extended ? 1 : (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Trun);
        if (extended)
        {
            BinaryPrimitives.WriteUInt64BigEndian(
                box.AsSpan(8, 8),
                (ulong)box.Length);
        }

        box[headerSize] = version;
        box[headerSize + 1] = (byte)(flags >> 16);
        box[headerSize + 2] = (byte)(flags >> 8);
        box[headerSize + 3] = (byte)flags;
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(headerSize + 4, 4),
            (uint)entries.Length);
        var cursor = headerSize + 8;
        if ((flags & 0x000001) != 0)
        {
            BinaryPrimitives.WriteInt32BigEndian(
                box.AsSpan(cursor, 4),
                dataOffset ?? 0);
            cursor += 4;
        }

        if ((flags & 0x000004) != 0)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                box.AsSpan(cursor, 4),
                firstSampleFlags ?? 0);
            cursor += 4;
        }

        foreach (var entry in entries)
        {
            WriteOptionalUInt32(box, flags, 0x000100, ref cursor, entry.SampleDuration);
            WriteOptionalUInt32(box, flags, 0x000200, ref cursor, entry.SampleSize);
            WriteOptionalUInt32(box, flags, 0x000400, ref cursor, entry.SampleFlags);
            if ((flags & 0x000800) == 0)
            {
                continue;
            }

            if (version == 0)
            {
                BinaryPrimitives.WriteUInt32BigEndian(
                    box.AsSpan(cursor, 4),
                    (uint)(entry.SampleCompositionTimeOffset ?? 0));
            }
            else
            {
                BinaryPrimitives.WriteInt32BigEndian(
                    box.AsSpan(cursor, 4),
                    (int)(entry.SampleCompositionTimeOffset ?? 0));
            }

            cursor += 4;
        }

        return box;
    }

    private static void WriteOptionalUInt32(
        byte[] box,
        uint flags,
        uint flag,
        ref int cursor,
        uint? value)
    {
        if ((flags & flag) == 0)
        {
            return;
        }

        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(cursor, 4),
            value ?? 0);
        cursor += 4;
    }
}
