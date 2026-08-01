using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffSampleMediaDataLookupTests
{
    private const uint Free = 0x66726565;
    private const uint Mdat = 0x6D646174;

    [Fact]
    public void Lookup_FindsContainingMediaDataAcrossTopLevelBoxes()
    {
        var file = CreateFile((Free, 12), (Mdat, 20), (Mdat, 16));

        var result = PlaybackIsoBmffSampleMediaDataLookup.Lookup(
            file,
            new(42, 4, 46),
            out var mediaDataBox);

        Assert.Equal(PlaybackIsoBmffSampleMediaDataLookupResult.Found, result);
        var found = Assert.IsType<PlaybackIsoBmffBoxLocation>(mediaDataBox);
        Assert.Equal(32, found.Offset);
        Assert.Equal(40, found.PayloadOffset);
        Assert.Equal(8, found.PayloadLength);
    }

    [Fact]
    public void Lookup_ZeroSizeRangeAtPayloadEndIsFound()
    {
        var file = CreateFile((Mdat, 20), (Free, 8));

        var result = PlaybackIsoBmffSampleMediaDataLookup.Lookup(
            file,
            new(20, 0, 20),
            out var mediaDataBox);

        Assert.Equal(PlaybackIsoBmffSampleMediaDataLookupResult.Found, result);
        Assert.Equal(0, Assert.IsType<PlaybackIsoBmffBoxLocation>(mediaDataBox).Offset);
    }

    [Fact]
    public void Lookup_ExtendsToEndMediaDataIsFound()
    {
        var file = new byte[20];
        WriteHeader(file, 0, Mdat);

        var result = PlaybackIsoBmffSampleMediaDataLookup.Lookup(
            file,
            new(10, 4, 14),
            out var mediaDataBox);

        Assert.Equal(PlaybackIsoBmffSampleMediaDataLookupResult.Found, result);
        Assert.True(
            Assert.IsType<PlaybackIsoBmffBoxLocation>(mediaDataBox)
                .Header.ExtendsToEnd);
    }

    [Fact]
    public void Lookup_RangeOutsideEveryMediaDataPayloadIsNotFound()
    {
        var file = CreateFile((Free, 12), (Mdat, 20));

        var result = PlaybackIsoBmffSampleMediaDataLookup.Lookup(
            file,
            new(4, 2, 6),
            out var mediaDataBox);

        Assert.Equal(PlaybackIsoBmffSampleMediaDataLookupResult.NotFound, result);
        Assert.Null(mediaDataBox);
    }

    [Fact]
    public void Lookup_InvalidSampleRangeIsRejectedBeforeTraversal()
    {
        var result = PlaybackIsoBmffSampleMediaDataLookup.Lookup(
            new byte[1],
            new(0, 1, 2),
            out var mediaDataBox);

        Assert.Equal(
            PlaybackIsoBmffSampleMediaDataLookupResult.InvalidSampleRange,
            result);
        Assert.Null(mediaDataBox);
    }

    [Fact]
    public void Lookup_SampleRangeBeyondFileIsRejectedBeforeTraversal()
    {
        var result = PlaybackIsoBmffSampleMediaDataLookup.Lookup(
            new byte[8],
            new(8, 1, 9),
            out var mediaDataBox);

        Assert.Equal(
            PlaybackIsoBmffSampleMediaDataLookupResult.SampleOutsideFile,
            result);
        Assert.Null(mediaDataBox);
    }

    [Fact]
    public void Lookup_TruncatedTopLevelBoxIsRejected()
    {
        var file = new byte[12];
        WriteHeader(file, 20, Mdat);

        var result = PlaybackIsoBmffSampleMediaDataLookup.Lookup(
            file,
            new(8, 1, 9),
            out var mediaDataBox);

        Assert.Equal(
            PlaybackIsoBmffSampleMediaDataLookupResult.InvalidBoxSequence,
            result);
        Assert.Null(mediaDataBox);
    }

    [Fact]
    public void Lookup_BoxCountIsBoundedBeforeInspectingAnotherBox()
    {
        var file = CreateFile((Free, 8), (Mdat, 12));
        var options = new PlaybackIsoBmffSampleMediaDataLookupOptions
        {
            MaximumBoxCount = 1,
        };

        var result = PlaybackIsoBmffSampleMediaDataLookup.Lookup(
            file,
            new(16, 1, 17),
            out var mediaDataBox,
            options);

        Assert.Equal(
            PlaybackIsoBmffSampleMediaDataLookupResult.BoxLimitExceeded,
            result);
        Assert.Null(mediaDataBox);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(
        PlaybackIsoBmffSampleMediaDataLookupOptions.MaximumBoxCountHardLimit + 1)]
    public void Lookup_InvalidMaximumBoxCountIsRejected(int maximumBoxCount)
    {
        var options = new PlaybackIsoBmffSampleMediaDataLookupOptions
        {
            MaximumBoxCount = maximumBoxCount,
        };

        var result = PlaybackIsoBmffSampleMediaDataLookup.Lookup(
            [],
            new(0, 0, 0),
            out var mediaDataBox,
            options);

        Assert.Equal(
            PlaybackIsoBmffSampleMediaDataLookupResult.InvalidConfiguration,
            result);
        Assert.Null(mediaDataBox);
    }

    [Fact]
    public void Lookup_EmptyFileHasNoMediaData()
    {
        var result = PlaybackIsoBmffSampleMediaDataLookup.Lookup(
            [],
            new(0, 0, 0),
            out var mediaDataBox);

        Assert.Equal(PlaybackIsoBmffSampleMediaDataLookupResult.NotFound, result);
        Assert.Null(mediaDataBox);
    }

    private static byte[] CreateFile(params (uint Type, int Size)[] boxes)
    {
        var file = new byte[boxes.Sum(static box => box.Size)];
        var offset = 0;
        foreach (var box in boxes)
        {
            WriteHeader(file.AsSpan(offset), (uint)box.Size, box.Type);
            offset += box.Size;
        }

        return file;
    }

    private static void WriteHeader(Span<byte> destination, uint size, uint type)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], size);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(4, 4), type);
    }
}
