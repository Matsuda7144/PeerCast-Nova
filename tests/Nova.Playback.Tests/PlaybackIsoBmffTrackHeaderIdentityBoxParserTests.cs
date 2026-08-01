using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffTrackHeaderIdentityBoxParserTests
{
    private const uint Tkhd = 0x746b6864;
    private const uint Trak = 0x7472616b;

    [Fact]
    public void Parse_Version0ProjectsIdentityFlagsAndFixedPointDimensions()
    {
        var box = CreateBox(
            version: 0,
            flags: 0x000007,
            creationTime: 1,
            modificationTime: uint.MaxValue,
            trackId: 7,
            duration: 9_000,
            widthFixedPoint: 1920U << 16,
            heightFixedPoint: 1080U << 16);

        var result = PlaybackIsoBmffTrackHeaderIdentityBoxParser.Parse(
            box,
            out var identity);

        Assert.Equal(PlaybackIsoBmffTrackHeaderIdentityBoxParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffTrackHeaderIdentityBox>(identity);
        Assert.Equal((byte)0, parsed.Version);
        Assert.Equal((uint)7, parsed.TrackId);
        Assert.Equal((ulong)9_000, parsed.DurationUnits);
        Assert.True(parsed.IsEnabled);
        Assert.True(parsed.IsInMovie);
        Assert.True(parsed.IsInPreview);
        Assert.False(parsed.SizeIsAspectRatio);
        Assert.Equal(1920U << 16, parsed.WidthFixedPoint);
        Assert.Equal(1080U << 16, parsed.HeightFixedPoint);
    }

    [Fact]
    public void Parse_Version1ProjectsValuesBeyondThirtyTwoBits()
    {
        var box = CreateBox(
            version: 1,
            flags: 0x00000f,
            creationTime: 0x00000001_00000002,
            modificationTime: 0x00000003_00000004,
            trackId: uint.MaxValue,
            duration: 0x00000005_00000006,
            widthFixedPoint: 1,
            heightFixedPoint: uint.MaxValue,
            extended: true);

        var result = PlaybackIsoBmffTrackHeaderIdentityBoxParser.Parse(
            box,
            out var identity);

        Assert.Equal(PlaybackIsoBmffTrackHeaderIdentityBoxParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffTrackHeaderIdentityBox>(identity);
        Assert.Equal((byte)1, parsed.Version);
        Assert.Equal(0x00000001_00000002UL, parsed.CreationTime);
        Assert.Equal(0x00000003_00000004UL, parsed.ModificationTime);
        Assert.Equal(uint.MaxValue, parsed.TrackId);
        Assert.Equal(0x00000005_00000006UL, parsed.DurationUnits);
        Assert.True(parsed.SizeIsAspectRatio);
        Assert.Equal((uint)1, parsed.WidthFixedPoint);
        Assert.Equal(uint.MaxValue, parsed.HeightFixedPoint);
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)1)]
    public void Parse_AllOnesDurationIsExplicitlyUnknown(byte version)
    {
        var box = CreateBox(
            version,
            0,
            0,
            0,
            1,
            version == 0 ? uint.MaxValue : ulong.MaxValue,
            0,
            0,
            extendsToEnd: true);

        var result = PlaybackIsoBmffTrackHeaderIdentityBoxParser.Parse(
            box,
            out var identity);

        Assert.Equal(PlaybackIsoBmffTrackHeaderIdentityBoxParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffTrackHeaderIdentityBox>(identity);
        Assert.Null(parsed.DurationUnits);
        Assert.False(parsed.HasKnownDuration);
    }

    [Fact]
    public void Parse_ZeroTrackIdIsRejected()
    {
        var box = CreateBox(0, 0, 0, 0, 0, 10, 0, 0);

        var result = PlaybackIsoBmffTrackHeaderIdentityBoxParser.Parse(
            box,
            out var identity);

        Assert.Equal(
            PlaybackIsoBmffTrackHeaderIdentityBoxParseResult.InvalidTrackId,
            result);
        Assert.Null(identity);
    }

    [Theory]
    [InlineData(0x000010)]
    [InlineData(0x800000)]
    public void Parse_UnknownFlagBitsAreRejected(uint flags)
    {
        var box = CreateBox(0, flags, 0, 0, 1, 10, 0, 0);

        var result = PlaybackIsoBmffTrackHeaderIdentityBoxParser.Parse(
            box,
            out var identity);

        Assert.Equal(
            PlaybackIsoBmffTrackHeaderIdentityBoxParseResult.UnsupportedFlags,
            result);
        Assert.Null(identity);
    }

    [Theory]
    [InlineData((byte)0, 16)]
    [InlineData((byte)0, 24)]
    [InlineData((byte)0, 38)]
    [InlineData((byte)1, 24)]
    [InlineData((byte)1, 36)]
    [InlineData((byte)1, 50)]
    public void Parse_NonzeroReservedFieldIsRejected(byte version, int payloadOffset)
    {
        var box = CreateBox(version, 0, 0, 0, 1, 10, 0, 0);
        box[8 + payloadOffset] = 1;

        var result = PlaybackIsoBmffTrackHeaderIdentityBoxParser.Parse(
            box,
            out var identity);

        Assert.Equal(
            PlaybackIsoBmffTrackHeaderIdentityBoxParseResult.InvalidPayload,
            result);
        Assert.Null(identity);
    }

    [Fact]
    public void Parse_UnsupportedVersionIsRejected()
    {
        var box = CreateBox(0, 0, 0, 0, 1, 10, 0, 0);
        box[8] = 2;

        var result = PlaybackIsoBmffTrackHeaderIdentityBoxParser.Parse(
            box,
            out var identity);

        Assert.Equal(
            PlaybackIsoBmffTrackHeaderIdentityBoxParseResult.UnsupportedVersion,
            result);
        Assert.Null(identity);
    }

    [Fact]
    public void Parse_WrongTypeAndCompleteLengthFailuresAreDistinct()
    {
        var wrongType = CreateBox(0, 0, 0, 0, 1, 10, 0, 0);
        BinaryPrimitives.WriteUInt32BigEndian(wrongType.AsSpan(4, 4), Trak);
        var shortInput = CreateBox(0, 0, 0, 0, 1, 10, 0, 0);
        BinaryPrimitives.WriteUInt32BigEndian(
            shortInput.AsSpan(0, 4),
            (uint)shortInput.Length + 1);
        var original = CreateBox(0, 0, 0, 0, 1, 10, 0, 0);
        var trailingInput = new byte[original.Length + 1];
        original.CopyTo(trailingInput, 0);

        Assert.Equal(
            PlaybackIsoBmffTrackHeaderIdentityBoxParseResult.WrongBoxType,
            PlaybackIsoBmffTrackHeaderIdentityBoxParser.Parse(
                wrongType,
                out var wrongIdentity));
        Assert.Null(wrongIdentity);
        Assert.Equal(
            PlaybackIsoBmffTrackHeaderIdentityBoxParseResult.NeedMoreData,
            PlaybackIsoBmffTrackHeaderIdentityBoxParser.Parse(
                shortInput,
                out var shortIdentity));
        Assert.Null(shortIdentity);
        Assert.Equal(
            PlaybackIsoBmffTrackHeaderIdentityBoxParseResult.BoxLengthMismatch,
            PlaybackIsoBmffTrackHeaderIdentityBoxParser.Parse(
                trailingInput,
                out var trailingIdentity));
        Assert.Null(trailingIdentity);
    }

    [Theory]
    [InlineData((byte)0, 83)]
    [InlineData((byte)0, 85)]
    [InlineData((byte)1, 95)]
    [InlineData((byte)1, 97)]
    public void Parse_NonexactVersionedPayloadLengthIsRejected(
        byte version,
        int payloadLength)
    {
        var box = new byte[8 + payloadLength];
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Tkhd);
        box[8] = version;

        var result = PlaybackIsoBmffTrackHeaderIdentityBoxParser.Parse(
            box,
            out var identity);

        Assert.Equal(
            PlaybackIsoBmffTrackHeaderIdentityBoxParseResult.InvalidPayload,
            result);
        Assert.Null(identity);
    }

    private static byte[] CreateBox(
        byte version,
        uint flags,
        ulong creationTime,
        ulong modificationTime,
        uint trackId,
        ulong duration,
        uint widthFixedPoint,
        uint heightFixedPoint,
        bool extended = false,
        bool extendsToEnd = false)
    {
        var headerSize = extended ? 16 : 8;
        var payloadSize = version == 1 ? 96 : 84;
        var box = new byte[headerSize + payloadSize];
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            extendsToEnd ? 0 : extended ? 1 : (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Tkhd);
        if (extended)
        {
            BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(8, 8), (ulong)box.Length);
        }

        var payload = box.AsSpan(headerSize);
        payload[0] = version;
        payload[1] = (byte)(flags >> 16);
        payload[2] = (byte)(flags >> 8);
        payload[3] = (byte)flags;
        int dimensionsOffset;
        if (version == 0)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                payload.Slice(4, 4),
                checked((uint)creationTime));
            BinaryPrimitives.WriteUInt32BigEndian(
                payload.Slice(8, 4),
                checked((uint)modificationTime));
            BinaryPrimitives.WriteUInt32BigEndian(payload.Slice(12, 4), trackId);
            BinaryPrimitives.WriteUInt32BigEndian(
                payload.Slice(20, 4),
                checked((uint)duration));
            dimensionsOffset = 76;
        }
        else
        {
            BinaryPrimitives.WriteUInt64BigEndian(payload.Slice(4, 8), creationTime);
            BinaryPrimitives.WriteUInt64BigEndian(payload.Slice(12, 8), modificationTime);
            BinaryPrimitives.WriteUInt32BigEndian(payload.Slice(20, 4), trackId);
            BinaryPrimitives.WriteUInt64BigEndian(payload.Slice(28, 8), duration);
            dimensionsOffset = 88;
        }

        BinaryPrimitives.WriteUInt32BigEndian(
            payload.Slice(dimensionsOffset, 4),
            widthFixedPoint);
        BinaryPrimitives.WriteUInt32BigEndian(
            payload.Slice(dimensionsOffset + 4, 4),
            heightFixedPoint);
        return box;
    }
}
