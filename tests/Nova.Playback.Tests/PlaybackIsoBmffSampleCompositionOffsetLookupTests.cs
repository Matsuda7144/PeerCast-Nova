using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffSampleCompositionOffsetLookupTests
{
    private const uint Ctts = 0x63747473;

    [Fact]
    public void Lookup_VersionZeroPreservesUnsignedOffset()
    {
        var result = Lookup(
            Parse(0, (1, uint.MaxValue)),
            sampleCount: 1,
            sampleNumber: 1,
            out var offset);

        Assert.Equal(
            PlaybackIsoBmffSampleCompositionOffsetLookupResult.Found,
            result);
        Assert.Equal(
            new PlaybackIsoBmffSampleCompositionOffset(
                uint.MaxValue,
                HasExplicitOffset: true),
            offset);
    }

    [Fact]
    public void Lookup_VersionOnePreservesNegativeOffset()
    {
        var result = Lookup(
            Parse(1, (1, unchecked((uint)-2))),
            sampleCount: 1,
            sampleNumber: 1,
            out var offset);

        Assert.Equal(
            PlaybackIsoBmffSampleCompositionOffsetLookupResult.Found,
            result);
        Assert.Equal(
            new PlaybackIsoBmffSampleCompositionOffset(
                -2,
                HasExplicitOffset: true),
            offset);
    }

    [Fact]
    public void Lookup_SelectsRunContainingSampleWithoutExpansion()
    {
        var result = Lookup(
            Parse(1, (3, 10), (2, unchecked((uint)-5))),
            sampleCount: 5,
            sampleNumber: 4,
            out var offset);

        Assert.Equal(
            PlaybackIsoBmffSampleCompositionOffsetLookupResult.Found,
            result);
        Assert.Equal(
            new PlaybackIsoBmffSampleCompositionOffset(
                -5,
                HasExplicitOffset: true),
            offset);
    }

    [Fact]
    public void Lookup_AbsentOptionalTableProjectsImplicitZero()
    {
        var result = PlaybackIsoBmffSampleCompositionOffsetLookup.Lookup(
            null,
            Reconciliation(2, hasCompositionOffsets: false),
            2,
            out var offset);

        Assert.Equal(
            PlaybackIsoBmffSampleCompositionOffsetLookupResult.Found,
            result);
        Assert.Equal(
            new PlaybackIsoBmffSampleCompositionOffset(
                0,
                HasExplicitOffset: false),
            offset);
    }

    [Fact]
    public void Lookup_LargeUniformRunDoesNotExpandSamples()
    {
        var result = Lookup(
            Parse(0, (uint.MaxValue, 7)),
            sampleCount: uint.MaxValue,
            sampleNumber: uint.MaxValue,
            out var offset);

        Assert.Equal(
            PlaybackIsoBmffSampleCompositionOffsetLookupResult.Found,
            result);
        Assert.Equal(
            new PlaybackIsoBmffSampleCompositionOffset(
                7,
                HasExplicitOffset: true),
            offset);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Lookup_SampleNumberMustBeInRange(ulong sampleNumber)
    {
        var result = Lookup(
            Parse(0, (2, 1)),
            sampleCount: 2,
            sampleNumber,
            out var offset);

        Assert.Equal(
            PlaybackIsoBmffSampleCompositionOffsetLookupResult
                .SampleNumberOutOfRange,
            result);
        Assert.Null(offset);
    }

    [Fact]
    public void Lookup_PresenceFlagAndTableMustAgree()
    {
        Assert.Equal(
            PlaybackIsoBmffSampleCompositionOffsetLookupResult
                .InvalidReconciliation,
            PlaybackIsoBmffSampleCompositionOffsetLookup.Lookup(
                Parse(0, (1, 1)),
                Reconciliation(1, hasCompositionOffsets: false),
                1,
                out var unexpectedTable));
        Assert.Null(unexpectedTable);
        Assert.Equal(
            PlaybackIsoBmffSampleCompositionOffsetLookupResult
                .InvalidReconciliation,
            PlaybackIsoBmffSampleCompositionOffsetLookup.Lookup(
                null,
                Reconciliation(1, hasCompositionOffsets: true),
                1,
                out var missingTable));
        Assert.Null(missingTable);
    }

    [Fact]
    public void Lookup_ReconciledCountMustMatchCompositionTable()
    {
        var result = Lookup(
            Parse(0, (2, 1)),
            sampleCount: 1,
            sampleNumber: 1,
            out var offset);

        Assert.Equal(
            PlaybackIsoBmffSampleCompositionOffsetLookupResult
                .InvalidReconciliation,
            result);
        Assert.Null(offset);
    }

    [Fact]
    public void Lookup_EmptyReconciliationHasNoSample()
    {
        var result = PlaybackIsoBmffSampleCompositionOffsetLookup.Lookup(
            null,
            Reconciliation(0, hasCompositionOffsets: false),
            1,
            out var offset);

        Assert.Equal(
            PlaybackIsoBmffSampleCompositionOffsetLookupResult
                .SampleNumberOutOfRange,
            result);
        Assert.Null(offset);
    }

    private static PlaybackIsoBmffSampleCompositionOffsetLookupResult Lookup(
        PlaybackIsoBmffCompositionTimeToSampleBox compositionTimeToSample,
        uint sampleCount,
        ulong sampleNumber,
        out PlaybackIsoBmffSampleCompositionOffset? offset) =>
        PlaybackIsoBmffSampleCompositionOffsetLookup.Lookup(
            compositionTimeToSample,
            Reconciliation(sampleCount, hasCompositionOffsets: true),
            sampleNumber,
            out offset);

    private static PlaybackIsoBmffReconciledSampleCount Reconciliation(
        uint sampleCount,
        bool hasCompositionOffsets) =>
        new(
            sampleCount,
            0,
            PlaybackIsoBmffSampleSizeTableKind.Standard,
            HasCompositionOffsets: hasCompositionOffsets);

    private static PlaybackIsoBmffCompositionTimeToSampleBox Parse(
        byte version,
        params (uint Count, uint Offset)[] entries)
    {
        var box = new byte[16 + (entries.Length * 8)];
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Ctts);
        box[8] = version;
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(12, 4),
            (uint)entries.Length);
        for (var index = 0; index < entries.Length; index++)
        {
            var offset = 16 + (index * 8);
            BinaryPrimitives.WriteUInt32BigEndian(
                box.AsSpan(offset, 4),
                entries[index].Count);
            BinaryPrimitives.WriteUInt32BigEndian(
                box.AsSpan(offset + 4, 4),
                entries[index].Offset);
        }

        Assert.Equal(
            PlaybackIsoBmffCompositionTimeToSampleBoxParseResult.Parsed,
            PlaybackIsoBmffCompositionTimeToSampleBoxParser.Parse(
                box,
                out var parsed));
        return Assert.IsType<PlaybackIsoBmffCompositionTimeToSampleBox>(parsed);
    }
}
