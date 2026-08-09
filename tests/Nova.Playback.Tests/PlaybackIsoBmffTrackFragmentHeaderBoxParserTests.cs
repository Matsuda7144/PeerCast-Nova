using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffTrackFragmentHeaderBoxParserTests
{
    private const uint Tfhd = 0x74666864;
    private const uint Traf = 0x74726166;
    private const uint AllFieldsPresent = 0x00003B;

    [Fact]
    public void Parse_ProjectsAllConditionalFieldsAndSemanticFlags()
    {
        var box = CreateBox(
            7,
            AllFieldsPresent | 0x030000,
            baseDataOffset: 0x0102030405060708,
            sampleDescriptionIndex: 3,
            defaultSampleDuration: 1_024,
            defaultSampleSize: 4_096,
            defaultSampleFlags: 0x01010000);

        Assert.Equal(
            PlaybackIsoBmffTrackFragmentHeaderBoxParseResult.Parsed,
            PlaybackIsoBmffTrackFragmentHeaderBoxParser.Parse(
                box,
                out var fragmentHeader));
        var parsed = Assert.IsType<PlaybackIsoBmffTrackFragmentHeaderBox>(
            fragmentHeader);
        Assert.Equal(7U, parsed.TrackId);
        Assert.Equal(0x0102030405060708UL, parsed.BaseDataOffset);
        Assert.Equal(3U, parsed.SampleDescriptionIndex);
        Assert.Equal(1_024U, parsed.DefaultSampleDuration);
        Assert.Equal(4_096U, parsed.DefaultSampleSize);
        Assert.Equal(0x01010000U, parsed.DefaultSampleFlags);
        Assert.True(parsed.DurationIsEmpty);
        Assert.True(parsed.DefaultBaseIsMoof);
    }

    [Fact]
    public void Parse_AbsentConditionalFieldsRemainAbsent()
    {
        Assert.Equal(
            PlaybackIsoBmffTrackFragmentHeaderBoxParseResult.Parsed,
            PlaybackIsoBmffTrackFragmentHeaderBoxParser.Parse(
                CreateBox(1, 0),
                out var fragmentHeader));
        var parsed = Assert.IsType<PlaybackIsoBmffTrackFragmentHeaderBox>(
            fragmentHeader);
        Assert.Null(parsed.BaseDataOffset);
        Assert.Null(parsed.SampleDescriptionIndex);
        Assert.Null(parsed.DefaultSampleDuration);
        Assert.Null(parsed.DefaultSampleSize);
        Assert.Null(parsed.DefaultSampleFlags);
        Assert.False(parsed.DurationIsEmpty);
        Assert.False(parsed.DefaultBaseIsMoof);
    }

    [Fact]
    public void Parse_PresentZeroValuesRemainDistinctFromAbsentValues()
    {
        Assert.Equal(
            PlaybackIsoBmffTrackFragmentHeaderBoxParseResult.Parsed,
            PlaybackIsoBmffTrackFragmentHeaderBoxParser.Parse(
                CreateBox(
                    1,
                    AllFieldsPresent,
                    baseDataOffset: 0,
                    sampleDescriptionIndex: 0,
                    defaultSampleDuration: 0,
                    defaultSampleSize: 0,
                    defaultSampleFlags: 0),
                out var fragmentHeader));
        var parsed = Assert.IsType<PlaybackIsoBmffTrackFragmentHeaderBox>(
            fragmentHeader);
        Assert.Equal(0UL, parsed.BaseDataOffset);
        Assert.Equal(0U, parsed.SampleDescriptionIndex);
        Assert.Equal(0U, parsed.DefaultSampleDuration);
        Assert.Equal(0U, parsed.DefaultSampleSize);
        Assert.Equal(0U, parsed.DefaultSampleFlags);
    }

    [Fact]
    public void Parse_ExtendedAndExtendsToEndHeadersAreSupported()
    {
        Assert.Equal(
            PlaybackIsoBmffTrackFragmentHeaderBoxParseResult.Parsed,
            PlaybackIsoBmffTrackFragmentHeaderBoxParser.Parse(
                CreateBox(1, 0x020000, extended: true),
                out var extended));
        Assert.NotNull(extended);
        Assert.Equal(
            PlaybackIsoBmffTrackFragmentHeaderBoxParseResult.Parsed,
            PlaybackIsoBmffTrackFragmentHeaderBoxParser.Parse(
                CreateBox(2, 0x010000, extendsToEnd: true),
                out var toEnd));
        Assert.NotNull(toEnd);
    }

    [Fact]
    public void Parse_RejectsZeroTrackId()
    {
        Assert.Equal(
            PlaybackIsoBmffTrackFragmentHeaderBoxParseResult.InvalidTrackId,
            PlaybackIsoBmffTrackFragmentHeaderBoxParser.Parse(
                CreateBox(0, 0),
                out var fragmentHeader));
        Assert.Null(fragmentHeader);
    }

    [Fact]
    public void Parse_UnsupportedVersionAndUnknownFlagsAreDistinct()
    {
        var version = CreateBox(1, 0);
        version[8] = 1;
        var flags = CreateBox(1, 0x000004);

        Assert.Equal(
            PlaybackIsoBmffTrackFragmentHeaderBoxParseResult.UnsupportedVersion,
            PlaybackIsoBmffTrackFragmentHeaderBoxParser.Parse(
                version,
                out var first));
        Assert.Null(first);
        Assert.Equal(
            PlaybackIsoBmffTrackFragmentHeaderBoxParseResult.UnknownFlags,
            PlaybackIsoBmffTrackFragmentHeaderBoxParser.Parse(
                flags,
                out var second));
        Assert.Null(second);
    }

    [Fact]
    public void Parse_FlagDeclaredFieldMustBeComplete()
    {
        var box = CreateBox(1, 0x000001, baseDataOffset: 9);
        Array.Resize(ref box, box.Length - 1);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), (uint)box.Length);

        Assert.Equal(
            PlaybackIsoBmffTrackFragmentHeaderBoxParseResult.InvalidPayload,
            PlaybackIsoBmffTrackFragmentHeaderBoxParser.Parse(
                box,
                out var fragmentHeader));
        Assert.Null(fragmentHeader);
    }

    [Fact]
    public void Parse_UndeclaredTrailingFieldIsRejected()
    {
        var box = CreateBox(1, 0);
        Array.Resize(ref box, box.Length + 4);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), (uint)box.Length);

        Assert.Equal(
            PlaybackIsoBmffTrackFragmentHeaderBoxParseResult.InvalidPayload,
            PlaybackIsoBmffTrackFragmentHeaderBoxParser.Parse(
                box,
                out var fragmentHeader));
        Assert.Null(fragmentHeader);
    }

    [Fact]
    public void Parse_WrongTypeAndCompleteLengthFailuresAreDistinct()
    {
        var wrongType = CreateBox(1, 0);
        BinaryPrimitives.WriteUInt32BigEndian(wrongType.AsSpan(4, 4), Traf);
        var shortInput = CreateBox(1, 0);
        BinaryPrimitives.WriteUInt32BigEndian(
            shortInput.AsSpan(0, 4),
            (uint)shortInput.Length + 1);
        var original = CreateBox(1, 0);
        var trailing = new byte[original.Length + 1];
        original.CopyTo(trailing, 0);

        Assert.Equal(
            PlaybackIsoBmffTrackFragmentHeaderBoxParseResult.WrongBoxType,
            PlaybackIsoBmffTrackFragmentHeaderBoxParser.Parse(
                wrongType,
                out var wrong));
        Assert.Null(wrong);
        Assert.Equal(
            PlaybackIsoBmffTrackFragmentHeaderBoxParseResult.NeedMoreData,
            PlaybackIsoBmffTrackFragmentHeaderBoxParser.Parse(
                shortInput,
                out var shortResult));
        Assert.Null(shortResult);
        Assert.Equal(
            PlaybackIsoBmffTrackFragmentHeaderBoxParseResult.BoxLengthMismatch,
            PlaybackIsoBmffTrackFragmentHeaderBoxParser.Parse(
                trailing,
                out var trailingResult));
        Assert.Null(trailingResult);
    }

    private static byte[] CreateBox(
        uint trackId,
        uint flags,
        ulong? baseDataOffset = null,
        uint? sampleDescriptionIndex = null,
        uint? defaultSampleDuration = null,
        uint? defaultSampleSize = null,
        uint? defaultSampleFlags = null,
        bool extended = false,
        bool extendsToEnd = false)
    {
        var headerSize = extended ? 16 : 8;
        var payloadSize = 8 +
            ((flags & 0x000001) != 0 ? 8 : 0) +
            ((flags & 0x000002) != 0 ? 4 : 0) +
            ((flags & 0x000008) != 0 ? 4 : 0) +
            ((flags & 0x000010) != 0 ? 4 : 0) +
            ((flags & 0x000020) != 0 ? 4 : 0);
        var box = new byte[headerSize + payloadSize];
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            extendsToEnd ? 0 : extended ? 1 : (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Tfhd);
        if (extended)
        {
            BinaryPrimitives.WriteUInt64BigEndian(
                box.AsSpan(8, 8),
                (ulong)box.Length);
        }

        box[headerSize + 1] = (byte)(flags >> 16);
        box[headerSize + 2] = (byte)(flags >> 8);
        box[headerSize + 3] = (byte)flags;
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(headerSize + 4, 4),
            trackId);
        var cursor = headerSize + 8;
        if ((flags & 0x000001) != 0)
        {
            BinaryPrimitives.WriteUInt64BigEndian(
                box.AsSpan(cursor, 8),
                baseDataOffset ?? 0);
            cursor += 8;
        }

        WriteOptionalUInt32(box, flags, 0x000002, ref cursor, sampleDescriptionIndex);
        WriteOptionalUInt32(box, flags, 0x000008, ref cursor, defaultSampleDuration);
        WriteOptionalUInt32(box, flags, 0x000010, ref cursor, defaultSampleSize);
        WriteOptionalUInt32(box, flags, 0x000020, ref cursor, defaultSampleFlags);
        return box;
    }

    private static void WriteOptionalUInt32(
        byte[] box,
        uint flags,
        uint flag,
        ref int cursor,
        uint? value)
    {
        if ((flags & flag) == 0)
        {
            return;
        }

        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(cursor, 4),
            value ?? 0);
        cursor += 4;
    }
}
