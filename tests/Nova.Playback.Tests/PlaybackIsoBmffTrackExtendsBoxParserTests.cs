using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffTrackExtendsBoxParserTests
{
    private const uint Trex = 0x74726578;
    private const uint Mvex = 0x6D766578;

    [Fact]
    public void Parse_ProjectsExactTrackFragmentDefaults()
    {
        var result = PlaybackIsoBmffTrackExtendsBoxParser.Parse(
            CreateBox(7, 3, 1_024, 4_096, 0x01010000),
            out var trackExtends);

        Assert.Equal(PlaybackIsoBmffTrackExtendsBoxParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffTrackExtendsBox>(trackExtends);
        Assert.Equal(7U, parsed.TrackId);
        Assert.Equal(3U, parsed.DefaultSampleDescriptionIndex);
        Assert.Equal(1_024U, parsed.DefaultSampleDuration);
        Assert.Equal(4_096U, parsed.DefaultSampleSize);
        Assert.Equal(0x01010000U, parsed.DefaultSampleFlags);
    }

    [Fact]
    public void Parse_AllowsZeroDurationAndSizeAndOpaqueFlags()
    {
        Assert.Equal(
            PlaybackIsoBmffTrackExtendsBoxParseResult.Parsed,
            PlaybackIsoBmffTrackExtendsBoxParser.Parse(
                CreateBox(1, 1, 0, 0, uint.MaxValue),
                out var trackExtends));
        var parsed = Assert.IsType<PlaybackIsoBmffTrackExtendsBox>(trackExtends);
        Assert.Equal(0U, parsed.DefaultSampleDuration);
        Assert.Equal(0U, parsed.DefaultSampleSize);
        Assert.Equal(uint.MaxValue, parsed.DefaultSampleFlags);
    }

    [Fact]
    public void Parse_ExtendedAndExtendsToEndHeadersAreSupported()
    {
        Assert.Equal(
            PlaybackIsoBmffTrackExtendsBoxParseResult.Parsed,
            PlaybackIsoBmffTrackExtendsBoxParser.Parse(
                CreateBox(1, 1, 2, 3, 4, extended: true),
                out var extended));
        Assert.NotNull(extended);
        Assert.Equal(
            PlaybackIsoBmffTrackExtendsBoxParseResult.Parsed,
            PlaybackIsoBmffTrackExtendsBoxParser.Parse(
                CreateBox(1, 1, 2, 3, 4, extendsToEnd: true),
                out var toEnd));
        Assert.NotNull(toEnd);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Parse_RejectsZeroRequiredIndexes(
        bool zeroTrackId,
        bool zeroSampleDescriptionIndex)
    {
        var result = PlaybackIsoBmffTrackExtendsBoxParser.Parse(
            CreateBox(
                zeroTrackId ? 0U : 1U,
                zeroSampleDescriptionIndex ? 0U : 1U,
                0,
                0,
                0),
            out var trackExtends);

        Assert.Equal(
            zeroTrackId
                ? PlaybackIsoBmffTrackExtendsBoxParseResult.InvalidTrackId
                : PlaybackIsoBmffTrackExtendsBoxParseResult
                    .InvalidSampleDescriptionIndex,
            result);
        Assert.Null(trackExtends);
    }

    [Fact]
    public void Parse_UnsupportedVersionAndNonzeroFlagsAreDistinct()
    {
        var version = CreateBox(1, 1, 0, 0, 0);
        version[8] = 1;
        var flags = CreateBox(1, 1, 0, 0, 0);
        flags[11] = 1;

        Assert.Equal(
            PlaybackIsoBmffTrackExtendsBoxParseResult.UnsupportedVersion,
            PlaybackIsoBmffTrackExtendsBoxParser.Parse(version, out var first));
        Assert.Null(first);
        Assert.Equal(
            PlaybackIsoBmffTrackExtendsBoxParseResult.NonzeroFlags,
            PlaybackIsoBmffTrackExtendsBoxParser.Parse(flags, out var second));
        Assert.Null(second);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Parse_PayloadLengthMustBeExact(int adjustment)
    {
        var original = CreateBox(1, 1, 0, 0, 0);
        var box = new byte[original.Length + adjustment];
        original.AsSpan(0, Math.Min(original.Length, box.Length)).CopyTo(box);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), (uint)box.Length);

        Assert.Equal(
            PlaybackIsoBmffTrackExtendsBoxParseResult.InvalidPayload,
            PlaybackIsoBmffTrackExtendsBoxParser.Parse(box, out var parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void Parse_WrongTypeAndCompleteLengthFailuresAreDistinct()
    {
        var wrongType = CreateBox(1, 1, 0, 0, 0);
        BinaryPrimitives.WriteUInt32BigEndian(wrongType.AsSpan(4, 4), Mvex);
        var shortInput = CreateBox(1, 1, 0, 0, 0);
        BinaryPrimitives.WriteUInt32BigEndian(
            shortInput.AsSpan(0, 4),
            (uint)shortInput.Length + 1);
        var original = CreateBox(1, 1, 0, 0, 0);
        var trailing = new byte[original.Length + 1];
        original.CopyTo(trailing, 0);

        Assert.Equal(
            PlaybackIsoBmffTrackExtendsBoxParseResult.WrongBoxType,
            PlaybackIsoBmffTrackExtendsBoxParser.Parse(
                wrongType,
                out var wrong));
        Assert.Null(wrong);
        Assert.Equal(
            PlaybackIsoBmffTrackExtendsBoxParseResult.NeedMoreData,
            PlaybackIsoBmffTrackExtendsBoxParser.Parse(
                shortInput,
                out var shortResult));
        Assert.Null(shortResult);
        Assert.Equal(
            PlaybackIsoBmffTrackExtendsBoxParseResult.BoxLengthMismatch,
            PlaybackIsoBmffTrackExtendsBoxParser.Parse(
                trailing,
                out var trailingResult));
        Assert.Null(trailingResult);
    }

    private static byte[] CreateBox(
        uint trackId,
        uint sampleDescriptionIndex,
        uint sampleDuration,
        uint sampleSize,
        uint sampleFlags,
        bool extended = false,
        bool extendsToEnd = false)
    {
        var headerSize = extended ? 16 : 8;
        var box = new byte[headerSize + 24];
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            extendsToEnd ? 0 : extended ? 1 : (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Trex);
        if (extended)
        {
            BinaryPrimitives.WriteUInt64BigEndian(
                box.AsSpan(8, 8),
                (ulong)box.Length);
        }

        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(headerSize + 4, 4),
            trackId);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(headerSize + 8, 4),
            sampleDescriptionIndex);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(headerSize + 12, 4),
            sampleDuration);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(headerSize + 16, 4),
            sampleSize);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(headerSize + 20, 4),
            sampleFlags);
        return box;
    }
}
