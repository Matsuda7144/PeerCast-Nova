using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffChunkOffsetBoxParserTests
{
    private const uint Stco = 0x7374636F;
    private const uint Stbl = 0x7374626C;

    [Fact]
    public void Parse_ProjectsOffsetsWithoutOrderingPolicy()
    {
        var result = PlaybackIsoBmffChunkOffsetBoxParser.Parse(
            CreateBox(20, 0, 10, uint.MaxValue),
            out var chunkOffset);

        Assert.Equal(PlaybackIsoBmffChunkOffsetBoxParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffChunkOffsetBox>(chunkOffset);
        Assert.Equal([20U, 0U, 10U, uint.MaxValue], parsed.ChunkOffsets);
        Assert.IsNotType<uint[]>(parsed.ChunkOffsets);
    }

    [Fact]
    public void Parse_EmptyTableIsValid()
    {
        var result = PlaybackIsoBmffChunkOffsetBoxParser.Parse(
            CreateBox(),
            out var chunkOffset);

        Assert.Equal(PlaybackIsoBmffChunkOffsetBoxParseResult.Parsed, result);
        Assert.Empty(
            Assert.IsType<PlaybackIsoBmffChunkOffsetBox>(
                chunkOffset).ChunkOffsets);
    }

    [Fact]
    public void Parse_ExtendedAndExtendsToEndHeadersAreSupported()
    {
        var extended = CreateBoxWithHeader([1, 2], extended: true);
        var toEnd = CreateBoxWithHeader([3, 4], extendsToEnd: true);

        Assert.Equal(
            PlaybackIsoBmffChunkOffsetBoxParseResult.Parsed,
            PlaybackIsoBmffChunkOffsetBoxParser.Parse(
                extended,
                out var extendedTable));
        Assert.Equal(
            [1U, 2U],
            Assert.IsType<PlaybackIsoBmffChunkOffsetBox>(
                extendedTable).ChunkOffsets);
        Assert.Equal(
            PlaybackIsoBmffChunkOffsetBoxParseResult.Parsed,
            PlaybackIsoBmffChunkOffsetBoxParser.Parse(
                toEnd,
                out var toEndTable));
        Assert.Equal(
            [3U, 4U],
            Assert.IsType<PlaybackIsoBmffChunkOffsetBox>(
                toEndTable).ChunkOffsets);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65537)]
    public void Parse_InvalidEntryLimitRejectsBeforeInputAccess(int maximum)
    {
        var result = PlaybackIsoBmffChunkOffsetBoxParser.Parse(
            [],
            out var chunkOffset,
            new() { MaximumEntryCount = maximum });

        Assert.Equal(
            PlaybackIsoBmffChunkOffsetBoxParseResult.InvalidConfiguration,
            result);
        Assert.Null(chunkOffset);
    }

    [Fact]
    public void Parse_EntryLimitRejectsBeforeProjection()
    {
        var result = PlaybackIsoBmffChunkOffsetBoxParser.Parse(
            CreateBox(1, 2),
            out var chunkOffset,
            new() { MaximumEntryCount = 1 });

        Assert.Equal(
            PlaybackIsoBmffChunkOffsetBoxParseResult.EntryLimitExceeded,
            result);
        Assert.Null(chunkOffset);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Parse_PayloadLengthMustBeExact(int adjustment)
    {
        var original = CreateBox(1);
        var box = ResizeCompleteBox(original, adjustment);

        var result = PlaybackIsoBmffChunkOffsetBoxParser.Parse(
            box,
            out var chunkOffset);

        Assert.Equal(
            PlaybackIsoBmffChunkOffsetBoxParseResult.InvalidPayload,
            result);
        Assert.Null(chunkOffset);
    }

    [Fact]
    public void Parse_UnsupportedVersionAndNonzeroFlagsAreDistinct()
    {
        var version = CreateBox();
        version[8] = 1;
        var flags = CreateBox();
        flags[11] = 1;

        Assert.Equal(
            PlaybackIsoBmffChunkOffsetBoxParseResult.UnsupportedVersion,
            PlaybackIsoBmffChunkOffsetBoxParser.Parse(
                version,
                out var versionTable));
        Assert.Null(versionTable);
        Assert.Equal(
            PlaybackIsoBmffChunkOffsetBoxParseResult.NonzeroFlags,
            PlaybackIsoBmffChunkOffsetBoxParser.Parse(
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
            PlaybackIsoBmffChunkOffsetBoxParseResult.WrongBoxType,
            PlaybackIsoBmffChunkOffsetBoxParser.Parse(
                wrongType,
                out var wrongTable));
        Assert.Null(wrongTable);
        Assert.Equal(
            PlaybackIsoBmffChunkOffsetBoxParseResult.NeedMoreData,
            PlaybackIsoBmffChunkOffsetBoxParser.Parse(
                shortInput,
                out var shortTable));
        Assert.Null(shortTable);
        Assert.Equal(
            PlaybackIsoBmffChunkOffsetBoxParseResult.BoxLengthMismatch,
            PlaybackIsoBmffChunkOffsetBoxParser.Parse(
                trailingInput,
                out var trailingTable));
        Assert.Null(trailingTable);
    }

    private static byte[] CreateBox(params uint[] chunkOffsets) =>
        CreateBoxWithHeader(chunkOffsets);

    private static byte[] CreateBoxWithHeader(
        uint[] chunkOffsets,
        bool extended = false,
        bool extendsToEnd = false)
    {
        var headerSize = extended ? 16 : 8;
        var box = new byte[headerSize + 8 + (chunkOffsets.Length * 4)];
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            extendsToEnd ? 0 : extended ? 1 : (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Stco);
        if (extended)
        {
            BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(8, 8), (ulong)box.Length);
        }

        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(headerSize + 4, 4),
            (uint)chunkOffsets.Length);
        for (var index = 0; index < chunkOffsets.Length; index++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                box.AsSpan(headerSize + 8 + (index * 4), 4),
                chunkOffsets[index]);
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
