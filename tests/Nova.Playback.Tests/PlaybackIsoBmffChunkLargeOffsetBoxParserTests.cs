using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffChunkLargeOffsetBoxParserTests
{
    private const uint Co64 = 0x636F3634;
    private const uint Stbl = 0x7374626C;

    [Fact]
    public void Parse_ProjectsOffsetsWithoutOrderingPolicy()
    {
        var result = PlaybackIsoBmffChunkLargeOffsetBoxParser.Parse(
            CreateBox(
                (ulong)uint.MaxValue + 1,
                0,
                10,
                ulong.MaxValue),
            out var chunkLargeOffset);

        Assert.Equal(
            PlaybackIsoBmffChunkLargeOffsetBoxParseResult.Parsed,
            result);
        var parsed = Assert.IsType<PlaybackIsoBmffChunkLargeOffsetBox>(
            chunkLargeOffset);
        Assert.Equal(
            [(ulong)uint.MaxValue + 1, 0UL, 10UL, ulong.MaxValue],
            parsed.ChunkOffsets);
        Assert.IsNotType<ulong[]>(parsed.ChunkOffsets);
    }

    [Fact]
    public void Parse_EmptyTableIsValid()
    {
        var result = PlaybackIsoBmffChunkLargeOffsetBoxParser.Parse(
            CreateBox(),
            out var chunkLargeOffset);

        Assert.Equal(
            PlaybackIsoBmffChunkLargeOffsetBoxParseResult.Parsed,
            result);
        Assert.Empty(
            Assert.IsType<PlaybackIsoBmffChunkLargeOffsetBox>(
                chunkLargeOffset).ChunkOffsets);
    }

    [Fact]
    public void Parse_ExtendedAndExtendsToEndHeadersAreSupported()
    {
        var extended = CreateBoxWithHeader([1, 2], extended: true);
        var toEnd = CreateBoxWithHeader([3, 4], extendsToEnd: true);

        Assert.Equal(
            PlaybackIsoBmffChunkLargeOffsetBoxParseResult.Parsed,
            PlaybackIsoBmffChunkLargeOffsetBoxParser.Parse(
                extended,
                out var extendedTable));
        Assert.Equal(
            [1UL, 2UL],
            Assert.IsType<PlaybackIsoBmffChunkLargeOffsetBox>(
                extendedTable).ChunkOffsets);
        Assert.Equal(
            PlaybackIsoBmffChunkLargeOffsetBoxParseResult.Parsed,
            PlaybackIsoBmffChunkLargeOffsetBoxParser.Parse(
                toEnd,
                out var toEndTable));
        Assert.Equal(
            [3UL, 4UL],
            Assert.IsType<PlaybackIsoBmffChunkLargeOffsetBox>(
                toEndTable).ChunkOffsets);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65537)]
    public void Parse_InvalidEntryLimitRejectsBeforeInputAccess(int maximum)
    {
        var result = PlaybackIsoBmffChunkLargeOffsetBoxParser.Parse(
            [],
            out var chunkLargeOffset,
            new() { MaximumEntryCount = maximum });

        Assert.Equal(
            PlaybackIsoBmffChunkLargeOffsetBoxParseResult.InvalidConfiguration,
            result);
        Assert.Null(chunkLargeOffset);
    }

    [Fact]
    public void Parse_EntryLimitRejectsBeforeProjection()
    {
        var result = PlaybackIsoBmffChunkLargeOffsetBoxParser.Parse(
            CreateBox(1, 2),
            out var chunkLargeOffset,
            new() { MaximumEntryCount = 1 });

        Assert.Equal(
            PlaybackIsoBmffChunkLargeOffsetBoxParseResult.EntryLimitExceeded,
            result);
        Assert.Null(chunkLargeOffset);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Parse_PayloadLengthMustBeExact(int adjustment)
    {
        var original = CreateBox(1);
        var box = ResizeCompleteBox(original, adjustment);

        var result = PlaybackIsoBmffChunkLargeOffsetBoxParser.Parse(
            box,
            out var chunkLargeOffset);

        Assert.Equal(
            PlaybackIsoBmffChunkLargeOffsetBoxParseResult.InvalidPayload,
            result);
        Assert.Null(chunkLargeOffset);
    }

    [Fact]
    public void Parse_UnsupportedVersionAndNonzeroFlagsAreDistinct()
    {
        var version = CreateBox();
        version[8] = 1;
        var flags = CreateBox();
        flags[11] = 1;

        Assert.Equal(
            PlaybackIsoBmffChunkLargeOffsetBoxParseResult.UnsupportedVersion,
            PlaybackIsoBmffChunkLargeOffsetBoxParser.Parse(
                version,
                out var versionTable));
        Assert.Null(versionTable);
        Assert.Equal(
            PlaybackIsoBmffChunkLargeOffsetBoxParseResult.NonzeroFlags,
            PlaybackIsoBmffChunkLargeOffsetBoxParser.Parse(
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
            PlaybackIsoBmffChunkLargeOffsetBoxParseResult.WrongBoxType,
            PlaybackIsoBmffChunkLargeOffsetBoxParser.Parse(
                wrongType,
                out var wrongTable));
        Assert.Null(wrongTable);
        Assert.Equal(
            PlaybackIsoBmffChunkLargeOffsetBoxParseResult.NeedMoreData,
            PlaybackIsoBmffChunkLargeOffsetBoxParser.Parse(
                shortInput,
                out var shortTable));
        Assert.Null(shortTable);
        Assert.Equal(
            PlaybackIsoBmffChunkLargeOffsetBoxParseResult.BoxLengthMismatch,
            PlaybackIsoBmffChunkLargeOffsetBoxParser.Parse(
                trailingInput,
                out var trailingTable));
        Assert.Null(trailingTable);
    }

    private static byte[] CreateBox(params ulong[] chunkOffsets) =>
        CreateBoxWithHeader(chunkOffsets);

    private static byte[] CreateBoxWithHeader(
        ulong[] chunkOffsets,
        bool extended = false,
        bool extendsToEnd = false)
    {
        var headerSize = extended ? 16 : 8;
        var box = new byte[headerSize + 8 + (chunkOffsets.Length * 8)];
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            extendsToEnd ? 0 : extended ? 1 : (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Co64);
        if (extended)
        {
            BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(8, 8), (ulong)box.Length);
        }

        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(headerSize + 4, 4),
            (uint)chunkOffsets.Length);
        for (var index = 0; index < chunkOffsets.Length; index++)
        {
            BinaryPrimitives.WriteUInt64BigEndian(
                box.AsSpan(headerSize + 8 + (index * 8), 8),
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
