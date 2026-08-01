using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffSampleMediaDataBoundsValidatorTests
{
    private const uint Mdat = 0x6D646174;
    private const uint Free = 0x66726565;

    [Fact]
    public void Validate_RangeInsideSelectedMediaDataPayloadIsContained()
    {
        var result = Validate(
            new(110, 10, 120),
            ReadBox(Mdat, 28, prefixLength: 4),
            containerOffset: 98);

        Assert.Equal(
            PlaybackIsoBmffSampleMediaDataBoundsValidationResult.Contained,
            result);
    }

    [Fact]
    public void Validate_RangeExactlyCoveringPayloadIsContained()
    {
        var result = Validate(
            new(108, 20, 128),
            ReadBox(Mdat, 28),
            containerOffset: 100);

        Assert.Equal(
            PlaybackIsoBmffSampleMediaDataBoundsValidationResult.Contained,
            result);
    }

    [Fact]
    public void Validate_RangeStartingInHeaderIsOutsideMediaData()
    {
        var result = Validate(
            new(107, 2, 109),
            ReadBox(Mdat, 28),
            containerOffset: 100);

        Assert.Equal(
            PlaybackIsoBmffSampleMediaDataBoundsValidationResult
                .OutsideMediaData,
            result);
    }

    [Fact]
    public void Validate_RangeEndingAfterPayloadIsOutsideMediaData()
    {
        var result = Validate(
            new(120, 9, 129),
            ReadBox(Mdat, 28),
            containerOffset: 100);

        Assert.Equal(
            PlaybackIsoBmffSampleMediaDataBoundsValidationResult
                .OutsideMediaData,
            result);
    }

    [Fact]
    public void Validate_ZeroSizeRangeAtPayloadEndIsContained()
    {
        var result = Validate(
            new(128, 0, 128),
            ReadBox(Mdat, 28),
            containerOffset: 100);

        Assert.Equal(
            PlaybackIsoBmffSampleMediaDataBoundsValidationResult.Contained,
            result);
    }

    [Fact]
    public void Validate_NonMediaDataBoxIsInvalid()
    {
        var result = Validate(
            new(108, 1, 109),
            ReadBox(Free, 28),
            containerOffset: 100);

        Assert.Equal(
            PlaybackIsoBmffSampleMediaDataBoundsValidationResult
                .InvalidMediaDataBox,
            result);
    }

    [Fact]
    public void Validate_MissingMediaDataBoxIsInvalid()
    {
        var result = Validate(
            new(108, 1, 109),
            null,
            containerOffset: 100);

        Assert.Equal(
            PlaybackIsoBmffSampleMediaDataBoundsValidationResult
                .InvalidMediaDataBox,
            result);
    }

    [Fact]
    public void Validate_InconsistentSampleRangeIsInvalid()
    {
        var result = Validate(
            new(108, 1, 110),
            ReadBox(Mdat, 28),
            containerOffset: 100);

        Assert.Equal(
            PlaybackIsoBmffSampleMediaDataBoundsValidationResult
                .InvalidSampleRange,
            result);
    }

    [Fact]
    public void Validate_AbsolutePayloadOffsetOverflowIsRejected()
    {
        var result = Validate(
            new(1, 1, 2),
            ReadBox(Mdat, 28),
            containerOffset: ulong.MaxValue - 7);

        Assert.Equal(
            PlaybackIsoBmffSampleMediaDataBoundsValidationResult.OffsetOverflow,
            result);
    }

    private static PlaybackIsoBmffSampleMediaDataBoundsValidationResult Validate(
        PlaybackIsoBmffSampleByteRange range,
        PlaybackIsoBmffBoxLocation? mediaDataBox,
        ulong containerOffset) =>
        PlaybackIsoBmffSampleMediaDataBoundsValidator.Validate(
            range,
            mediaDataBox,
            containerOffset);

    private static PlaybackIsoBmffBoxLocation ReadBox(
        uint type,
        int boxLength,
        int prefixLength = 0)
    {
        var container = new byte[prefixLength + boxLength];
        BinaryPrimitives.WriteUInt32BigEndian(
            container.AsSpan(prefixLength, 4),
            (uint)boxLength);
        BinaryPrimitives.WriteUInt32BigEndian(
            container.AsSpan(prefixLength + 4, 4),
            type);

        Assert.Equal(
            PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable,
            PlaybackIsoBmffBoxSequenceReader.ReadNext(
                container,
                prefixLength,
                out var box));
        return Assert.IsType<PlaybackIsoBmffBoxLocation>(box);
    }
}
