namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffExplicitSbrPsExtensionParserTests
{
    [Fact]
    public void Parse_ProjectsExplicitSbrWithIndexedFrequency()
    {
        var header = Header(Bits(
            (5, 5), (7, 4), (2, 4),
            (4, 4), (2, 5)));

        Assert.Equal(
            PlaybackIsoBmffExplicitSbrPsExtensionParseResult.Parsed,
            PlaybackIsoBmffExplicitSbrPsExtensionParser.Parse(
                header,
                out var extension));
        var parsed = Assert.IsType<PlaybackIsoBmffExplicitSbrPsExtension>(
            extension);
        Assert.Same(header, parsed.AudioSpecificConfigHeader);
        Assert.True(parsed.SbrPresent);
        Assert.False(parsed.PsPresent);
        Assert.Equal((byte)4, parsed.ExtensionSamplingFrequencyIndex);
        Assert.Equal(44_100u, parsed.ExtensionSamplingFrequency);
        Assert.Equal((byte)2, parsed.CoreAudioObjectType);
        Assert.Null(parsed.ExtensionChannelConfiguration);
        Assert.Equal(22, parsed.ConsumedBitLength);
    }

    [Fact]
    public void Parse_ProjectsExplicitPsAndExplicitFrequency()
    {
        var header = Header(Bits(
            (29, 5), (8, 4), (1, 4),
            (15, 4), (48_000, 24), (2, 5)));

        Assert.Equal(
            PlaybackIsoBmffExplicitSbrPsExtensionParseResult.Parsed,
            PlaybackIsoBmffExplicitSbrPsExtensionParser.Parse(
                header,
                out var extension));
        Assert.True(extension!.SbrPresent);
        Assert.True(extension.PsPresent);
        Assert.Equal((byte)15, extension.ExtensionSamplingFrequencyIndex);
        Assert.Equal(48_000u, extension.ExtensionSamplingFrequency);
        Assert.Equal((byte)2, extension.CoreAudioObjectType);
        Assert.Equal(46, extension.ConsumedBitLength);
    }

    [Fact]
    public void Parse_ProjectsExtendedCoreAudioObjectType()
    {
        var header = Header(Bits(
            (5, 5), (4, 4), (2, 4),
            (3, 4), (31, 5), (10, 6)));

        Assert.Equal(
            PlaybackIsoBmffExplicitSbrPsExtensionParseResult.Parsed,
            PlaybackIsoBmffExplicitSbrPsExtensionParser.Parse(
                header,
                out var extension));
        Assert.Equal((byte)42, extension!.CoreAudioObjectType);
        Assert.Equal(28, extension.ConsumedBitLength);
    }

    [Fact]
    public void Parse_ProjectsErBsacExtensionChannelConfiguration()
    {
        var header = Header(Bits(
            (5, 5), (4, 4), (2, 4),
            (3, 4), (22, 5), (6, 4)));

        Assert.Equal(
            PlaybackIsoBmffExplicitSbrPsExtensionParseResult.Parsed,
            PlaybackIsoBmffExplicitSbrPsExtensionParser.Parse(
                header,
                out var extension));
        Assert.Equal((byte)22, extension!.CoreAudioObjectType);
        Assert.Equal((byte)6, extension.ExtensionChannelConfiguration);
        Assert.Equal(26, extension.ConsumedBitLength);
    }

    [Fact]
    public void Parse_MissingHeaderIsDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffExplicitSbrPsExtensionParseResult
                .MissingAudioSpecificConfigHeader,
            PlaybackIsoBmffExplicitSbrPsExtensionParser.Parse(
                null,
                out var extension));
        Assert.Null(extension);
    }

    [Fact]
    public void Parse_NonExtensionObjectTypeIsUnsupported()
    {
        Assert.Equal(
            PlaybackIsoBmffExplicitSbrPsExtensionParseResult
                .UnsupportedAudioObjectType,
            PlaybackIsoBmffExplicitSbrPsExtensionParser.Parse(
                Header(Bits((2, 5), (4, 4), (2, 4))),
                out var extension));
        Assert.Null(extension);
    }

    [Theory]
    [InlineData(13)]
    [InlineData(14)]
    public void Parse_ReservedExtensionFrequencyIsRejected(int index)
    {
        Assert.Equal(
            PlaybackIsoBmffExplicitSbrPsExtensionParseResult
                .InvalidExtensionSamplingFrequency,
            PlaybackIsoBmffExplicitSbrPsExtensionParser.Parse(
                Header(Bits(
                    (5, 5), (4, 4), (2, 4),
                    ((uint)index, 4), (2, 5))),
                out var extension));
        Assert.Null(extension);
    }

    [Fact]
    public void Parse_ExplicitZeroExtensionFrequencyIsRejected()
    {
        Assert.Equal(
            PlaybackIsoBmffExplicitSbrPsExtensionParseResult
                .InvalidExtensionSamplingFrequency,
            PlaybackIsoBmffExplicitSbrPsExtensionParser.Parse(
                Header(Bits(
                    (5, 5), (4, 4), (2, 4),
                    (15, 4), (0, 24), (2, 5))),
                out var extension));
        Assert.Null(extension);
    }

    [Fact]
    public void Parse_CoreAudioObjectTypeZeroIsRejected()
    {
        Assert.Equal(
            PlaybackIsoBmffExplicitSbrPsExtensionParseResult
                .InvalidCoreAudioObjectType,
            PlaybackIsoBmffExplicitSbrPsExtensionParser.Parse(
                Header(Bits(
                    (5, 5), (4, 4), (2, 4),
                    (3, 4), (0, 5))),
                out var extension));
        Assert.Null(extension);
    }

    [Fact]
    public void Parse_TruncatedConditionalFieldsNeedMoreData()
    {
        byte[][] configurations =
        {
            Bits((5, 5), (4, 4), (2, 4)),
            Bits((5, 5), (4, 4), (2, 4), (15, 4)),
            Bits((5, 5), (4, 4), (2, 4), (3, 4), (31, 5)),
            Bits((5, 5), (4, 4), (2, 4), (3, 4), (22, 5)),
        };

        foreach (var configuration in configurations)
        {
            Assert.Equal(
                PlaybackIsoBmffExplicitSbrPsExtensionParseResult.NeedMoreData,
                PlaybackIsoBmffExplicitSbrPsExtensionParser.Parse(
                    Header(configuration),
                    out var extension));
            Assert.Null(extension);
        }
    }

    private static PlaybackIsoBmffAudioSpecificConfigHeader Header(
        byte[] configuration)
    {
        Assert.Equal(
            PlaybackIsoBmffAudioSpecificConfigHeaderParseResult.Parsed,
            PlaybackIsoBmffAudioSpecificConfigHeaderParser.Parse(
                DecoderConfig(configuration),
                out var header));
        return Assert.IsType<PlaybackIsoBmffAudioSpecificConfigHeader>(header);
    }

    private static PlaybackIsoBmffDecoderConfigDescriptor DecoderConfig(
        byte[] decoderSpecificInfo)
    {
        var specific = Descriptor(0x05, decoderSpecificInfo);
        var payload = new byte[13 + specific.Length];
        payload[0] = 0x40;
        payload[1] = (byte)((5 << 2) | 1);
        specific.CopyTo(payload, 13);
        var elementaryStream = ElementaryStream(payload);
        Assert.Equal(
            PlaybackIsoBmffDecoderConfigDescriptorParseResult.Parsed,
            PlaybackIsoBmffDecoderConfigDescriptorParser.Parse(
                elementaryStream,
                out var decoderConfig));
        return Assert.IsType<PlaybackIsoBmffDecoderConfigDescriptor>(
            decoderConfig);
    }

    private static PlaybackIsoBmffElementaryStreamDescriptor ElementaryStream(
        byte[] decoderConfigPayload)
    {
        var decoder = Descriptor(0x04, decoderConfigPayload);
        var sl = Descriptor(0x06, [2]);
        var outerPayload = new byte[3 + decoder.Length + sl.Length];
        outerPayload[1] = 1;
        decoder.CopyTo(outerPayload, 3);
        sl.CopyTo(outerPayload, 3 + decoder.Length);
        var bytes = Descriptor(0x03, outerPayload);
        var decoderLocation = new PlaybackIsoBmffDescriptorLocation(
            new(0x04, 2, decoderConfigPayload.Length),
            5);
        var slLocation = new PlaybackIsoBmffDescriptorLocation(
            new(0x06, 2, 1),
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

    private static byte[] Bits(params (uint Value, int Count)[] fields)
    {
        var bitCount = fields.Sum(field => field.Count);
        var output = new byte[(bitCount + 7) / 8];
        var offset = 0;
        foreach (var (value, count) in fields)
        {
            Assert.InRange(count, 1, 32);
            Assert.True(count == 32 || value < (1u << count));
            for (var index = 0; index < count; index++)
            {
                var bit = (value >> (count - index - 1)) & 1;
                output[(offset + index) / 8] |=
                    (byte)(bit << (7 - ((offset + index) % 8)));
            }

            offset += count;
        }

        return output;
    }

    private static byte[] Descriptor(byte tag, byte[] payload) =>
        [tag, (byte)payload.Length, .. payload];

    private static PlaybackIsoBmffBoxLocation BoxLocation(uint type) =>
        new(new(type, 8, 8, null), 0, 8);
}
