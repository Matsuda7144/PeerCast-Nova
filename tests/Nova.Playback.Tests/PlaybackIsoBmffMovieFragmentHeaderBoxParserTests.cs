using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffMovieFragmentHeaderBoxParserTests
{
    private const uint Mfhd = 0x6D666864;
    private const uint Moof = 0x6D6F6F66;

    [Theory]
    [InlineData(0U)]
    [InlineData(1U)]
    [InlineData(uint.MaxValue)]
    public void Parse_ProjectsExactOpaqueSequenceNumber(uint sequenceNumber)
    {
        Assert.Equal(
            PlaybackIsoBmffMovieFragmentHeaderBoxParseResult.Parsed,
            PlaybackIsoBmffMovieFragmentHeaderBoxParser.Parse(
                CreateBox(sequenceNumber),
                out var fragmentHeader));
        Assert.Equal(
            sequenceNumber,
            Assert.IsType<PlaybackIsoBmffMovieFragmentHeaderBox>(
                fragmentHeader).SequenceNumber);
    }

    [Fact]
    public void Parse_ExtendedAndExtendsToEndHeadersAreSupported()
    {
        Assert.Equal(
            PlaybackIsoBmffMovieFragmentHeaderBoxParseResult.Parsed,
            PlaybackIsoBmffMovieFragmentHeaderBoxParser.Parse(
                CreateBox(7, extended: true),
                out var extended));
        Assert.NotNull(extended);
        Assert.Equal(
            PlaybackIsoBmffMovieFragmentHeaderBoxParseResult.Parsed,
            PlaybackIsoBmffMovieFragmentHeaderBoxParser.Parse(
                CreateBox(8, extendsToEnd: true),
                out var toEnd));
        Assert.NotNull(toEnd);
    }

    [Fact]
    public void Parse_UnsupportedVersionAndNonzeroFlagsAreDistinct()
    {
        var version = CreateBox(1);
        version[8] = 1;
        var flags = CreateBox(1);
        flags[11] = 1;

        Assert.Equal(
            PlaybackIsoBmffMovieFragmentHeaderBoxParseResult.UnsupportedVersion,
            PlaybackIsoBmffMovieFragmentHeaderBoxParser.Parse(
                version,
                out var first));
        Assert.Null(first);
        Assert.Equal(
            PlaybackIsoBmffMovieFragmentHeaderBoxParseResult.NonzeroFlags,
            PlaybackIsoBmffMovieFragmentHeaderBoxParser.Parse(
                flags,
                out var second));
        Assert.Null(second);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Parse_PayloadLengthMustBeExact(int adjustment)
    {
        var original = CreateBox(1);
        var box = new byte[original.Length + adjustment];
        original.AsSpan(0, Math.Min(original.Length, box.Length)).CopyTo(box);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), (uint)box.Length);

        Assert.Equal(
            PlaybackIsoBmffMovieFragmentHeaderBoxParseResult.InvalidPayload,
            PlaybackIsoBmffMovieFragmentHeaderBoxParser.Parse(
                box,
                out var fragmentHeader));
        Assert.Null(fragmentHeader);
    }

    [Fact]
    public void Parse_WrongTypeAndCompleteLengthFailuresAreDistinct()
    {
        var wrongType = CreateBox(1);
        BinaryPrimitives.WriteUInt32BigEndian(wrongType.AsSpan(4, 4), Moof);
        var shortInput = CreateBox(1);
        BinaryPrimitives.WriteUInt32BigEndian(
            shortInput.AsSpan(0, 4),
            (uint)shortInput.Length + 1);
        var original = CreateBox(1);
        var trailing = new byte[original.Length + 1];
        original.CopyTo(trailing, 0);

        Assert.Equal(
            PlaybackIsoBmffMovieFragmentHeaderBoxParseResult.WrongBoxType,
            PlaybackIsoBmffMovieFragmentHeaderBoxParser.Parse(
                wrongType,
                out var wrong));
        Assert.Null(wrong);
        Assert.Equal(
            PlaybackIsoBmffMovieFragmentHeaderBoxParseResult.NeedMoreData,
            PlaybackIsoBmffMovieFragmentHeaderBoxParser.Parse(
                shortInput,
                out var shortResult));
        Assert.Null(shortResult);
        Assert.Equal(
            PlaybackIsoBmffMovieFragmentHeaderBoxParseResult.BoxLengthMismatch,
            PlaybackIsoBmffMovieFragmentHeaderBoxParser.Parse(
                trailing,
                out var trailingResult));
        Assert.Null(trailingResult);
    }

    private static byte[] CreateBox(
        uint sequenceNumber,
        bool extended = false,
        bool extendsToEnd = false)
    {
        var headerSize = extended ? 16 : 8;
        var box = new byte[headerSize + 8];
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            extendsToEnd ? 0 : extended ? 1 : (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Mfhd);
        if (extended)
        {
            BinaryPrimitives.WriteUInt64BigEndian(
                box.AsSpan(8, 8),
                (ulong)box.Length);
        }

        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(headerSize + 4, 4),
            sequenceNumber);
        return box;
    }
}
