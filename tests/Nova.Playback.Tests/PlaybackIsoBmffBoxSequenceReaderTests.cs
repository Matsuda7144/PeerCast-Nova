using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffBoxSequenceReaderTests
{
    private const uint Ftyp = 0x66747970;
    private const uint Moov = 0x6d6f6f76;
    private const uint Uuid = 0x75756964;

    [Fact]
    public void ReadNext_TwoBoxesAdvanceByDeclaredRangesWithoutReadingPayload()
    {
        var container = new byte[28];
        WriteHeader(container.AsSpan(0, 8), 12, Ftyp);
        container.AsSpan(8, 4).Fill(0xaa);
        WriteHeader(container.AsSpan(12, 8), 16, Moov);
        container.AsSpan(20, 8).Fill(0xbb);

        var firstResult = PlaybackIsoBmffBoxSequenceReader.ReadNext(
            container,
            0,
            out var first);
        var secondResult = PlaybackIsoBmffBoxSequenceReader.ReadNext(
            container,
            Assert.IsType<PlaybackIsoBmffBoxLocation>(first).NextOffset,
            out var second);
        var endResult = PlaybackIsoBmffBoxSequenceReader.ReadNext(
            container,
            Assert.IsType<PlaybackIsoBmffBoxLocation>(second).NextOffset,
            out var end);

        Assert.Equal(PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable, firstResult);
        Assert.Equal(0, first.Offset);
        Assert.Equal(12, first.BoxLength);
        Assert.Equal(8, first.PayloadOffset);
        Assert.Equal(4, first.PayloadLength);
        Assert.Equal(Ftyp, first.Header.Type);
        Assert.Equal(PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable, secondResult);
        Assert.Equal(12, second.Offset);
        Assert.Equal(16, second.BoxLength);
        Assert.Equal(8, second.PayloadLength);
        Assert.Equal(Moov, second.Header.Type);
        Assert.Equal(PlaybackIsoBmffBoxSequenceReadResult.EndOfContainer, endResult);
        Assert.Null(end);
    }

    [Fact]
    public void ReadNext_ZeroSizeConsumesContainingRemainder()
    {
        var container = new byte[20];
        WriteHeader(container.AsSpan(4, 8), 0, Moov);

        var result = PlaybackIsoBmffBoxSequenceReader.ReadNext(
            container,
            4,
            out var box);

        Assert.Equal(PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable, result);
        var parsed = Assert.IsType<PlaybackIsoBmffBoxLocation>(box);
        Assert.True(parsed.Header.ExtendsToEnd);
        Assert.Equal(16, parsed.BoxLength);
        Assert.Equal(8, parsed.PayloadLength);
        Assert.Equal(container.Length, parsed.NextOffset);
    }

    [Fact]
    public void ReadNext_UuidOffsetsIncludeUserTypeHeader()
    {
        var container = new byte[28];
        WriteHeader(container.AsSpan(0, 8), 28, Uuid);
        BinaryPrimitives.WriteUInt64BigEndian(
            container.AsSpan(8, 8),
            0x0102030405060708);
        BinaryPrimitives.WriteUInt64BigEndian(
            container.AsSpan(16, 8),
            0x1112131415161718);

        var result = PlaybackIsoBmffBoxSequenceReader.ReadNext(
            container,
            0,
            out var box);

        Assert.Equal(PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable, result);
        var parsed = Assert.IsType<PlaybackIsoBmffBoxLocation>(box);
        Assert.Equal(24, parsed.Header.HeaderSize);
        Assert.Equal(24, parsed.PayloadOffset);
        Assert.Equal(4, parsed.PayloadLength);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    public void ReadNext_OffsetOutsideContainerIsRejected(int offset)
    {
        var result = PlaybackIsoBmffBoxSequenceReader.ReadNext(
            new byte[8],
            offset,
            out var box);

        Assert.Equal(PlaybackIsoBmffBoxSequenceReadResult.InvalidOffset, result);
        Assert.Null(box);
    }

    [Fact]
    public void ReadNext_EmptyContainerIsAtEnd()
    {
        var result = PlaybackIsoBmffBoxSequenceReader.ReadNext(
            [],
            0,
            out var box);

        Assert.Equal(PlaybackIsoBmffBoxSequenceReadResult.EndOfContainer, result);
        Assert.Null(box);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(15)]
    public void ReadNext_IncompleteStructuralHeaderIsTruncated(int length)
    {
        var container = new byte[length];
        if (length >= 8)
        {
            WriteHeader(container.AsSpan(0, 8), 1, Ftyp);
        }

        var result = PlaybackIsoBmffBoxSequenceReader.ReadNext(
            container,
            0,
            out var box);

        Assert.Equal(PlaybackIsoBmffBoxSequenceReadResult.TruncatedBox, result);
        Assert.Null(box);
    }

    [Fact]
    public void ReadNext_UndersizedDeclarationIsInvalidHeader()
    {
        var container = new byte[8];
        WriteHeader(container, 7, Ftyp);

        var result = PlaybackIsoBmffBoxSequenceReader.ReadNext(
            container,
            0,
            out var box);

        Assert.Equal(PlaybackIsoBmffBoxSequenceReadResult.InvalidHeader, result);
        Assert.Null(box);
    }

    [Fact]
    public void ReadNext_DeclaredBoxBeyondContainerIsTruncated()
    {
        var container = new byte[20];
        WriteHeader(container.AsSpan(0, 8), 21, Moov);

        var result = PlaybackIsoBmffBoxSequenceReader.ReadNext(
            container,
            0,
            out var box);

        Assert.Equal(PlaybackIsoBmffBoxSequenceReadResult.TruncatedBox, result);
        Assert.Null(box);
    }

    [Fact]
    public void ReadNext_ExtendedSizeBeyondManagedContainerIsTruncated()
    {
        var container = new byte[16];
        WriteHeader(container.AsSpan(0, 8), 1, Moov);
        BinaryPrimitives.WriteUInt64BigEndian(
            container.AsSpan(8, 8),
            (ulong)int.MaxValue + 1);

        var result = PlaybackIsoBmffBoxSequenceReader.ReadNext(
            container,
            0,
            out var box);

        Assert.Equal(PlaybackIsoBmffBoxSequenceReadResult.TruncatedBox, result);
        Assert.Null(box);
    }

    private static void WriteHeader(Span<byte> destination, uint size, uint type)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], size);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..8], type);
    }
}
