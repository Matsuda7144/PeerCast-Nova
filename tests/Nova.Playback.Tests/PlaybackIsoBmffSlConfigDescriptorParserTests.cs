namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffSlConfigDescriptorParserTests
{
    [Fact]
    public void Parse_ProjectsMp4PredefinedConfiguration()
    {
        var elementaryStream = ElementaryStream([2]);

        Assert.Equal(
            PlaybackIsoBmffSlConfigDescriptorParseResult.Parsed,
            PlaybackIsoBmffSlConfigDescriptorParser.Parse(
                elementaryStream,
                out var slConfig));
        var parsed = Assert.IsType<PlaybackIsoBmffSlConfigDescriptor>(slConfig);
        Assert.Same(elementaryStream, parsed.ElementaryStream);
        Assert.Equal((byte)2, parsed.Predefined);
    }

    [Fact]
    public void Parse_MissingElementaryStreamIsDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffSlConfigDescriptorParseResult
                .MissingElementaryStreamDescriptor,
            PlaybackIsoBmffSlConfigDescriptorParser.Parse(null, out var config));
        Assert.Null(config);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(255)]
    public void Parse_OtherPredefinedConfigurationsAreUnsupported(int value)
    {
        Assert.Equal(
            PlaybackIsoBmffSlConfigDescriptorParseResult
                .UnsupportedPredefinedConfiguration,
            PlaybackIsoBmffSlConfigDescriptorParser.Parse(
                ElementaryStream([(byte)value]),
                out var config));
        Assert.Null(config);
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 2, 0 })]
    public void Parse_RequiresExactOneBytePayload(byte[] payload)
    {
        Assert.Equal(
            PlaybackIsoBmffSlConfigDescriptorParseResult.InvalidPayload,
            PlaybackIsoBmffSlConfigDescriptorParser.Parse(
                ElementaryStream(payload),
                out var config));
        Assert.Null(config);
    }

    [Fact]
    public void Parse_ForeignSlConfigLocationIsInvalidHierarchy()
    {
        var owned = ElementaryStream([2]);
        var foreign = ElementaryStream([2]);
        var invalid = new PlaybackIsoBmffElementaryStreamDescriptor(
            owned.AudioEntry,
            owned.ElementaryStreamBox,
            owned.ElementaryStreamId,
            owned.StreamPriority,
            owned.DependsOnElementaryStreamId,
            owned.Url.ToArray(),
            owned.OcrElementaryStreamId,
            Array.AsReadOnly(owned.Children.ToArray()),
            owned.DecoderConfigDescriptor,
            foreign.SlConfigDescriptor,
            owned.DescriptorBytes.ToArray());

        Assert.Equal(
            PlaybackIsoBmffSlConfigDescriptorParseResult.InvalidHierarchy,
            PlaybackIsoBmffSlConfigDescriptorParser.Parse(
                invalid,
                out var config));
        Assert.Null(config);
    }

    private static PlaybackIsoBmffElementaryStreamDescriptor ElementaryStream(
        byte[] slPayload)
    {
        var decoder = Descriptor(0x04, [0x40]);
        var sl = Descriptor(0x06, slPayload);
        var payload = new byte[3 + decoder.Length + sl.Length];
        payload[1] = 1;
        decoder.CopyTo(payload, 3);
        sl.CopyTo(payload, 3 + decoder.Length);
        var bytes = Descriptor(0x03, payload);
        var decoderLocation = new PlaybackIsoBmffDescriptorLocation(
            new(0x04, 2, 1),
            5);
        var slLocation = new PlaybackIsoBmffDescriptorLocation(
            new(0x06, 2, slPayload.Length),
            5 + decoder.Length);
        return new(
            AudioEntry(),
            BoxLocation(0x65736473),
            1,
            0,
            null,
            [],
            null,
            Array.AsReadOnly(new[] { decoderLocation, slLocation }),
            decoderLocation,
            slLocation,
            bytes);
    }

    private static PlaybackIsoBmffAudioSampleEntry AudioEntry()
    {
        var selection = new PlaybackIsoBmffEditedSampleDescriptionSelection(
            1,
            BoxLocation(0x6D703461),
            PlaybackIsoBmffSampleEntryCodecCandidate.Mp4a,
            PlaybackIsoBmffHandlerKind.Audio);
        return new(
            selection,
            1,
            2,
            16,
            48_000u << 16,
            Array.AsReadOnly(Array.Empty<PlaybackIsoBmffBoxLocation>()));
    }

    private static byte[] Descriptor(byte tag, byte[] payload) =>
        [tag, (byte)payload.Length, .. payload];

    private static PlaybackIsoBmffBoxLocation BoxLocation(uint type) =>
        new(new(type, 8, 8, null), 0, 8);
}
