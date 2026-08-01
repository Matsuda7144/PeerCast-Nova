using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffChunkTableReconcilerTests
{
    private const uint Stsc = 0x73747363;
    private const uint Stco = 0x7374636F;
    private const uint Co64 = 0x636F3634;
    private const uint Stsd = 0x73747364;
    private const uint Avc1 = 0x61766331;

    [Fact]
    public void Reconcile_ProjectsStandardChunkOffsetTable()
    {
        var result = PlaybackIsoBmffChunkTableReconciler.Reconcile(
            ParseSampleToChunk((1, 2, 1), (3, 1, 1)),
            ParseChunkOffset(10, 20, 30),
            null,
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffChunkTableReconciliationResult.Reconciled,
            result);
        Assert.Equal(
            new PlaybackIsoBmffReconciledChunkTable(
                3,
                2,
                PlaybackIsoBmffChunkOffsetTableKind.Standard,
                5),
            reconciliation);
    }

    [Fact]
    public void Reconcile_ProjectsLargeChunkOffsetTable()
    {
        var result = PlaybackIsoBmffChunkTableReconciler.Reconcile(
            ParseSampleToChunk((1, 1, 1)),
            null,
            ParseChunkLargeOffset(ulong.MaxValue),
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffChunkTableReconciliationResult.Reconciled,
            result);
        Assert.Equal(
            PlaybackIsoBmffChunkOffsetTableKind.Large,
            reconciliation?.ChunkOffsetTableKind);
        Assert.Equal((uint)1, reconciliation?.ChunkCount);
        Assert.Equal((ulong)1, reconciliation?.TotalSampleCount);
    }

    [Fact]
    public void Reconcile_EmptyTablesAreValid()
    {
        var result = PlaybackIsoBmffChunkTableReconciler.Reconcile(
            ParseSampleToChunk(),
            ParseChunkOffset(),
            null,
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffChunkTableReconciliationResult.Reconciled,
            result);
        Assert.Equal((uint)0, reconciliation?.ChunkCount);
        Assert.Equal((uint)0, reconciliation?.SampleToChunkRunCount);
        Assert.Equal((ulong)0, reconciliation?.TotalSampleCount);
    }

    [Fact]
    public void Reconcile_SampleToChunkIsRequired()
    {
        var result = PlaybackIsoBmffChunkTableReconciler.Reconcile(
            null,
            ParseChunkOffset(),
            null,
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffChunkTableReconciliationResult.MissingSampleToChunk,
            result);
        Assert.Null(reconciliation);
    }

    [Fact]
    public void Reconcile_ExactlyOneChunkOffsetTableIsRequired()
    {
        var sampleToChunk = ParseSampleToChunk();

        Assert.Equal(
            PlaybackIsoBmffChunkTableReconciliationResult
                .MissingChunkOffsetTable,
            PlaybackIsoBmffChunkTableReconciler.Reconcile(
                sampleToChunk,
                null,
                null,
                out var missing));
        Assert.Null(missing);

        Assert.Equal(
            PlaybackIsoBmffChunkTableReconciliationResult
                .ConflictingChunkOffsetTables,
            PlaybackIsoBmffChunkTableReconciler.Reconcile(
                sampleToChunk,
                ParseChunkOffset(),
                ParseChunkLargeOffset(),
                out var conflicting));
        Assert.Null(conflicting);
    }

    [Fact]
    public void Reconcile_NonemptyChunksRequireSampleToChunkRun()
    {
        var result = PlaybackIsoBmffChunkTableReconciler.Reconcile(
            ParseSampleToChunk(),
            ParseChunkOffset(10),
            null,
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffChunkTableReconciliationResult
                .SampleToChunkCoverageMismatch,
            result);
        Assert.Null(reconciliation);
    }

    [Fact]
    public void Reconcile_EmptyChunksRejectSampleToChunkRun()
    {
        var result = PlaybackIsoBmffChunkTableReconciler.Reconcile(
            ParseSampleToChunk((1, 1, 1)),
            ParseChunkOffset(),
            null,
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffChunkTableReconciliationResult
                .SampleToChunkCoverageMismatch,
            result);
        Assert.Null(reconciliation);
    }

    [Fact]
    public void Reconcile_FirstChunkMustBeWithinOffsetTable()
    {
        var result = PlaybackIsoBmffChunkTableReconciler.Reconcile(
            ParseSampleToChunk((1, 2, 1), (4, 1, 1)),
            ParseChunkOffset(10, 20, 30),
            null,
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffChunkTableReconciliationResult
                .SampleToChunkCoverageMismatch,
            result);
        Assert.Null(reconciliation);
    }

    [Fact]
    public void Reconcile_MatchingExpectedSampleCountIsValid()
    {
        var result = PlaybackIsoBmffChunkTableReconciler.Reconcile(
            ParseSampleToChunk((1, 2, 1), (3, 1, 1)),
            ParseChunkOffset(10, 20, 30),
            null,
            5,
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffChunkTableReconciliationResult.Reconciled,
            result);
        Assert.Equal((ulong)5, reconciliation?.TotalSampleCount);
    }

    [Fact]
    public void Reconcile_ExpectedSampleCountMustMatch()
    {
        var result = PlaybackIsoBmffChunkTableReconciler.Reconcile(
            ParseSampleToChunk((1, 2, 1), (3, 1, 1)),
            ParseChunkOffset(10, 20, 30),
            null,
            4,
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffChunkTableReconciliationResult.SampleCountMismatch,
            result);
        Assert.Null(reconciliation);
    }

    [Fact]
    public void Reconcile_InRangeSampleDescriptionIndexesAreProjected()
    {
        var result = PlaybackIsoBmffChunkTableReconciler.Reconcile(
            ParseSampleToChunk((1, 2, 1), (3, 1, 2)),
            ParseChunkOffset(10, 20, 30),
            null,
            ParseSampleDescription(2),
            5,
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffChunkTableReconciliationResult.Reconciled,
            result);
        Assert.Equal((uint)2, reconciliation?.SampleDescriptionCount);
        Assert.Equal((ulong)5, reconciliation?.TotalSampleCount);
    }

    [Fact]
    public void Reconcile_ExplicitSampleDescriptionIsRequired()
    {
        var result = PlaybackIsoBmffChunkTableReconciler.Reconcile(
            ParseSampleToChunk(),
            ParseChunkOffset(),
            null,
            (PlaybackIsoBmffSampleDescriptionBox?)null,
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffChunkTableReconciliationResult
                .MissingSampleDescription,
            result);
        Assert.Null(reconciliation);
    }

    [Fact]
    public void Reconcile_SampleDescriptionIndexMustBeInRange()
    {
        var result = PlaybackIsoBmffChunkTableReconciler.Reconcile(
            ParseSampleToChunk((1, 1, 2)),
            ParseChunkOffset(10),
            null,
            ParseSampleDescription(1),
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffChunkTableReconciliationResult
                .SampleDescriptionIndexOutOfRange,
            result);
        Assert.Null(reconciliation);
    }

    [Fact]
    public void Reconcile_EmptyDescriptionAndChunkTablesAreValid()
    {
        var result = PlaybackIsoBmffChunkTableReconciler.Reconcile(
            ParseSampleToChunk(),
            ParseChunkOffset(),
            null,
            ParseSampleDescription(0),
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffChunkTableReconciliationResult.Reconciled,
            result);
        Assert.Equal((uint)0, reconciliation?.SampleDescriptionCount);
    }

    [Theory]
    [InlineData(1, 0, 1, 2, 2, 1)]
    [InlineData(3, 1, 3, 3, 1, 2)]
    public void Lookup_ProjectsContainingRun(
        uint chunkNumber,
        uint runIndex,
        uint firstChunk,
        uint lastChunk,
        uint samplesPerChunk,
        uint sampleDescriptionIndex)
    {
        var sampleToChunk = ParseSampleToChunk((1, 2, 1), (3, 1, 2));
        Assert.Equal(
            PlaybackIsoBmffChunkTableReconciliationResult.Reconciled,
            PlaybackIsoBmffChunkTableReconciler.Reconcile(
                sampleToChunk,
                ParseChunkOffset(10, 20, 30),
                null,
                ParseSampleDescription(2),
                out var reconciliation));

        var result = PlaybackIsoBmffChunkRunLookup.Lookup(
            sampleToChunk,
            reconciliation,
            chunkNumber,
            out var run);

        Assert.Equal(PlaybackIsoBmffChunkRunLookupResult.Found, result);
        Assert.Equal(
            new PlaybackIsoBmffChunkRun(
                runIndex,
                firstChunk,
                lastChunk,
                samplesPerChunk,
                sampleDescriptionIndex),
            run);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Lookup_ChunkNumberMustBeInRange(uint chunkNumber)
    {
        var sampleToChunk = ParseSampleToChunk((1, 1, 1));
        Assert.Equal(
            PlaybackIsoBmffChunkTableReconciliationResult.Reconciled,
            PlaybackIsoBmffChunkTableReconciler.Reconcile(
                sampleToChunk,
                ParseChunkOffset(10, 20, 30),
                null,
                out var reconciliation));

        Assert.Equal(
            PlaybackIsoBmffChunkRunLookupResult.ChunkNumberOutOfRange,
            PlaybackIsoBmffChunkRunLookup.Lookup(
                sampleToChunk,
                reconciliation,
                chunkNumber,
                out var run));
        Assert.Null(run);
    }

    [Fact]
    public void Lookup_EmptyReconciliationHasNoChunk()
    {
        var sampleToChunk = ParseSampleToChunk();
        Assert.Equal(
            PlaybackIsoBmffChunkTableReconciliationResult.Reconciled,
            PlaybackIsoBmffChunkTableReconciler.Reconcile(
                sampleToChunk,
                ParseChunkOffset(),
                null,
                out var reconciliation));

        Assert.Equal(
            PlaybackIsoBmffChunkRunLookupResult.ChunkNumberOutOfRange,
            PlaybackIsoBmffChunkRunLookup.Lookup(
                sampleToChunk,
                reconciliation,
                1,
                out var run));
        Assert.Null(run);
    }

    [Fact]
    public void Lookup_RunCountMismatchIsRejected()
    {
        var result = PlaybackIsoBmffChunkRunLookup.Lookup(
            ParseSampleToChunk((1, 2, 1), (3, 1, 1)),
            new(
                3,
                1,
                PlaybackIsoBmffChunkOffsetTableKind.Standard,
                5),
            1,
            out var run);

        Assert.Equal(
            PlaybackIsoBmffChunkRunLookupResult.InvalidReconciliation,
            result);
        Assert.Null(run);
    }

    [Fact]
    public void Lookup_TotalSampleCountMismatchIsRejected()
    {
        var result = PlaybackIsoBmffChunkRunLookup.Lookup(
            ParseSampleToChunk((1, 2, 1), (3, 1, 1)),
            new(
                3,
                2,
                PlaybackIsoBmffChunkOffsetTableKind.Standard,
                4),
            1,
            out var run);

        Assert.Equal(
            PlaybackIsoBmffChunkRunLookupResult.InvalidReconciliation,
            result);
        Assert.Null(run);
    }

    [Fact]
    public void Lookup_DescriptionCountMismatchIsRejected()
    {
        var result = PlaybackIsoBmffChunkRunLookup.Lookup(
            ParseSampleToChunk((1, 2, 1), (3, 1, 2)),
            new(
                3,
                2,
                PlaybackIsoBmffChunkOffsetTableKind.Standard,
                5,
                1),
            3,
            out var run);

        Assert.Equal(
            PlaybackIsoBmffChunkRunLookupResult.InvalidReconciliation,
            result);
        Assert.Null(run);
    }

    [Theory]
    [InlineData(1, 0, 1, 1, 0, 2, 1)]
    [InlineData(4, 0, 2, 3, 1, 2, 1)]
    [InlineData(5, 1, 3, 5, 0, 1, 2)]
    public void SampleLookup_ProjectsContainingChunk(
        ulong sampleNumber,
        uint runIndex,
        uint chunkNumber,
        ulong firstSampleNumberInChunk,
        uint sampleIndexWithinChunk,
        uint samplesPerChunk,
        uint sampleDescriptionIndex)
    {
        var sampleToChunk = ParseSampleToChunk((1, 2, 1), (3, 1, 2));
        Assert.Equal(
            PlaybackIsoBmffChunkTableReconciliationResult.Reconciled,
            PlaybackIsoBmffChunkTableReconciler.Reconcile(
                sampleToChunk,
                ParseChunkOffset(10, 20, 30),
                null,
                ParseSampleDescription(2),
                out var reconciliation));

        var result = PlaybackIsoBmffSampleChunkLookup.Lookup(
            sampleToChunk,
            reconciliation,
            sampleNumber,
            out var location);

        Assert.Equal(PlaybackIsoBmffSampleChunkLookupResult.Found, result);
        Assert.Equal(
            new PlaybackIsoBmffSampleChunkLocation(
                runIndex,
                chunkNumber,
                firstSampleNumberInChunk,
                sampleIndexWithinChunk,
                samplesPerChunk,
                sampleDescriptionIndex),
            location);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void SampleLookup_SampleNumberMustBeInRange(ulong sampleNumber)
    {
        var sampleToChunk = ParseSampleToChunk((1, 2, 1), (3, 1, 1));
        Assert.Equal(
            PlaybackIsoBmffChunkTableReconciliationResult.Reconciled,
            PlaybackIsoBmffChunkTableReconciler.Reconcile(
                sampleToChunk,
                ParseChunkOffset(10, 20, 30),
                null,
                out var reconciliation));

        Assert.Equal(
            PlaybackIsoBmffSampleChunkLookupResult.SampleNumberOutOfRange,
            PlaybackIsoBmffSampleChunkLookup.Lookup(
                sampleToChunk,
                reconciliation,
                sampleNumber,
                out var location));
        Assert.Null(location);
    }

    [Fact]
    public void SampleLookup_EmptyReconciliationHasNoSample()
    {
        var sampleToChunk = ParseSampleToChunk();
        Assert.Equal(
            PlaybackIsoBmffChunkTableReconciliationResult.Reconciled,
            PlaybackIsoBmffChunkTableReconciler.Reconcile(
                sampleToChunk,
                ParseChunkOffset(),
                null,
                out var reconciliation));

        Assert.Equal(
            PlaybackIsoBmffSampleChunkLookupResult.SampleNumberOutOfRange,
            PlaybackIsoBmffSampleChunkLookup.Lookup(
                sampleToChunk,
                reconciliation,
                1,
                out var location));
        Assert.Null(location);
    }

    [Fact]
    public void SampleLookup_InvalidReconciliationIsRejected()
    {
        var result = PlaybackIsoBmffSampleChunkLookup.Lookup(
            ParseSampleToChunk((1, 2, 1), (3, 1, 1)),
            new(
                3,
                2,
                PlaybackIsoBmffChunkOffsetTableKind.Standard,
                4),
            1,
            out var location);

        Assert.Equal(
            PlaybackIsoBmffSampleChunkLookupResult.InvalidReconciliation,
            result);
        Assert.Null(location);
    }

    [Fact]
    public void SampleLookup_MaximumSamplesPerChunkUsesWideArithmetic()
    {
        var sampleToChunk = ParseSampleToChunk((1, uint.MaxValue, 1));
        Assert.Equal(
            PlaybackIsoBmffChunkTableReconciliationResult.Reconciled,
            PlaybackIsoBmffChunkTableReconciler.Reconcile(
                sampleToChunk,
                ParseChunkOffset(10),
                null,
                out var reconciliation));

        Assert.Equal(
            PlaybackIsoBmffSampleChunkLookupResult.Found,
            PlaybackIsoBmffSampleChunkLookup.Lookup(
                sampleToChunk,
                reconciliation,
                uint.MaxValue,
                out var location));
        Assert.Equal(uint.MaxValue - 1, location?.SampleIndexWithinChunk);
        Assert.Equal((uint)1, location?.ChunkNumber);
    }

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

    private static PlaybackIsoBmffSampleDescriptionBox ParseSampleDescription(
        int entryCount)
    {
        var box = new byte[16 + (entryCount * 8)];
        WriteHeader(box, Stsd);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(12, 4),
            (uint)entryCount);
        for (var index = 0; index < entryCount; index++)
        {
            var offset = 16 + (index * 8);
            BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(offset, 4), 8);
            BinaryPrimitives.WriteUInt32BigEndian(
                box.AsSpan(offset + 4, 4),
                Avc1);
        }

        Assert.Equal(
            PlaybackIsoBmffSampleDescriptionBoxParseResult.Parsed,
            PlaybackIsoBmffSampleDescriptionBoxParser.Parse(
                box,
                out var parsed));
        return Assert.IsType<PlaybackIsoBmffSampleDescriptionBox>(parsed);
    }

    private static void WriteHeader(byte[] box, uint type)
    {
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), type);
    }
}
