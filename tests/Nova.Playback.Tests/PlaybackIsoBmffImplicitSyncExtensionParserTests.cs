namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffImplicitSyncExtensionParserTests
{
    [Fact]
    public void ParseSbr_ProjectsIndexedExtensionFrequency()
    {
        var ga = Ga(Bits(
            (2, 5), (4, 4), (2, 4), (0, 3),
            (0x2B7, 11), (5, 5), (1, 1), (3, 4)));

        Assert.Equal(
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult.Parsed,
            PlaybackIsoBmffImplicitSbrSyncExtensionParser.Parse(
                ga,
                out var extension));
        var parsed = Assert.IsType<PlaybackIsoBmffImplicitSbrSyncExtension>(
            extension);
        Assert.Same(ga, parsed.GaSpecificConfig);
        Assert.True(parsed.SbrPresent);
        Assert.Equal((byte)3, parsed.ExtensionSamplingFrequencyIndex);
        Assert.Equal(48_000u, parsed.ExtensionSamplingFrequency);
        Assert.Equal(37, parsed.ConsumedBitLength);
    }

    [Fact]
    public void ParseSbr_PresenceFalseHasNoFrequency()
    {
        var ga = Ga(Bits(
            (2, 5), (4, 4), (2, 4), (0, 3),
            (0x2B7, 11), (5, 5), (0, 1)));

        Assert.Equal(
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult.Parsed,
            PlaybackIsoBmffImplicitSbrSyncExtensionParser.Parse(
                ga,
                out var extension));
        Assert.False(extension!.SbrPresent);
        Assert.Null(extension.ExtensionSamplingFrequencyIndex);
        Assert.Null(extension.ExtensionSamplingFrequency);
        Assert.Equal(33, extension.ConsumedBitLength);
    }

    [Fact]
    public void ParseSbr_ProjectsExplicitExtensionFrequency()
    {
        var ga = Ga(Bits(
            (2, 5), (4, 4), (2, 4), (0, 3),
            (0x2B7, 11), (5, 5), (1, 1),
            (15, 4), (48_000, 24)));

        Assert.Equal(
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult.Parsed,
            PlaybackIsoBmffImplicitSbrSyncExtensionParser.Parse(
                ga,
                out var extension));
        Assert.Equal((byte)15, extension!.ExtensionSamplingFrequencyIndex);
        Assert.Equal(48_000u, extension.ExtensionSamplingFrequency);
        Assert.Equal(61, extension.ConsumedBitLength);
    }

    [Fact]
    public void ParseSbr_MissingGaConfigIsDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
                .MissingGaSpecificConfig,
            PlaybackIsoBmffImplicitSbrSyncExtensionParser.Parse(
                null,
                out var extension));
        Assert.Null(extension);
    }

    [Fact]
    public void ParseSbr_ExplicitExtensionIsNotReinterpreted()
    {
        var header = Header(Bits(
            (5, 5), (7, 4), (2, 4),
            (4, 4), (2, 5), (0, 3)));
        Assert.Equal(
            PlaybackIsoBmffExplicitSbrPsExtensionParseResult.Parsed,
            PlaybackIsoBmffExplicitSbrPsExtensionParser.Parse(
                header,
                out var explicitExtension));
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigParseResult.Parsed,
            PlaybackIsoBmffGaSpecificConfigParser.ParseExplicitExtension(
                explicitExtension,
                out var ga));

        Assert.Equal(
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
                .ExplicitExtensionAlreadyPresent,
            PlaybackIsoBmffImplicitSbrSyncExtensionParser.Parse(
                ga,
                out var extension));
        Assert.Null(extension);
    }

    [Fact]
    public void ParseSbr_ErCoreTypeIsOutsideImmediateBoundary()
    {
        var ga = Ga(Bits(
            (17, 5), (4, 4), (2, 4), (0, 3),
            (0x2B7, 11), (5, 5), (0, 1)));

        Assert.Equal(
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
                .UnsupportedCoreAudioObjectType,
            PlaybackIsoBmffImplicitSbrSyncExtensionParser.Parse(
                ga,
                out var extension));
        Assert.Null(extension);
    }

    [Fact]
    public void ParseSbr_AbsentOrDifferentMarkerIsNotPresent()
    {
        var insufficient = Ga(Bits(
            (2, 5), (4, 4), (2, 4), (0, 3)));
        var different = Ga(Bits(
            (2, 5), (4, 4), (2, 4), (0, 3),
            (0x123, 11)));

        Assert.Equal(
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult.NotPresent,
            PlaybackIsoBmffImplicitSbrSyncExtensionParser.Parse(
                insufficient,
                out var absent));
        Assert.Null(absent);
        Assert.Equal(
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult.NotPresent,
            PlaybackIsoBmffImplicitSbrSyncExtensionParser.Parse(
                different,
                out var mismatched));
        Assert.Null(mismatched);
    }

    [Fact]
    public void ParseSbr_UnsupportedExtensionObjectTypeIsDistinct()
    {
        var ga = Ga(Bits(
            (2, 5), (4, 4), (2, 4), (0, 3),
            (0x2B7, 11), (2, 5)));

        Assert.Equal(
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
                .UnsupportedExtensionAudioObjectType,
            PlaybackIsoBmffImplicitSbrSyncExtensionParser.Parse(
                ga,
                out var extension));
        Assert.Null(extension);
    }

    [Theory]
    [InlineData(13)]
    [InlineData(14)]
    public void ParseSbr_ReservedFrequencyIsRejected(int index)
    {
        var ga = Ga(Bits(
            (2, 5), (4, 4), (2, 4), (0, 3),
            (0x2B7, 11), (5, 5), (1, 1), ((uint)index, 4)));

        Assert.Equal(
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
                .InvalidExtensionSamplingFrequency,
            PlaybackIsoBmffImplicitSbrSyncExtensionParser.Parse(
                ga,
                out var extension));
        Assert.Null(extension);
    }

    [Fact]
    public void ParseSbr_ExplicitZeroFrequencyIsRejected()
    {
        var ga = Ga(Bits(
            (2, 5), (4, 4), (2, 4), (0, 3),
            (0x2B7, 11), (5, 5), (1, 1), (15, 4), (0, 24)));

        Assert.Equal(
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
                .InvalidExtensionSamplingFrequency,
            PlaybackIsoBmffImplicitSbrSyncExtensionParser.Parse(
                ga,
                out var extension));
        Assert.Null(extension);
    }

    [Fact]
    public void ParseSbr_TruncatedConditionalFieldsNeedMoreData()
    {
        byte[][] configurations =
        {
            Bits(
                (2, 5), (4, 4), (2, 4), (0, 3),
                (0x2B7, 11), (31, 5)),
            Bits(
                (2, 5), (4, 4), (2, 4), (0, 3),
                (0x2B7, 11), (5, 5)),
            Bits(
                (2, 5), (4, 4), (2, 4), (0, 3),
                (0x2B7, 11), (5, 5), (1, 1), (15, 4)),
        };

        foreach (var configuration in configurations)
        {
            Assert.Equal(
                PlaybackIsoBmffImplicitSbrSyncExtensionParseResult.NeedMoreData,
                PlaybackIsoBmffImplicitSbrSyncExtensionParser.Parse(
                    Ga(configuration),
                    out var extension));
            Assert.Null(extension);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ParsePs_ProjectsPresenceFlag(int present)
    {
        var sbr = Sbr(Bits(
            (2, 5), (4, 4), (2, 4), (0, 3),
            (0x2B7, 11), (5, 5), (1, 1), (3, 4),
            (0x548, 11), ((uint)present, 1)));

        Assert.Equal(
            PlaybackIsoBmffImplicitPsSyncExtensionParseResult.Parsed,
            PlaybackIsoBmffImplicitPsSyncExtensionParser.Parse(
                sbr,
                out var extension));
        Assert.Same(sbr, extension!.SbrSyncExtension);
        Assert.Equal(present != 0, extension.PsPresent);
        Assert.Equal(49, extension.ConsumedBitLength);
    }

    [Fact]
    public void ParsePs_MissingSbrExtensionIsDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffImplicitPsSyncExtensionParseResult
                .MissingSbrSyncExtension,
            PlaybackIsoBmffImplicitPsSyncExtensionParser.Parse(
                null,
                out var extension));
        Assert.Null(extension);
    }

    [Fact]
    public void ParsePs_SbrPresenceFalseIsDistinct()
    {
        var sbr = Sbr(Bits(
            (2, 5), (4, 4), (2, 4), (0, 3),
            (0x2B7, 11), (5, 5), (0, 1)));

        Assert.Equal(
            PlaybackIsoBmffImplicitPsSyncExtensionParseResult.SbrNotPresent,
            PlaybackIsoBmffImplicitPsSyncExtensionParser.Parse(
                sbr,
                out var extension));
        Assert.Null(extension);
    }

    [Fact]
    public void ParsePs_AbsentOrDifferentMarkerIsNotPresent()
    {
        var absent = Sbr(Bits(
            (2, 5), (4, 4), (2, 4), (0, 3),
            (0x2B7, 11), (5, 5), (1, 1), (3, 4)));
        var different = Sbr(Bits(
            (2, 5), (4, 4), (2, 4), (0, 3),
            (0x2B7, 11), (5, 5), (1, 1), (3, 4),
            (0x123, 11), (1, 1)));

        Assert.Equal(
            PlaybackIsoBmffImplicitPsSyncExtensionParseResult.NotPresent,
            PlaybackIsoBmffImplicitPsSyncExtensionParser.Parse(
                absent,
                out var missing));
        Assert.Null(missing);
        Assert.Equal(
            PlaybackIsoBmffImplicitPsSyncExtensionParseResult.NotPresent,
            PlaybackIsoBmffImplicitPsSyncExtensionParser.Parse(
                different,
                out var mismatched));
        Assert.Null(mismatched);
    }

    [Fact]
    public void ParsePs_TruncatedPresenceFlagNeedsMoreData()
    {
        var sbr = Sbr(Bits(
            (2, 5), (4, 4), (2, 4), (0, 3),
            (0x2B7, 11), (5, 5), (1, 1), (3, 4),
            (0x548, 11)));

        Assert.Equal(
            PlaybackIsoBmffImplicitPsSyncExtensionParseResult.NeedMoreData,
            PlaybackIsoBmffImplicitPsSyncExtensionParser.Parse(
                sbr,
                out var extension));
        Assert.Null(extension);
    }

    private static PlaybackIsoBmffImplicitSbrSyncExtension Sbr(
        byte[] configuration)
    {
        Assert.Equal(
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult.Parsed,
            PlaybackIsoBmffImplicitSbrSyncExtensionParser.Parse(
                Ga(configuration),
                out var extension));
        return Assert.IsType<PlaybackIsoBmffImplicitSbrSyncExtension>(extension);
    }

    internal static PlaybackIsoBmffGaSpecificConfig Ga(byte[] configuration)
    {
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigParseResult.Parsed,
            PlaybackIsoBmffGaSpecificConfigParser.Parse(
                Header(configuration),
                out var config));
        return Assert.IsType<PlaybackIsoBmffGaSpecificConfig>(config);
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

    internal static byte[] Bits(params (uint Value, int Count)[] fields)
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
