using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffSampleSizeLookupTests
{
    private const uint Stsz = 0x7374737A;
    private const uint Stz2 = 0x73747A32;

    [Fact]
    public void Lookup_UniformStandardSizeDoesNotRequireExpansion()
    {
        var sampleSize = ParseUniformSampleSize(4, 3);
        Assert.Empty(sampleSize.SampleSizes);

        var result = PlaybackIsoBmffSampleSizeLookup.Lookup(
            Reconciliation(3, 12, PlaybackIsoBmffSampleSizeTableKind.Standard),
            sampleSize,
            null,
            2,
            out var size);

        Assert.Equal(PlaybackIsoBmffSampleSizeLookupResult.Found, result);
        Assert.Equal((uint)4, size);
    }

    [Fact]
    public void Lookup_ProjectsIndividualStandardSize()
    {
        var result = PlaybackIsoBmffSampleSizeLookup.Lookup(
            Reconciliation(3, 60, PlaybackIsoBmffSampleSizeTableKind.Standard),
            ParseIndividualSampleSize(10, 20, 30),
            null,
            2,
            out var size);

        Assert.Equal(PlaybackIsoBmffSampleSizeLookupResult.Found, result);
        Assert.Equal((uint)20, size);
    }

    [Fact]
    public void Lookup_ProjectsCompactSize()
    {
        var result = PlaybackIsoBmffSampleSizeLookup.Lookup(
            Reconciliation(3, 60, PlaybackIsoBmffSampleSizeTableKind.Compact),
            null,
            ParseCompactSampleSize(10, 20, 30),
            3,
            out var size);

        Assert.Equal(PlaybackIsoBmffSampleSizeLookupResult.Found, result);
        Assert.Equal((uint)30, size);
    }

    [Fact]
    public void Lookup_SizeKindMustMatchTable()
    {
        var result = PlaybackIsoBmffSampleSizeLookup.Lookup(
            Reconciliation(1, 4, PlaybackIsoBmffSampleSizeTableKind.Standard),
            null,
            ParseCompactSampleSize(4),
            1,
            out var size);

        Assert.Equal(
            PlaybackIsoBmffSampleSizeLookupResult.InvalidReconciliation,
            result);
        Assert.Null(size);
    }

    [Fact]
    public void Lookup_ConflictingSizeTablesAreRejected()
    {
        var result = PlaybackIsoBmffSampleSizeLookup.Lookup(
            Reconciliation(1, 4, PlaybackIsoBmffSampleSizeTableKind.Standard),
            ParseUniformSampleSize(4, 1),
            ParseCompactSampleSize(4),
            1,
            out var size);

        Assert.Equal(
            PlaybackIsoBmffSampleSizeLookupResult.InvalidReconciliation,
            result);
        Assert.Null(size);
    }

    [Fact]
    public void Lookup_SampleCountMustMatchReconciliation()
    {
        var result = PlaybackIsoBmffSampleSizeLookup.Lookup(
            Reconciliation(3, 8, PlaybackIsoBmffSampleSizeTableKind.Standard),
            ParseUniformSampleSize(4, 2),
            null,
            1,
            out var size);

        Assert.Equal(
            PlaybackIsoBmffSampleSizeLookupResult.InvalidReconciliation,
            result);
        Assert.Null(size);
    }

    [Fact]
    public void Lookup_TotalBytesMustMatchReconciliation()
    {
        var result = PlaybackIsoBmffSampleSizeLookup.Lookup(
            Reconciliation(2, 7, PlaybackIsoBmffSampleSizeTableKind.Standard),
            ParseUniformSampleSize(4, 2),
            null,
            1,
            out var size);

        Assert.Equal(
            PlaybackIsoBmffSampleSizeLookupResult.InvalidReconciliation,
            result);
        Assert.Null(size);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Lookup_SampleNumberMustBeInRange(uint sampleNumber)
    {
        var result = PlaybackIsoBmffSampleSizeLookup.Lookup(
            Reconciliation(3, 12, PlaybackIsoBmffSampleSizeTableKind.Standard),
            ParseUniformSampleSize(4, 3),
            null,
            sampleNumber,
            out var size);

        Assert.Equal(
            PlaybackIsoBmffSampleSizeLookupResult.SampleNumberOutOfRange,
            result);
        Assert.Null(size);
    }

    [Fact]
    public void Lookup_EmptyTableHasNoSample()
    {
        var result = PlaybackIsoBmffSampleSizeLookup.Lookup(
            Reconciliation(0, 0, PlaybackIsoBmffSampleSizeTableKind.Standard),
            ParseUniformSampleSize(4, 0),
            null,
            1,
            out var size);

        Assert.Equal(
            PlaybackIsoBmffSampleSizeLookupResult.SampleNumberOutOfRange,
            result);
        Assert.Null(size);
    }

    private static PlaybackIsoBmffReconciledSampleCount Reconciliation(
        uint sampleCount,
        ulong totalBytes,
        PlaybackIsoBmffSampleSizeTableKind kind) =>
        new(sampleCount, totalBytes, kind);

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
