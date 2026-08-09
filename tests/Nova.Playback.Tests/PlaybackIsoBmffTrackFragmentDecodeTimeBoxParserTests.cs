using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffTrackFragmentDecodeTimeBoxParserTests
{
    private const uint Tfdt = 0x74666474;
    private const uint Traf = 0x74726166;

    [Theory]
    [InlineData(0U)]
    [InlineData(uint.MaxValue)]
    public void Parse_VersionZeroProjectsUnsignedDecodeTime(uint value)
    {
        Assert.Equal(
            PlaybackIsoBmffTrackFragmentDecodeTimeBoxParseResult.Parsed,
            PlaybackIsoBmffTrackFragmentDecodeTimeBoxParser.Parse(
                CreateBox(0, value),
                out var decodeTime));
        var parsed = Assert.IsType<PlaybackIsoBmffTrackFragmentDecodeTimeBox>(
            decodeTime);
        Assert.Equal((byte)0, parsed.Version);
        Assert.Equal(value, parsed.BaseMediaDecodeTime);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(0x100000000UL)]
    [InlineData(ulong.MaxValue)]
    public void Parse_VersionOneProjectsUnsignedDecodeTime(ulong value)
    {
        Assert.Equal(
            PlaybackIsoBmffTrackFragmentDecodeTimeBoxParseResult.Parsed,
            PlaybackIsoBmffTrackFragmentDecodeTimeBoxParser.Parse(
                CreateBox(1, value),
                out var decodeTime));
        var parsed = Assert.IsType<PlaybackIsoBmffTrackFragmentDecodeTimeBox>(
            decodeTime);
        Assert.Equal((byte)1, parsed.Version);
        Assert.Equal(value, parsed.BaseMediaDecodeTime);
    }

    [Fact]
    public void Parse_ExtendedAndExtendsToEndHeadersAreSupported()
    {
        Assert.Equal(
            PlaybackIsoBmffTrackFragmentDecodeTimeBoxParseResult.Parsed,
            PlaybackIsoBmffTrackFragmentDecodeTimeBoxParser.Parse(
                CreateBox(0, 7, extended: true),
                out var extended));
        Assert.NotNull(extended);
        Assert.Equal(
            PlaybackIsoBmffTrackFragmentDecodeTimeBoxParseResult.Parsed,
            PlaybackIsoBmffTrackFragmentDecodeTimeBoxParser.Parse(
                CreateBox(1, 8, extendsToEnd: true),
                out var toEnd));
        Assert.NotNull(toEnd);
    }

    [Fact]
    public void Parse_UnsupportedVersionAndNonzeroFlagsAreDistinct()
    {
        var version = CreateBox(0, 1);
        version[8] = 2;
        var flags = CreateBox(0, 1);
        flags[11] = 1;

        Assert.Equal(
            PlaybackIsoBmffTrackFragmentDecodeTimeBoxParseResult.UnsupportedVersion,
            PlaybackIsoBmffTrackFragmentDecodeTimeBoxParser.Parse(
                version,
                out var first));
        Assert.Null(first);
        Assert.Equal(
            PlaybackIsoBmffTrackFragmentDecodeTimeBoxParseResult.NonzeroFlags,
            PlaybackIsoBmffTrackFragmentDecodeTimeBoxParser.Parse(
                flags,
                out var second));
        Assert.Null(second);
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(0, 1)]
    [InlineData(1, -1)]
    [InlineData(1, 1)]
    public void Parse_VersionControlsExactPayloadLength(byte version, int adjustment)
    {
        var original = CreateBox(version, 1);
        var box = new byte[original.Length + adjustment];
        original.AsSpan(0, Math.Min(original.Length, box.Length)).CopyTo(box);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), (uint)box.Length);

        Assert.Equal(
            PlaybackIsoBmffTrackFragmentDecodeTimeBoxParseResult.InvalidPayload,
            PlaybackIsoBmffTrackFragmentDecodeTimeBoxParser.Parse(
                box,
                out var decodeTime));
        Assert.Null(decodeTime);
    }

    [Fact]
    public void Parse_WrongTypeAndCompleteLengthFailuresAreDistinct()
    {
        var wrongType = CreateBox(0, 1);
        BinaryPrimitives.WriteUInt32BigEndian(wrongType.AsSpan(4, 4), Traf);
        var shortInput = CreateBox(0, 1);
        BinaryPrimitives.WriteUInt32BigEndian(
            shortInput.AsSpan(0, 4),
            (uint)shortInput.Length + 1);
        var original = CreateBox(0, 1);
        var trailing = new byte[original.Length + 1];
        original.CopyTo(trailing, 0);

        Assert.Equal(
            PlaybackIsoBmffTrackFragmentDecodeTimeBoxParseResult.WrongBoxType,
            PlaybackIsoBmffTrackFragmentDecodeTimeBoxParser.Parse(
                wrongType,
                out var wrong));
        Assert.Null(wrong);
        Assert.Equal(
            PlaybackIsoBmffTrackFragmentDecodeTimeBoxParseResult.NeedMoreData,
            PlaybackIsoBmffTrackFragmentDecodeTimeBoxParser.Parse(
                shortInput,
                out var shortResult));
        Assert.Null(shortResult);
        Assert.Equal(
            PlaybackIsoBmffTrackFragmentDecodeTimeBoxParseResult.BoxLengthMismatch,
            PlaybackIsoBmffTrackFragmentDecodeTimeBoxParser.Parse(
                trailing,
                out var trailingResult));
        Assert.Null(trailingResult);
    }

    private static byte[] CreateBox(
        byte version,
        ulong value,
        bool extended = false,
        bool extendsToEnd = false)
    {
        var headerSize = extended ? 16 : 8;
        var payloadSize = version == 1 ? 12 : 8;
        var box = new byte[headerSize + payloadSize];
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            extendsToEnd ? 0 : extended ? 1 : (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Tfdt);
        if (extended)
        {
            BinaryPrimitives.WriteUInt64BigEndian(
                box.AsSpan(8, 8),
                (ulong)box.Length);
        }

        box[headerSize] = version;
        if (version == 1)
        {
            BinaryPrimitives.WriteUInt64BigEndian(
                box.AsSpan(headerSize + 4, 8),
                value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                box.AsSpan(headerSize + 4, 4),
                (uint)value);
        }

        return box;
    }
}
