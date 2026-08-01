using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffSampleDescriptionBoxParserTests
{
    private const uint Stsd = 0x73747364;
    private const uint Stbl = 0x7374626c;
    private const uint Avc1 = 0x61766331;
    private const uint Mp4a = 0x6d703461;

    [Fact]
    public void Parse_ProjectsBoundedEntryLocationsWithoutReadingEntryPayloads()
    {
        var box = CreateBox(
            Entry(Avc1, [0xaa, 0xbb]),
            Entry(Mp4a, [0xcc, 0xdd, 0xee]));

        var result = PlaybackIsoBmffSampleDescriptionBoxParser.Parse(
            box,
            out var sampleDescription);

        Assert.Equal(PlaybackIsoBmffSampleDescriptionBoxParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffSampleDescriptionBox>(
            sampleDescription);
        Assert.Equal(2, parsed.Entries.Count);
        Assert.IsNotType<PlaybackIsoBmffBoxLocation[]>(parsed.Entries);
        Assert.Equal(Avc1, parsed.Entries[0].Header.Type);
        Assert.Equal(16, parsed.Entries[0].Offset);
        Assert.Equal(10, parsed.Entries[0].BoxLength);
        Assert.Equal(24, parsed.Entries[0].PayloadOffset);
        Assert.Equal(2, parsed.Entries[0].PayloadLength);
        Assert.Equal(Mp4a, parsed.Entries[1].Header.Type);
        Assert.Equal(26, parsed.Entries[1].Offset);
        Assert.Equal(11, parsed.Entries[1].BoxLength);
        Assert.Equal(box.Length, parsed.Entries[1].NextOffset);
    }

    [Fact]
    public void Parse_ZeroEntriesRequireNoTrailingPayload()
    {
        var box = CreateBox();

        var result = PlaybackIsoBmffSampleDescriptionBoxParser.Parse(
            box,
            out var sampleDescription);

        Assert.Equal(PlaybackIsoBmffSampleDescriptionBoxParseResult.Parsed, result);
        Assert.Empty(Assert.IsType<PlaybackIsoBmffSampleDescriptionBox>(
            sampleDescription).Entries);
    }

    [Fact]
    public void Parse_ExtendedAndExtendsToEndHeadersAreSupported()
    {
        var extended = CreateBox([Entry(Avc1, [])], extended: true);
        var toEnd = CreateBox([Entry(Mp4a, [])], extendsToEnd: true);

        Assert.Equal(
            PlaybackIsoBmffSampleDescriptionBoxParseResult.Parsed,
            PlaybackIsoBmffSampleDescriptionBoxParser.Parse(
                extended,
                out var extendedDescription));
        Assert.Single(Assert.IsType<PlaybackIsoBmffSampleDescriptionBox>(
            extendedDescription).Entries);
        Assert.Equal(
            PlaybackIsoBmffSampleDescriptionBoxParseResult.Parsed,
            PlaybackIsoBmffSampleDescriptionBoxParser.Parse(
                toEnd,
                out var toEndDescription));
        Assert.Single(Assert.IsType<PlaybackIsoBmffSampleDescriptionBox>(
            toEndDescription).Entries);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(257)]
    public void Parse_InvalidEntryLimitRejectsBeforeInputAccess(int maximum)
    {
        var result = PlaybackIsoBmffSampleDescriptionBoxParser.Parse(
            [],
            out var sampleDescription,
            new() { MaximumEntryCount = maximum });

        Assert.Equal(
            PlaybackIsoBmffSampleDescriptionBoxParseResult.InvalidConfiguration,
            result);
        Assert.Null(sampleDescription);
    }

    [Fact]
    public void Parse_EntryLimitRejectsBeforeEntryProjection()
    {
        var box = CreateBox(Entry(Avc1, []), Entry(Mp4a, []));

        var result = PlaybackIsoBmffSampleDescriptionBoxParser.Parse(
            box,
            out var sampleDescription,
            new() { MaximumEntryCount = 1 });

        Assert.Equal(
            PlaybackIsoBmffSampleDescriptionBoxParseResult.EntryLimitExceeded,
            result);
        Assert.Null(sampleDescription);
    }

    [Fact]
    public void Parse_FewerEntriesThanDeclaredIsRejected()
    {
        var box = CreateBox(Entry(Avc1, []));
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4), 2);

        var result = PlaybackIsoBmffSampleDescriptionBoxParser.Parse(
            box,
            out var sampleDescription);

        Assert.Equal(
            PlaybackIsoBmffSampleDescriptionBoxParseResult.EntrySequenceMismatch,
            result);
        Assert.Null(sampleDescription);
    }

    [Fact]
    public void Parse_ExtraEntryBeyondDeclaredCountIsRejected()
    {
        var box = CreateBox(Entry(Avc1, []), Entry(Mp4a, []));
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4), 1);

        var result = PlaybackIsoBmffSampleDescriptionBoxParser.Parse(
            box,
            out var sampleDescription);

        Assert.Equal(
            PlaybackIsoBmffSampleDescriptionBoxParseResult.EntrySequenceMismatch,
            result);
        Assert.Null(sampleDescription);
    }

    [Fact]
    public void Parse_TruncatedOrInvalidEntryIsRejected()
    {
        var truncated = CreateBox(Entry(Avc1, []));
        BinaryPrimitives.WriteUInt32BigEndian(truncated.AsSpan(16, 4), 9);
        var invalid = CreateBox(Entry(Avc1, []));
        BinaryPrimitives.WriteUInt32BigEndian(invalid.AsSpan(16, 4), 7);

        Assert.Equal(
            PlaybackIsoBmffSampleDescriptionBoxParseResult.EntrySequenceMismatch,
            PlaybackIsoBmffSampleDescriptionBoxParser.Parse(
                truncated,
                out var truncatedDescription));
        Assert.Null(truncatedDescription);
        Assert.Equal(
            PlaybackIsoBmffSampleDescriptionBoxParseResult.EntrySequenceMismatch,
            PlaybackIsoBmffSampleDescriptionBoxParser.Parse(
                invalid,
                out var invalidDescription));
        Assert.Null(invalidDescription);
    }

    [Fact]
    public void Parse_UnsupportedVersionAndNonzeroFlagsAreDistinct()
    {
        var version = CreateBox();
        version[8] = 1;
        var flags = CreateBox();
        flags[11] = 1;

        Assert.Equal(
            PlaybackIsoBmffSampleDescriptionBoxParseResult.UnsupportedVersion,
            PlaybackIsoBmffSampleDescriptionBoxParser.Parse(
                version,
                out var versionDescription));
        Assert.Null(versionDescription);
        Assert.Equal(
            PlaybackIsoBmffSampleDescriptionBoxParseResult.NonzeroFlags,
            PlaybackIsoBmffSampleDescriptionBoxParser.Parse(
                flags,
                out var flagsDescription));
        Assert.Null(flagsDescription);
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
            PlaybackIsoBmffSampleDescriptionBoxParseResult.WrongBoxType,
            PlaybackIsoBmffSampleDescriptionBoxParser.Parse(
                wrongType,
                out var wrongDescription));
        Assert.Null(wrongDescription);
        Assert.Equal(
            PlaybackIsoBmffSampleDescriptionBoxParseResult.NeedMoreData,
            PlaybackIsoBmffSampleDescriptionBoxParser.Parse(
                shortInput,
                out var shortDescription));
        Assert.Null(shortDescription);
        Assert.Equal(
            PlaybackIsoBmffSampleDescriptionBoxParseResult.BoxLengthMismatch,
            PlaybackIsoBmffSampleDescriptionBoxParser.Parse(
                trailingInput,
                out var trailingDescription));
        Assert.Null(trailingDescription);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(15)]
    public void Parse_TruncatedHeaderOrFixedPayloadIsRejected(int length)
    {
        var box = new byte[length];
        if (length >= 8)
        {
            BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), (uint)length);
            BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Stsd);
        }

        var result = PlaybackIsoBmffSampleDescriptionBoxParser.Parse(
            box,
            out var sampleDescription);

        var expected = length < 8
            ? PlaybackIsoBmffSampleDescriptionBoxParseResult.NeedMoreData
            : PlaybackIsoBmffSampleDescriptionBoxParseResult.InvalidPayload;
        Assert.Equal(expected, result);
        Assert.Null(sampleDescription);
    }

    private static byte[] Entry(uint type, byte[] payload)
    {
        var entry = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(0, 4), (uint)entry.Length);
        BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(4, 4), type);
        payload.CopyTo(entry, 8);
        return entry;
    }

    private static byte[] CreateBox(params byte[][] entries) =>
        CreateBox(entries, extended: false, extendsToEnd: false);

    private static byte[] CreateBox(
        byte[][] entries,
        bool extended = false,
        bool extendsToEnd = false)
    {
        var headerSize = extended ? 16 : 8;
        var box = new byte[headerSize + 8 + entries.Sum(static entry => entry.Length)];
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            extendsToEnd ? 0 : extended ? 1 : (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Stsd);
        if (extended)
        {
            BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(8, 8), (ulong)box.Length);
        }

        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(headerSize + 4, 4),
            (uint)entries.Length);
        var offset = headerSize + 8;
        foreach (var entry in entries)
        {
            entry.CopyTo(box, offset);
            offset += entry.Length;
        }

        return box;
    }
}
