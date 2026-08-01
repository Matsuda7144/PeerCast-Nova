using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffChunkOffsetLookupTests
{
    private const uint Stco = 0x7374636F;
    private const uint Co64 = 0x636F3634;

    [Fact]
    public void Lookup_ProjectsStandardOffsetAsUnsigned64Bit()
    {
        var result = PlaybackIsoBmffChunkOffsetLookup.Lookup(
            Reconciliation(3, PlaybackIsoBmffChunkOffsetTableKind.Standard),
            ParseChunkOffset(10, uint.MaxValue, 30),
            null,
            2,
            out var offset);

        Assert.Equal(PlaybackIsoBmffChunkOffsetLookupResult.Found, result);
        Assert.Equal((ulong)uint.MaxValue, offset);
    }

    [Fact]
    public void Lookup_ProjectsLargeOffset()
    {
        var result = PlaybackIsoBmffChunkOffsetLookup.Lookup(
            Reconciliation(1, PlaybackIsoBmffChunkOffsetTableKind.Large),
            null,
            ParseChunkLargeOffset(ulong.MaxValue),
            1,
            out var offset);

        Assert.Equal(PlaybackIsoBmffChunkOffsetLookupResult.Found, result);
        Assert.Equal(ulong.MaxValue, offset);
    }

    [Fact]
    public void Lookup_OffsetKindMustMatchTable()
    {
        var result = PlaybackIsoBmffChunkOffsetLookup.Lookup(
            Reconciliation(1, PlaybackIsoBmffChunkOffsetTableKind.Standard),
            null,
            ParseChunkLargeOffset(10),
            1,
            out var offset);

        Assert.Equal(
            PlaybackIsoBmffChunkOffsetLookupResult.InvalidReconciliation,
            result);
        Assert.Null(offset);
    }

    [Fact]
    public void Lookup_ConflictingOffsetTablesAreRejected()
    {
        var result = PlaybackIsoBmffChunkOffsetLookup.Lookup(
            Reconciliation(1, PlaybackIsoBmffChunkOffsetTableKind.Standard),
            ParseChunkOffset(10),
            ParseChunkLargeOffset(10),
            1,
            out var offset);

        Assert.Equal(
            PlaybackIsoBmffChunkOffsetLookupResult.InvalidReconciliation,
            result);
        Assert.Null(offset);
    }

    [Fact]
    public void Lookup_OffsetCountMustMatchReconciliation()
    {
        var result = PlaybackIsoBmffChunkOffsetLookup.Lookup(
            Reconciliation(3, PlaybackIsoBmffChunkOffsetTableKind.Standard),
            ParseChunkOffset(10, 20),
            null,
            1,
            out var offset);

        Assert.Equal(
            PlaybackIsoBmffChunkOffsetLookupResult.InvalidReconciliation,
            result);
        Assert.Null(offset);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Lookup_ChunkNumberMustBeInRange(uint chunkNumber)
    {
        var result = PlaybackIsoBmffChunkOffsetLookup.Lookup(
            Reconciliation(3, PlaybackIsoBmffChunkOffsetTableKind.Standard),
            ParseChunkOffset(10, 20, 30),
            null,
            chunkNumber,
            out var offset);

        Assert.Equal(
            PlaybackIsoBmffChunkOffsetLookupResult.ChunkNumberOutOfRange,
            result);
        Assert.Null(offset);
    }

    [Fact]
    public void Lookup_EmptyTableHasNoChunk()
    {
        var result = PlaybackIsoBmffChunkOffsetLookup.Lookup(
            Reconciliation(0, PlaybackIsoBmffChunkOffsetTableKind.Standard),
            ParseChunkOffset(),
            null,
            1,
            out var offset);

        Assert.Equal(
            PlaybackIsoBmffChunkOffsetLookupResult.ChunkNumberOutOfRange,
            result);
        Assert.Null(offset);
    }

    private static PlaybackIsoBmffReconciledChunkTable Reconciliation(
        uint chunkCount,
        PlaybackIsoBmffChunkOffsetTableKind kind) =>
        new(chunkCount, chunkCount == 0 ? 0u : 1u, kind, chunkCount);

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

    private static void WriteHeader(byte[] box, uint type)
    {
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), type);
    }
}
