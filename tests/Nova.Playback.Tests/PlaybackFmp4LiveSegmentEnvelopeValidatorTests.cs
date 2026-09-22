using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFmp4LiveSegmentEnvelopeValidatorTests
{
    private const uint Ftyp = 0x66747970;
    private const uint Moov = 0x6d6f6f76;
    private const uint Moof = 0x6d6f6f66;
    private const uint Mdat = 0x6d646174;
    private const uint Unknown = 0x756e6b6e;

    [Fact]
    public void Validate_ExactInitializationEnvelopeIsAccepted()
    {
        var segment = new byte[20];
        WriteHeader(segment.AsSpan(0, 8), 8, Ftyp);
        WriteHeader(segment.AsSpan(8, 8), 12, Moov);

        var result = PlaybackFmp4LiveSegmentEnvelopeValidator.Validate(
            segment,
            segment.Length,
            out var envelope);

        Assert.Equal(PlaybackFmp4LiveSegmentEnvelopeValidationResult.Accepted, result);
        var accepted = Assert.IsType<PlaybackFmp4LiveSegmentEnvelope>(envelope);
        Assert.Equal(PlaybackFmp4LiveSegmentKind.Initialization, accepted.Kind);
        Assert.Equal(segment.Length, accepted.SegmentLength);
        Assert.Equal(8, accepted.FirstBoxLength);
        Assert.Equal(12, accepted.SecondBoxLength);
    }

    [Fact]
    public void Validate_ExactMediaEnvelopeIsAccepted()
    {
        var segment = new byte[24];
        WriteHeader(segment.AsSpan(0, 8), 12, Moof);
        WriteHeader(segment.AsSpan(12, 8), 12, Mdat);

        var result = PlaybackFmp4LiveSegmentEnvelopeValidator.Validate(
            segment,
            segment.Length,
            out var envelope);

        Assert.Equal(PlaybackFmp4LiveSegmentEnvelopeValidationResult.Accepted, result);
        var accepted = Assert.IsType<PlaybackFmp4LiveSegmentEnvelope>(envelope);
        Assert.Equal(PlaybackFmp4LiveSegmentKind.Media, accepted.Kind);
        Assert.Equal(segment.Length, accepted.SegmentLength);
        Assert.Equal(12, accepted.FirstBoxLength);
        Assert.Equal(12, accepted.SecondBoxLength);
    }

    [Fact]
    public void Validate_EmptySegmentIsRejectedWithoutEnvelope()
    {
        var result = PlaybackFmp4LiveSegmentEnvelopeValidator.Validate(
            [],
            1,
            out var envelope);

        Assert.Equal(PlaybackFmp4LiveSegmentEnvelopeValidationResult.EmptySegment, result);
        Assert.Null(envelope);
    }

    [Fact]
    public void Validate_OverBoundSegmentIsRejectedWithoutEnvelope()
    {
        var segment = new byte[16];

        var result = PlaybackFmp4LiveSegmentEnvelopeValidator.Validate(
            segment,
            segment.Length - 1,
            out var envelope);

        Assert.Equal(PlaybackFmp4LiveSegmentEnvelopeValidationResult.SegmentTooLarge, result);
        Assert.Null(envelope);
    }

    [Fact]
    public void Validate_TruncatedFirstBoxIsRejectedWithoutEnvelope()
    {
        var segment = new byte[8];
        WriteHeader(segment, 12, Ftyp);

        var result = PlaybackFmp4LiveSegmentEnvelopeValidator.Validate(
            segment,
            segment.Length,
            out var envelope);

        Assert.Equal(PlaybackFmp4LiveSegmentEnvelopeValidationResult.InvalidBox, result);
        Assert.Null(envelope);
    }

    [Fact]
    public void Validate_FirstExtendsToEndBoxIsRejectedWithoutEnvelope()
    {
        var segment = new byte[8];
        WriteHeader(segment, 0, Ftyp);

        var result = PlaybackFmp4LiveSegmentEnvelopeValidator.Validate(
            segment,
            segment.Length,
            out var envelope);

        Assert.Equal(PlaybackFmp4LiveSegmentEnvelopeValidationResult.ExtendsToEnd, result);
        Assert.Null(envelope);
    }

    [Fact]
    public void Validate_SecondExtendsToEndBoxIsRejectedWithoutEnvelope()
    {
        var segment = new byte[16];
        WriteHeader(segment.AsSpan(0, 8), 8, Ftyp);
        WriteHeader(segment.AsSpan(8, 8), 0, Moov);

        var result = PlaybackFmp4LiveSegmentEnvelopeValidator.Validate(
            segment,
            segment.Length,
            out var envelope);

        Assert.Equal(PlaybackFmp4LiveSegmentEnvelopeValidationResult.ExtendsToEnd, result);
        Assert.Null(envelope);
    }

    [Fact]
    public void Validate_ReorderedInitializationBoxesAreRejectedWithoutEnvelope()
    {
        var segment = new byte[16];
        WriteHeader(segment.AsSpan(0, 8), 8, Moov);
        WriteHeader(segment.AsSpan(8, 8), 8, Ftyp);

        var result = PlaybackFmp4LiveSegmentEnvelopeValidator.Validate(
            segment,
            segment.Length,
            out var envelope);

        Assert.Equal(PlaybackFmp4LiveSegmentEnvelopeValidationResult.InvalidShape, result);
        Assert.Null(envelope);
    }

    [Fact]
    public void Validate_WrongInitializationPairIsRejectedWithoutEnvelope()
    {
        var segment = new byte[16];
        WriteHeader(segment.AsSpan(0, 8), 8, Ftyp);
        WriteHeader(segment.AsSpan(8, 8), 8, Mdat);

        var result = PlaybackFmp4LiveSegmentEnvelopeValidator.Validate(
            segment,
            segment.Length,
            out var envelope);

        Assert.Equal(PlaybackFmp4LiveSegmentEnvelopeValidationResult.InvalidShape, result);
        Assert.Null(envelope);
    }

    [Fact]
    public void Validate_UnknownFirstBoxTypeIsRejectedWithoutEnvelope()
    {
        var segment = new byte[16];
        WriteHeader(segment.AsSpan(0, 8), 8, Unknown);
        WriteHeader(segment.AsSpan(8, 8), 8, Moov);

        var result = PlaybackFmp4LiveSegmentEnvelopeValidator.Validate(
            segment,
            segment.Length,
            out var envelope);

        Assert.Equal(PlaybackFmp4LiveSegmentEnvelopeValidationResult.InvalidShape, result);
        Assert.Null(envelope);
    }

    [Fact]
    public void Validate_ExtraThirdBoxIsRejectedWithoutEnvelope()
    {
        var segment = new byte[24];
        WriteHeader(segment.AsSpan(0, 8), 8, Ftyp);
        WriteHeader(segment.AsSpan(8, 8), 8, Moov);
        WriteHeader(segment.AsSpan(16, 8), 8, Mdat);

        var result = PlaybackFmp4LiveSegmentEnvelopeValidator.Validate(
            segment,
            segment.Length,
            out var envelope);

        Assert.Equal(PlaybackFmp4LiveSegmentEnvelopeValidationResult.InvalidShape, result);
        Assert.Null(envelope);
    }

    private static void WriteHeader(Span<byte> destination, uint size, uint type)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], size);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..8], type);
    }
}
