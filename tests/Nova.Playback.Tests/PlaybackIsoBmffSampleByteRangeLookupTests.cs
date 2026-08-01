using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffSampleByteRangeLookupTests
{
    private const uint Stsc = 0x73747363;
    private const uint Stco = 0x7374636F;
    private const uint Co64 = 0x636F3634;
    private const uint Stsz = 0x7374737A;
    private const uint Stz2 = 0x73747A32;

    [Fact]
    public void Lookup_UniformSizeProjectsAbsoluteRangeWithoutExpansion()
    {
        var result = LookupStandard(
            ParseSampleToChunk((1, 3, 1)),
            new(1, 1, PlaybackIsoBmffChunkOffsetTableKind.Standard, 3),
            ParseChunkOffset(100),
            null,
            new(3, 12, PlaybackIsoBmffSampleSizeTableKind.Standard),
            ParseUniformSampleSize(4, 3),
            2,
            out var range);

        Assert.Equal(PlaybackIsoBmffSampleByteRangeLookupResult.Found, result);
        Assert.Equal(new PlaybackIsoBmffSampleByteRange(104, 4, 108), range);
    }

    [Fact]
    public void Lookup_IndividualSizesAccumulateOnlyWithinChunk()
    {
        var result = LookupStandard(
            ParseSampleToChunk((1, 2, 1)),
            new(2, 1, PlaybackIsoBmffChunkOffsetTableKind.Standard, 4),
            ParseChunkOffset(100, 200),
            null,
            new(4, 100, PlaybackIsoBmffSampleSizeTableKind.Standard),
            ParseIndividualSampleSize(10, 20, 30, 40),
            4,
            out var range);

        Assert.Equal(PlaybackIsoBmffSampleByteRangeLookupResult.Found, result);
        Assert.Equal(new PlaybackIsoBmffSampleByteRange(230, 40, 270), range);
    }

    [Fact]
    public void Lookup_CompactSizesProjectAbsoluteRange()
    {
        var sampleToChunk = ParseSampleToChunk((1, 3, 1));
        var result = PlaybackIsoBmffSampleByteRangeLookup.Lookup(
            sampleToChunk,
            new(1, 1, PlaybackIsoBmffChunkOffsetTableKind.Standard, 3),
            ParseChunkOffset(100),
            null,
            new(3, 60, PlaybackIsoBmffSampleSizeTableKind.Compact),
            null,
            ParseCompactSampleSize(10, 20, 30),
            3,
            out var range);

        Assert.Equal(PlaybackIsoBmffSampleByteRangeLookupResult.Found, result);
        Assert.Equal(new PlaybackIsoBmffSampleByteRange(130, 30, 160), range);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Lookup_SampleNumberMustBeInRange(ulong sampleNumber)
    {
        var result = LookupStandard(
            ParseSampleToChunk((1, 3, 1)),
            new(1, 1, PlaybackIsoBmffChunkOffsetTableKind.Standard, 3),
            ParseChunkOffset(100),
            null,
            new(3, 12, PlaybackIsoBmffSampleSizeTableKind.Standard),
            ParseUniformSampleSize(4, 3),
            sampleNumber,
            out var range);

        Assert.Equal(
            PlaybackIsoBmffSampleByteRangeLookupResult.SampleNumberOutOfRange,
            result);
        Assert.Null(range);
    }

    [Fact]
    public void Lookup_ChunkAndSampleCountsMustAgree()
    {
        var result = LookupStandard(
            ParseSampleToChunk((1, 3, 1)),
            new(1, 1, PlaybackIsoBmffChunkOffsetTableKind.Standard, 3),
            ParseChunkOffset(100),
            null,
            new(2, 8, PlaybackIsoBmffSampleSizeTableKind.Standard),
            ParseUniformSampleSize(4, 2),
            1,
            out var range);

        Assert.Equal(
            PlaybackIsoBmffSampleByteRangeLookupResult.InvalidReconciliation,
            result);
        Assert.Null(range);
    }

    [Fact]
    public void Lookup_EndOffsetOverflowIsRejected()
    {
        var result = LookupStandard(
            ParseSampleToChunk((1, 1, 1)),
            new(1, 1, PlaybackIsoBmffChunkOffsetTableKind.Large, 1),
            null,
            ParseChunkLargeOffset(ulong.MaxValue),
            new(1, 1, PlaybackIsoBmffSampleSizeTableKind.Standard),
            ParseUniformSampleSize(1, 1),
            1,
            out var range);

        Assert.Equal(
            PlaybackIsoBmffSampleByteRangeLookupResult.OffsetOverflow,
            result);
        Assert.Null(range);
    }

    [Fact]
    public void Lookup_PrecedingBytesOverflowIsRejected()
    {
        var result = LookupStandard(
            ParseSampleToChunk((1, 2, 1)),
            new(1, 1, PlaybackIsoBmffChunkOffsetTableKind.Large, 2),
            null,
            ParseChunkLargeOffset(ulong.MaxValue - 5),
            new(2, 11, PlaybackIsoBmffSampleSizeTableKind.Standard),
            ParseIndividualSampleSize(10, 1),
            2,
            out var range);

        Assert.Equal(
            PlaybackIsoBmffSampleByteRangeLookupResult.OffsetOverflow,
            result);
        Assert.Null(range);
    }

    [Fact]
    public void Lookup_EmptyTablesHaveNoSample()
    {
        var result = LookupStandard(
            ParseSampleToChunk(),
            new(0, 0, PlaybackIsoBmffChunkOffsetTableKind.Standard, 0),
            ParseChunkOffset(),
            null,
            new(0, 0, PlaybackIsoBmffSampleSizeTableKind.Standard),
            ParseUniformSampleSize(4, 0),
            1,
            out var range);

        Assert.Equal(
            PlaybackIsoBmffSampleByteRangeLookupResult.SampleNumberOutOfRange,
            result);
        Assert.Null(range);
    }

    private static PlaybackIsoBmffSampleByteRangeLookupResult LookupStandard(
        PlaybackIsoBmffSampleToChunkBox sampleToChunk,
        PlaybackIsoBmffReconciledChunkTable chunkReconciliation,
        PlaybackIsoBmffChunkOffsetBox? chunkOffset,
        PlaybackIsoBmffChunkLargeOffsetBox? chunkLargeOffset,
        PlaybackIsoBmffReconciledSampleCount sampleReconciliation,
        PlaybackIsoBmffSampleSizeBox sampleSize,
        ulong sampleNumber,
        out PlaybackIsoBmffSampleByteRange? range) =>
        PlaybackIsoBmffSampleByteRangeLookup.Lookup(
            sampleToChunk,
            chunkReconciliation,
            chunkOffset,
            chunkLargeOffset,
            sampleReconciliation,
            sampleSize,
            null,
            sampleNumber,
            out range);

    private static PlaybackIsoBmffSampleToChunkBox ParseSampleToChunk(
        params (uint FirstChunk, uint SamplesPerChunk,
            uint SampleDescriptionIndex)[] entries)
    {
        var box = new byte[16 + (entries.Length * 12)];
        WriteHeader(box, Stsc);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(12, 4),
            (uint)entries.Length);
        for (var index = 0; index < entries.Length; index++)
        {
            var offset = 16 + (index * 12);
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

        Assert.Equal(
            PlaybackIsoBmffSampleToChunkBoxParseResult.Parsed,
            PlaybackIsoBmffSampleToChunkBoxParser.Parse(box, out var parsed));
        return Assert.IsType<PlaybackIsoBmffSampleToChunkBox>(parsed);
    }

    private static PlaybackIsoBmffChunkOffsetBox ParseChunkOffset(
        params uint[] offsets)
    {
        var box = new byte[16 + (offsets.Length * 4)];
        WriteHeader(box, Stco);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(12, 4),
            (uint)offsets.Length);
        for (var index = 0; index < offsets.Length; index++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                box.AsSpan(16 + (index * 4), 4),
                offsets[index]);
        }

        Assert.Equal(
            PlaybackIsoBmffChunkOffsetBoxParseResult.Parsed,
            PlaybackIsoBmffChunkOffsetBoxParser.Parse(box, out var parsed));
        return Assert.IsType<PlaybackIsoBmffChunkOffsetBox>(parsed);
    }

    private static PlaybackIsoBmffChunkLargeOffsetBox ParseChunkLargeOffset(
        params ulong[] offsets)
    {
        var box = new byte[16 + (offsets.Length * 8)];
        WriteHeader(box, Co64);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(12, 4),
            (uint)offsets.Length);
        for (var index = 0; index < offsets.Length; index++)
        {
            BinaryPrimitives.WriteUInt64BigEndian(
                box.AsSpan(16 + (index * 8), 8),
                offsets[index]);
        }

        Assert.Equal(
            PlaybackIsoBmffChunkLargeOffsetBoxParseResult.Parsed,
            PlaybackIsoBmffChunkLargeOffsetBoxParser.Parse(
                box,
                out var parsed));
        return Assert.IsType<PlaybackIsoBmffChunkLargeOffsetBox>(parsed);
    }

    private static PlaybackIsoBmffSampleSizeBox ParseUniformSampleSize(
        uint size,
        uint sampleCount)
    {
        var box = new byte[20];
        WriteHeader(box, Stsz);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4), size);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(16, 4),
            sampleCount);
        Assert.Equal(
            PlaybackIsoBmffSampleSizeBoxParseResult.Parsed,
            PlaybackIsoBmffSampleSizeBoxParser.Parse(box, out var parsed));
        return Assert.IsType<PlaybackIsoBmffSampleSizeBox>(parsed);
    }

    private static PlaybackIsoBmffSampleSizeBox ParseIndividualSampleSize(
        params uint[] sizes)
    {
        var box = new byte[20 + (sizes.Length * 4)];
        WriteHeader(box, Stsz);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(16, 4),
            (uint)sizes.Length);
        for (var index = 0; index < sizes.Length; index++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                box.AsSpan(20 + (index * 4), 4),
                sizes[index]);
        }

        Assert.Equal(
            PlaybackIsoBmffSampleSizeBoxParseResult.Parsed,
            PlaybackIsoBmffSampleSizeBoxParser.Parse(box, out var parsed));
        return Assert.IsType<PlaybackIsoBmffSampleSizeBox>(parsed);
    }

    private static PlaybackIsoBmffCompactSampleSizeBox ParseCompactSampleSize(
        params ushort[] sizes)
    {
        var box = new byte[20 + (sizes.Length * 2)];
        WriteHeader(box, Stz2);
        box[15] = 16;
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(16, 4),
            (uint)sizes.Length);
        for (var index = 0; index < sizes.Length; index++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(
                box.AsSpan(20 + (index * 2), 2),
                sizes[index]);
        }

        Assert.Equal(
            PlaybackIsoBmffCompactSampleSizeBoxParseResult.Parsed,
            PlaybackIsoBmffCompactSampleSizeBoxParser.Parse(
                box,
                out var parsed));
        return Assert.IsType<PlaybackIsoBmffCompactSampleSizeBox>(parsed);
    }

    private static void WriteHeader(byte[] box, uint type)
    {
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), type);
    }
}
