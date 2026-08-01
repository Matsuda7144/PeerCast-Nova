using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffEditedSampleMediaDataLookupTests
{
    [Fact]
    public void Lookup_BindsAdmissionRangeAndContainingMediaData()
    {
        var editedRange = CreateEditedRange(2, new(20, 4, 24));
        var file = CreateFile((0x66726565, 12), (0x6D646174, 20));

        var result = PlaybackIsoBmffEditedSampleMediaDataLookup.Lookup(
            file,
            editedRange,
            out var mediaData);

        Assert.Equal(
            PlaybackIsoBmffEditedSampleMediaDataLookupResult.Found,
            result);
        var found = Assert.IsType<PlaybackIsoBmffEditedSampleMediaData>(
            mediaData);
        Assert.Same(editedRange, found.EditedRange);
        Assert.Equal((ulong)2, found.SampleNumber);
        Assert.Equal(new PlaybackIsoBmffSampleByteRange(20, 4, 24),
            found.Range);
        Assert.Equal(12, found.MediaDataBox.Offset);
        Assert.Equal(20, found.MediaDataBox.PayloadOffset);
    }

    [Fact]
    public void Lookup_MissingRangeRejectsBeforeFileTraversal()
    {
        var result = PlaybackIsoBmffEditedSampleMediaDataLookup.Lookup(
            [],
            null,
            out var mediaData);

        Assert.Equal(
            PlaybackIsoBmffEditedSampleMediaDataLookupResult
                .MissingEditedRange,
            result);
        Assert.Null(mediaData);
    }

    [Fact]
    public void Lookup_OutsideFileAndNonMediaDataAreDistinct()
    {
        var file = CreateFile((0x66726565, 20));

        Assert.Equal(
            PlaybackIsoBmffEditedSampleMediaDataLookupResult.SampleOutsideFile,
            PlaybackIsoBmffEditedSampleMediaDataLookup.Lookup(
                file,
                CreateEditedRange(1, new(20, 1, 21)),
                out var outside));
        Assert.Null(outside);

        Assert.Equal(
            PlaybackIsoBmffEditedSampleMediaDataLookupResult.NotFound,
            PlaybackIsoBmffEditedSampleMediaDataLookup.Lookup(
                file,
                CreateEditedRange(1, new(8, 1, 9)),
                out var absent));
        Assert.Null(absent);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65_537)]
    public void Lookup_InvalidBoxLimitRejectsBeforeInputAccess(int maximum)
    {
        var result = PlaybackIsoBmffEditedSampleMediaDataLookup.Lookup(
            [],
            null,
            out var mediaData,
            new() { MaximumBoxCount = maximum });

        Assert.Equal(
            PlaybackIsoBmffEditedSampleMediaDataLookupResult
                .InvalidConfiguration,
            result);
        Assert.Null(mediaData);
    }

    [Fact]
    public void Lookup_BoxLimitIsPreserved()
    {
        var file = CreateFile((0x66726565, 8), (0x6D646174, 12));
        var result = PlaybackIsoBmffEditedSampleMediaDataLookup.Lookup(
            file,
            CreateEditedRange(1, new(16, 1, 17)),
            out var mediaData,
            new() { MaximumBoxCount = 1 });

        Assert.Equal(
            PlaybackIsoBmffEditedSampleMediaDataLookupResult.BoxLimitExceeded,
            result);
        Assert.Null(mediaData);
    }

    private static PlaybackIsoBmffEditedSampleByteRange CreateEditedRange(
        ulong sampleNumber,
        PlaybackIsoBmffSampleByteRange range)
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
        var admission = new PlaybackIsoBmffEditedSampleAdmission(
            sampleNumber,
            new PlaybackIsoBmffCompleteSampleEditAdmission(sample, appearances));
        return new(
            admission,
            range,
            new(0, 1, 1, 0, 1, 1));
    }

    private static byte[] CreateFile(params (uint Type, int Size)[] boxes)
    {
        var file = new byte[boxes.Sum(static box => box.Size)];
        var offset = 0;
        foreach (var box in boxes)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                file.AsSpan(offset, 4),
                (uint)box.Size);
            BinaryPrimitives.WriteUInt32BigEndian(
                file.AsSpan(offset + 4, 4),
                box.Type);
            offset += box.Size;
        }

        return file;
    }
}
