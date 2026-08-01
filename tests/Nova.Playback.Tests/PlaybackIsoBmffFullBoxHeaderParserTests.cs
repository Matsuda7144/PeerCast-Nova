namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffFullBoxHeaderParserTests
{
    [Fact]
    public void Parse_ZeroVersionAndFlagsAreProjected()
    {
        var result = PlaybackIsoBmffFullBoxHeaderParser.Parse(
            [0, 0, 0, 0],
            out var header);

        Assert.Equal(PlaybackIsoBmffFullBoxHeaderParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffFullBoxHeader>(header);
        Assert.Equal((byte)0, parsed.Version);
        Assert.Equal((uint)0, parsed.Flags);
    }

    [Fact]
    public void Parse_MaximumVersionAndFlagsRemainSeparateUnsignedFields()
    {
        var result = PlaybackIsoBmffFullBoxHeaderParser.Parse(
            [0xff, 0xff, 0xff, 0xff],
            out var header);

        Assert.Equal(PlaybackIsoBmffFullBoxHeaderParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffFullBoxHeader>(header);
        Assert.Equal(byte.MaxValue, parsed.Version);
        Assert.Equal(0x00ffffffU, parsed.Flags);
    }

    [Fact]
    public void Parse_FlagsUseBigEndianTwentyFourBitOrder()
    {
        var result = PlaybackIsoBmffFullBoxHeaderParser.Parse(
            [2, 0x12, 0x34, 0x56],
            out var header);

        Assert.Equal(PlaybackIsoBmffFullBoxHeaderParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffFullBoxHeader>(header);
        Assert.Equal((byte)2, parsed.Version);
        Assert.Equal(0x00123456U, parsed.Flags);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Parse_TruncatedPrefixNeedsMoreData(int length)
    {
        var result = PlaybackIsoBmffFullBoxHeaderParser.Parse(
            new byte[length],
            out var header);

        Assert.Equal(PlaybackIsoBmffFullBoxHeaderParseResult.NeedMoreData, result);
        Assert.Null(header);
    }

    [Fact]
    public void Parse_TrailingPayloadIsNotRetainedOrInterpreted()
    {
        var result = PlaybackIsoBmffFullBoxHeaderParser.Parse(
            [1, 0, 0, 7, 0xaa, 0xbb, 0xcc],
            out var header);

        Assert.Equal(PlaybackIsoBmffFullBoxHeaderParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffFullBoxHeader>(header);
        Assert.Equal((byte)1, parsed.Version);
        Assert.Equal((uint)7, parsed.Flags);
        Assert.DoesNotContain(
            parsed.GetType().GetProperties(),
            static property =>
                property.PropertyType == typeof(byte[]) ||
                property.PropertyType == typeof(ReadOnlyMemory<byte>));
    }
}
