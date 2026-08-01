using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffSampleToChunkBoxParserTests
{
    private const uint Stsc = 0x73747363;
    private const uint Stbl = 0x7374626C;

    [Fact]
    public void Parse_ProjectsOrderedEntries()
    {
        var result = PlaybackIsoBmffSampleToChunkBoxParser.Parse(
            CreateBox(
                new PlaybackIsoBmffSampleToChunkEntry(1, 4, 1),
                new PlaybackIsoBmffSampleToChunkEntry(3, 2, 2),
                new PlaybackIsoBmffSampleToChunkEntry(
                    uint.MaxValue,
                    1,
                    uint.MaxValue)),
            out var sampleToChunk);

        Assert.Equal(PlaybackIsoBmffSampleToChunkBoxParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffSampleToChunkBox>(sampleToChunk);
        Assert.Equal(
            [
                new PlaybackIsoBmffSampleToChunkEntry(1, 4, 1),
                new PlaybackIsoBmffSampleToChunkEntry(3, 2, 2),
                new PlaybackIsoBmffSampleToChunkEntry(
                    uint.MaxValue,
                    1,
                    uint.MaxValue),
            ],
            parsed.Entries);
        Assert.IsNotType<PlaybackIsoBmffSampleToChunkEntry[]>(parsed.Entries);
    }

    [Fact]
    public void Parse_EmptyTableIsValid()
    {
        var result = PlaybackIsoBmffSampleToChunkBoxParser.Parse(
            CreateBox(),
            out var sampleToChunk);

        Assert.Equal(PlaybackIsoBmffSampleToChunkBoxParseResult.Parsed, result);
        Assert.Empty(
            Assert.IsType<PlaybackIsoBmffSampleToChunkBox>(
                sampleToChunk).Entries);
    }

    [Fact]
    public void Parse_ExtendedAndExtendsToEndHeadersAreSupported()
    {
        var extended = CreateBoxWithHeader(
            [new(1, 2, 3)],
            extended: true);
        var toEnd = CreateBoxWithHeader(
            [new(1, 4, 5)],
            extendsToEnd: true);

        Assert.Equal(
            PlaybackIsoBmffSampleToChunkBoxParseResult.Parsed,
            PlaybackIsoBmffSampleToChunkBoxParser.Parse(
                extended,
                out var extendedTable));
        Assert.Equal(
            (uint)2,
            Assert.IsType<PlaybackIsoBmffSampleToChunkBox>(
                extendedTable).Entries[0].SamplesPerChunk);
        Assert.Equal(
            PlaybackIsoBmffSampleToChunkBoxParseResult.Parsed,
            PlaybackIsoBmffSampleToChunkBoxParser.Parse(
                toEnd,
                out var toEndTable));
        Assert.Equal(
            (uint)5,
            Assert.IsType<PlaybackIsoBmffSampleToChunkBox>(
                toEndTable).Entries[0].SampleDescriptionIndex);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65537)]
    public void Parse_InvalidEntryLimitRejectsBeforeInputAccess(int maximum)
    {
        var result = PlaybackIsoBmffSampleToChunkBoxParser.Parse(
            [],
            out var sampleToChunk,
            new() { MaximumEntryCount = maximum });

        Assert.Equal(
            PlaybackIsoBmffSampleToChunkBoxParseResult.InvalidConfiguration,
            result);
        Assert.Null(sampleToChunk);
    }

    [Fact]
    public void Parse_EntryLimitRejectsBeforeProjection()
    {
        var result = PlaybackIsoBmffSampleToChunkBoxParser.Parse(
            CreateBox(
                new PlaybackIsoBmffSampleToChunkEntry(1, 1, 1),
                new PlaybackIsoBmffSampleToChunkEntry(2, 1, 1)),
            out var sampleToChunk,
            new() { MaximumEntryCount = 1 });

        Assert.Equal(
            PlaybackIsoBmffSampleToChunkBoxParseResult.EntryLimitExceeded,
            result);
        Assert.Null(sampleToChunk);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Parse_PayloadLengthMustBeExact(int adjustment)
    {
        var original = CreateBox(
            new PlaybackIsoBmffSampleToChunkEntry(1, 1, 1));
        var box = ResizeCompleteBox(original, adjustment);

        var result = PlaybackIsoBmffSampleToChunkBoxParser.Parse(
            box,
            out var sampleToChunk);

        Assert.Equal(
            PlaybackIsoBmffSampleToChunkBoxParseResult.InvalidPayload,
            result);
        Assert.Null(sampleToChunk);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Parse_FirstEntryMustStartAtChunkOne(uint firstChunk)
    {
        var result = PlaybackIsoBmffSampleToChunkBoxParser.Parse(
            CreateBox(
                new PlaybackIsoBmffSampleToChunkEntry(firstChunk, 1, 1)),
            out var sampleToChunk);

        Assert.Equal(
            PlaybackIsoBmffSampleToChunkBoxParseResult.InvalidFirstChunk,
            result);
        Assert.Null(sampleToChunk);
    }

    [Fact]
    public void Parse_FirstChunksMustStrictlyIncrease()
    {
        var result = PlaybackIsoBmffSampleToChunkBoxParser.Parse(
            CreateBox(
                new PlaybackIsoBmffSampleToChunkEntry(1, 1, 1),
                new PlaybackIsoBmffSampleToChunkEntry(1, 1, 1)),
            out var sampleToChunk);

        Assert.Equal(
            PlaybackIsoBmffSampleToChunkBoxParseResult.NonIncreasingFirstChunk,
            result);
        Assert.Null(sampleToChunk);
    }

    [Fact]
    public void Parse_ZeroLaterFirstChunkIsRejected()
    {
        var result = PlaybackIsoBmffSampleToChunkBoxParser.Parse(
            CreateBox(
                new PlaybackIsoBmffSampleToChunkEntry(1, 1, 1),
                new PlaybackIsoBmffSampleToChunkEntry(0, 1, 1)),
            out var sampleToChunk);

        Assert.Equal(
            PlaybackIsoBmffSampleToChunkBoxParseResult.InvalidFirstChunk,
            result);
        Assert.Null(sampleToChunk);
    }

    [Fact]
    public void Parse_ZeroSamplesPerChunkIsRejected()
    {
        var result = PlaybackIsoBmffSampleToChunkBoxParser.Parse(
            CreateBox(
                new PlaybackIsoBmffSampleToChunkEntry(1, 0, 1)),
            out var sampleToChunk);

        Assert.Equal(
            PlaybackIsoBmffSampleToChunkBoxParseResult.InvalidSamplesPerChunk,
            result);
        Assert.Null(sampleToChunk);
    }

    [Fact]
    public void Parse_ZeroSampleDescriptionIndexIsRejected()
    {
        var result = PlaybackIsoBmffSampleToChunkBoxParser.Parse(
            CreateBox(
                new PlaybackIsoBmffSampleToChunkEntry(1, 1, 0)),
            out var sampleToChunk);

        Assert.Equal(
            PlaybackIsoBmffSampleToChunkBoxParseResult
                .InvalidSampleDescriptionIndex,
            result);
        Assert.Null(sampleToChunk);
    }

    [Fact]
    public void Parse_UnsupportedVersionAndNonzeroFlagsAreDistinct()
    {
        var version = CreateBox();
        version[8] = 1;
        var flags = CreateBox();
        flags[11] = 1;

        Assert.Equal(
            PlaybackIsoBmffSampleToChunkBoxParseResult.UnsupportedVersion,
            PlaybackIsoBmffSampleToChunkBoxParser.Parse(
                version,
                out var versionTable));
        Assert.Null(versionTable);
        Assert.Equal(
            PlaybackIsoBmffSampleToChunkBoxParseResult.NonzeroFlags,
            PlaybackIsoBmffSampleToChunkBoxParser.Parse(
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
            PlaybackIsoBmffSampleToChunkBoxParseResult.WrongBoxType,
            PlaybackIsoBmffSampleToChunkBoxParser.Parse(
                wrongType,
                out var wrongTable));
        Assert.Null(wrongTable);
        Assert.Equal(
            PlaybackIsoBmffSampleToChunkBoxParseResult.NeedMoreData,
            PlaybackIsoBmffSampleToChunkBoxParser.Parse(
                shortInput,
                out var shortTable));
        Assert.Null(shortTable);
        Assert.Equal(
            PlaybackIsoBmffSampleToChunkBoxParseResult.BoxLengthMismatch,
            PlaybackIsoBmffSampleToChunkBoxParser.Parse(
                trailingInput,
                out var trailingTable));
        Assert.Null(trailingTable);
    }

    private static byte[] CreateBox(
        params PlaybackIsoBmffSampleToChunkEntry[] entries) =>
        CreateBoxWithHeader(entries);

    private static byte[] CreateBoxWithHeader(
        PlaybackIsoBmffSampleToChunkEntry[] entries,
        bool extended = false,
        bool extendsToEnd = false)
    {
        var headerSize = extended ? 16 : 8;
        var box = new byte[headerSize + 8 + (entries.Length * 12)];
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            extendsToEnd ? 0 : extended ? 1 : (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Stsc);
        if (extended)
        {
            BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(8, 8), (ulong)box.Length);
        }

        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(headerSize + 4, 4),
            (uint)entries.Length);
        for (var index = 0; index < entries.Length; index++)
        {
            var offset = headerSize + 8 + (index * 12);
            BinaryPrimitives.WriteUInt32BigEndian(
                box.AsSpan(offset, 4),
                entries[index].FirstChunk);
            BinaryPrimitives.WriteUInt32BigEndian(
                box.AsSpan(offset + 4, 4),
                entries[index].SamplesPerChunk);
            BinaryPrimitives.WriteUInt32BigEndian(
                box.AsSpan(offset + 8, 4),
                entries[index].SampleDescriptionIndex);
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
