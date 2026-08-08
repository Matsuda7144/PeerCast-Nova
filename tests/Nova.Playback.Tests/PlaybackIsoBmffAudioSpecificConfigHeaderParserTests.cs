namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffAudioSpecificConfigHeaderParserTests
{
    [Fact]
    public void Parse_ProjectsAacLowComplexityHeader()
    {
        var decoderConfig = DecoderConfig(0x40, [0x12, 0x10]);

        var result = PlaybackIsoBmffAudioSpecificConfigHeaderParser.Parse(
            decoderConfig,
            out var header);

        Assert.Equal(
            PlaybackIsoBmffAudioSpecificConfigHeaderParseResult.Parsed,
            result);
        var parsed = Assert.IsType<PlaybackIsoBmffAudioSpecificConfigHeader>(
            header);
        Assert.Same(decoderConfig, parsed.DecoderConfig);
        Assert.Equal((byte)2, parsed.AudioObjectType);
        Assert.Equal((byte)4, parsed.SamplingFrequencyIndex);
        Assert.Equal(44_100u, parsed.SamplingFrequency);
        Assert.Equal((byte)2, parsed.ChannelConfiguration);
        Assert.Equal(13, parsed.HeaderBitLength);
        Assert.Equal(new byte[] { 0x12, 0x10 },
            parsed.ConfigurationBytes.ToArray());
    }

    [Fact]
    public void Parse_ProjectsExplicitSamplingFrequency()
    {
        var configuration = Bits(
            (2, 5),
            (15, 4),
            (48_000, 24),
            (2, 4));

        Assert.Equal(
            PlaybackIsoBmffAudioSpecificConfigHeaderParseResult.Parsed,
            PlaybackIsoBmffAudioSpecificConfigHeaderParser.Parse(
                DecoderConfig(0x40, configuration),
                out var header));
        Assert.Equal((byte)15, header!.SamplingFrequencyIndex);
        Assert.Equal(48_000u, header.SamplingFrequency);
        Assert.Equal(37, header.HeaderBitLength);
    }

    [Fact]
    public void Parse_ProjectsExtendedAudioObjectType()
    {
        var configuration = Bits(
            (31, 5),
            (10, 6),
            (3, 4),
            (1, 4));

        Assert.Equal(
            PlaybackIsoBmffAudioSpecificConfigHeaderParseResult.Parsed,
            PlaybackIsoBmffAudioSpecificConfigHeaderParser.Parse(
                DecoderConfig(0x40, configuration),
                out var header));
        Assert.Equal((byte)42, header!.AudioObjectType);
        Assert.Equal(48_000u, header.SamplingFrequency);
        Assert.Equal((byte)1, header.ChannelConfiguration);
        Assert.Equal(19, header.HeaderBitLength);
    }

    [Fact]
    public void Parse_ChannelConfigurationZeroIsPreservedForLaterSemantics()
    {
        Assert.Equal(
            PlaybackIsoBmffAudioSpecificConfigHeaderParseResult.Parsed,
            PlaybackIsoBmffAudioSpecificConfigHeaderParser.Parse(
                DecoderConfig(0x40, Bits((2, 5), (4, 4), (0, 4))),
                out var header));
        Assert.Equal((byte)0, header!.ChannelConfiguration);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_048_577)]
    public void Parse_InvalidLimitRejectsBeforeInputAccess(int maximumBytes)
    {
        Assert.Equal(
            PlaybackIsoBmffAudioSpecificConfigHeaderParseResult
                .InvalidConfiguration,
            PlaybackIsoBmffAudioSpecificConfigHeaderParser.Parse(
                null,
                out var header,
                new() { MaximumConfigurationBytes = maximumBytes }));
        Assert.Null(header);
    }

    [Fact]
    public void Parse_MissingDecoderConfigIsDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffAudioSpecificConfigHeaderParseResult
                .MissingDecoderConfig,
            PlaybackIsoBmffAudioSpecificConfigHeaderParser.Parse(
                null,
                out var header));
        Assert.Null(header);
    }

    [Fact]
    public void Parse_RequiresMpeg4AudioObjectTypeIndication()
    {
        Assert.Equal(
            PlaybackIsoBmffAudioSpecificConfigHeaderParseResult
                .UnsupportedObjectTypeIndication,
            PlaybackIsoBmffAudioSpecificConfigHeaderParser.Parse(
                DecoderConfig(0x66, [0x12, 0x10]),
                out var header));
        Assert.Null(header);
    }

    [Theory]
    [InlineData(new byte[] { 0xF8 })]
    [InlineData(new byte[] { 0x17, 0x80 })]
    public void Parse_TruncatedHeaderNeedsMoreData(byte[] configuration)
    {
        Assert.Equal(
            PlaybackIsoBmffAudioSpecificConfigHeaderParseResult.NeedMoreData,
            PlaybackIsoBmffAudioSpecificConfigHeaderParser.Parse(
                DecoderConfig(0x40, configuration),
                out var header));
        Assert.Null(header);
    }

    [Fact]
    public void Parse_AudioObjectTypeZeroIsRejected()
    {
        Assert.Equal(
            PlaybackIsoBmffAudioSpecificConfigHeaderParseResult
                .InvalidAudioObjectType,
            PlaybackIsoBmffAudioSpecificConfigHeaderParser.Parse(
                DecoderConfig(0x40, Bits((0, 5), (4, 4), (2, 4))),
                out var header));
        Assert.Null(header);
    }

    [Theory]
    [InlineData(13)]
    [InlineData(14)]
    public void Parse_ReservedSamplingFrequencyIndexIsRejected(int index)
    {
        Assert.Equal(
            PlaybackIsoBmffAudioSpecificConfigHeaderParseResult
                .InvalidSamplingFrequency,
            PlaybackIsoBmffAudioSpecificConfigHeaderParser.Parse(
                DecoderConfig(0x40, Bits((2, 5), ((uint)index, 4), (2, 4))),
                out var header));
        Assert.Null(header);
    }

    [Fact]
    public void Parse_ExplicitZeroSamplingFrequencyIsRejected()
    {
        Assert.Equal(
            PlaybackIsoBmffAudioSpecificConfigHeaderParseResult
                .InvalidSamplingFrequency,
            PlaybackIsoBmffAudioSpecificConfigHeaderParser.Parse(
                DecoderConfig(0x40,
                    Bits((2, 5), (15, 4), (0, 24), (2, 4))),
                out var header));
        Assert.Null(header);
    }

    [Fact]
    public void Parse_ConfigurationByteLimitIsEnforced()
    {
        Assert.Equal(
            PlaybackIsoBmffAudioSpecificConfigHeaderParseResult
                .ConfigurationLimitExceeded,
            PlaybackIsoBmffAudioSpecificConfigHeaderParser.Parse(
                DecoderConfig(0x40, [0x12, 0x10]),
                out var header,
                new() { MaximumConfigurationBytes = 1 }));
        Assert.Null(header);
    }

    private static PlaybackIsoBmffDecoderConfigDescriptor DecoderConfig(
        byte objectTypeIndication,
        byte[] decoderSpecificInfo)
    {
        var specific = Descriptor(0x05, decoderSpecificInfo);
        var payload = new byte[13 + specific.Length];
        payload[0] = objectTypeIndication;
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
        var decoderOffset = 5;
        var decoderLocation = new PlaybackIsoBmffDescriptorLocation(
            new(0x04, 2, decoderConfigPayload.Length),
            decoderOffset);
        var slLocation = new PlaybackIsoBmffDescriptorLocation(
            new(0x06, 2, 1),
            decoderOffset + decoder.Length);
        var children = Array.AsReadOnly(
            new[] { decoderLocation, slLocation });
        return new(
            AudioEntry(),
            BoxLocation(0x65736473),
            1,
            0,
            null,
            [],
            null,
            children,
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

    private static byte[] Descriptor(byte tag, byte[] payload)
    {
        Assert.True(payload.Length < 128);
        return [tag, (byte)payload.Length, .. payload];
    }

    private static PlaybackIsoBmffBoxLocation BoxLocation(uint type) =>
        new(new(type, 8, 8, null), 0, 8);
}
