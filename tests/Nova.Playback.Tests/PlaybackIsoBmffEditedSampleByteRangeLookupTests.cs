using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffEditedSampleByteRangeLookupTests
{
    [Fact]
    public void Lookup_BindsAdmittedSampleNumberToAbsoluteRange()
    {
        var admission = CreateAdmission(2);
        var result = PlaybackIsoBmffEditedSampleByteRangeLookup.Lookup(
            admission,
            ParseSampleToChunk((1, 3, 1)),
            new(1, 1, PlaybackIsoBmffChunkOffsetTableKind.Standard, 3),
            ParseChunkOffset(100),
            null,
            new(3, 12, PlaybackIsoBmffSampleSizeTableKind.Standard),
            ParseUniformSampleSize(4, 3),
            null,
            out var editedRange);

        Assert.Equal(
            PlaybackIsoBmffEditedSampleByteRangeLookupResult.Found,
            result);
        var found = Assert.IsType<PlaybackIsoBmffEditedSampleByteRange>(
            editedRange);
        Assert.Same(admission, found.Admission);
        Assert.Equal((ulong)2, found.SampleNumber);
        Assert.Equal(new PlaybackIsoBmffSampleByteRange(104, 4, 108),
            found.Range);
        Assert.Equal((uint)1, found.SampleDescriptionIndex);
    }

    [Fact]
    public void Lookup_MissingAdmissionRejectsBeforeTableAccess()
    {
        var result = PlaybackIsoBmffEditedSampleByteRangeLookup.Lookup(
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            out var editedRange);

        Assert.Equal(
            PlaybackIsoBmffEditedSampleByteRangeLookupResult.MissingAdmission,
            result);
        Assert.Null(editedRange);
    }

    [Fact]
    public void Lookup_InvalidReconciliationFailsClosed()
    {
        var result = PlaybackIsoBmffEditedSampleByteRangeLookup.Lookup(
            CreateAdmission(1),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            out var editedRange);

        Assert.Equal(
            PlaybackIsoBmffEditedSampleByteRangeLookupResult
                .InvalidReconciliation,
            result);
        Assert.Null(editedRange);
    }

    [Fact]
    public void Lookup_AdmissionOutsideReconciledSamplesIsDistinct()
    {
        var result = PlaybackIsoBmffEditedSampleByteRangeLookup.Lookup(
            CreateAdmission(4),
            ParseSampleToChunk((1, 3, 1)),
            new(1, 1, PlaybackIsoBmffChunkOffsetTableKind.Standard, 3),
            ParseChunkOffset(100),
            null,
            new(3, 12, PlaybackIsoBmffSampleSizeTableKind.Standard),
            ParseUniformSampleSize(4, 3),
            null,
            out var editedRange);

        Assert.Equal(
            PlaybackIsoBmffEditedSampleByteRangeLookupResult
                .SampleNumberOutOfRange,
            result);
        Assert.Null(editedRange);
    }

    private static PlaybackIsoBmffEditedSampleAdmission CreateAdmission(
        ulong sampleNumber)
    {
        var sample = new PlaybackIsoBmffSamplePresentationTime(0, 1, 1, 0,
            false);
        var appearances = Array.AsReadOnly(
            new[]
            {
                new PlaybackIsoBmffSampleEditMovieOverlap(
                    0,
                    0,
                    1,
                    1,
                    true),
            });
        return new(
            sampleNumber,
            new PlaybackIsoBmffCompleteSampleEditAdmission(sample, appearances));
    }

    internal static PlaybackIsoBmffSampleToChunkBox ParseSampleToChunk(
        params (uint FirstChunk, uint SamplesPerChunk,
            uint SampleDescriptionIndex)[] entries)
    {
        var box = new byte[16 + (entries.Length * 12)];
        WriteHeader(box, 0x73747363);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4),
            (uint)entries.Length);
        for (var index = 0; index < entries.Length; index++)
        {
            var offset = 16 + (index * 12);
            BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(offset, 4),
                entries[index].FirstChunk);
            BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(offset + 4, 4),
                entries[index].SamplesPerChunk);
            BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(offset + 8, 4),
                entries[index].SampleDescriptionIndex);
        }

        Assert.Equal(
            PlaybackIsoBmffSampleToChunkBoxParseResult.Parsed,
            PlaybackIsoBmffSampleToChunkBoxParser.Parse(box, out var parsed));
        return Assert.IsType<PlaybackIsoBmffSampleToChunkBox>(parsed);
    }

    internal static PlaybackIsoBmffChunkOffsetBox ParseChunkOffset(
        params uint[] offsets)
    {
        var box = new byte[16 + (offsets.Length * 4)];
        WriteHeader(box, 0x7374636F);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4),
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

    internal static PlaybackIsoBmffSampleSizeBox ParseUniformSampleSize(
        uint size,
        uint sampleCount)
    {
        var box = new byte[20];
        WriteHeader(box, 0x7374737A);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4), size);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(16, 4), sampleCount);
        Assert.Equal(
            PlaybackIsoBmffSampleSizeBoxParseResult.Parsed,
            PlaybackIsoBmffSampleSizeBoxParser.Parse(box, out var parsed));
        return Assert.IsType<PlaybackIsoBmffSampleSizeBox>(parsed);
    }

    private static void WriteHeader(byte[] box, uint type)
    {
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4),
            (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), type);
    }
}
