namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffDescriptorHeaderParserTests
{
    [Theory]
    [InlineData(new byte[] { 0x03, 0x00 }, 2, 0)]
    [InlineData(new byte[] { 0x04, 0x01, 0xAA }, 2, 1)]
    [InlineData(new byte[] { 0x05, 0x81, 0x00 }, 3, 128)]
    public void Parse_ProjectsTagHeaderAndPayloadLengths(
        byte[] prefix,
        int expectedHeaderLength,
        int expectedPayloadLength)
    {
        var descriptor = new byte[expectedHeaderLength + expectedPayloadLength];
        prefix.CopyTo(descriptor, 0);

        var result = PlaybackIsoBmffDescriptorHeaderParser.Parse(
            descriptor,
            out var header);

        Assert.Equal(PlaybackIsoBmffDescriptorHeaderParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffDescriptorHeader>(header);
        Assert.Equal(prefix[0], parsed.Tag);
        Assert.Equal(expectedHeaderLength, parsed.HeaderLength);
        Assert.Equal(expectedPayloadLength, parsed.PayloadLength);
        Assert.Equal(descriptor.Length, parsed.TotalLength);
    }

    [Fact]
    public void Parse_FourByteNonMinimalLengthEncodingIsAccepted()
    {
        var descriptor = new byte[] { 0x06, 0x80, 0x80, 0x80, 0x01, 0x02 };

        var result = PlaybackIsoBmffDescriptorHeaderParser.Parse(
            descriptor,
            out var header);

        Assert.Equal(PlaybackIsoBmffDescriptorHeaderParseResult.Parsed, result);
        Assert.Equal(5, header!.HeaderLength);
        Assert.Equal(1, header.PayloadLength);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x03 })]
    [InlineData(new byte[] { 0x03, 0x81 })]
    [InlineData(new byte[] { 0x03, 0x02, 0xAA })]
    public void Parse_TruncatedHeaderOrPayloadNeedsMoreData(byte[] descriptor)
    {
        var result = PlaybackIsoBmffDescriptorHeaderParser.Parse(
            descriptor,
            out var header);

        Assert.Equal(
            PlaybackIsoBmffDescriptorHeaderParseResult.NeedMoreData,
            result);
        Assert.Null(header);
    }

    [Fact]
    public void Parse_FourContinuationLengthBytesAreRejected()
    {
        var result = PlaybackIsoBmffDescriptorHeaderParser.Parse(
            [0x03, 0x80, 0x80, 0x80, 0x80],
            out var header);

        Assert.Equal(
            PlaybackIsoBmffDescriptorHeaderParseResult.InvalidLengthEncoding,
            result);
        Assert.Null(header);
    }

    [Fact]
    public void Parse_TrailingDataCanBeRejectedOrLeftToContainerReader()
    {
        byte[] descriptor = [0x03, 0x00, 0xFF];

        Assert.Equal(
            PlaybackIsoBmffDescriptorHeaderParseResult.TrailingData,
            PlaybackIsoBmffDescriptorHeaderParser.Parse(
                descriptor,
                out var exact));
        Assert.Null(exact);
        Assert.Equal(
            PlaybackIsoBmffDescriptorHeaderParseResult.Parsed,
            PlaybackIsoBmffDescriptorHeaderParser.Parse(
                descriptor,
                out var prefix,
                requireExactLength: false));
        Assert.Equal(2, prefix!.TotalLength);
    }

    [Fact]
    public void Parse_PayloadLimitIsCheckedBeforePayloadAvailability()
    {
        var result = PlaybackIsoBmffDescriptorHeaderParser.Parse(
            [0x05, 0x0B],
            out var header,
            new() { MaximumPayloadBytes = 10 });

        Assert.Equal(
            PlaybackIsoBmffDescriptorHeaderParseResult.PayloadLimitExceeded,
            result);
        Assert.Null(header);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1_048_577)]
    public void Parse_InvalidLimitRejectsBeforeInputAccess(int maximum)
    {
        var result = PlaybackIsoBmffDescriptorHeaderParser.Parse(
            [],
            out var header,
            new() { MaximumPayloadBytes = maximum });

        Assert.Equal(
            PlaybackIsoBmffDescriptorHeaderParseResult.InvalidConfiguration,
            result);
        Assert.Null(header);
    }
}
