using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffDecoderConfigDescriptorParserTests
{
    [Fact]
    public void Parse_ProjectsAudioDecoderConfigurationAndSpecificInfo()
    {
        var payload = DecoderConfigPayload(
            Descriptor(0x05, [0x12, 0x10]));

        var result = PlaybackIsoBmffDecoderConfigDescriptorParser.Parse(
            ElementaryStream(payload),
            out var decoderConfig);

        Assert.Equal(
            PlaybackIsoBmffDecoderConfigDescriptorParseResult.Parsed,
            result);
        var parsed = Assert.IsType<PlaybackIsoBmffDecoderConfigDescriptor>(
            decoderConfig);
        Assert.Equal((byte)0x40, parsed.ObjectTypeIndication);
        Assert.Equal((byte)0x05, parsed.StreamType);
        Assert.False(parsed.Upstream);
        Assert.Equal(0x010203u, parsed.BufferSizeBytes);
        Assert.Equal(128_000u, parsed.MaximumBitrate);
        Assert.Equal(96_000u, parsed.AverageBitrate);
        Assert.Single(parsed.Children);
        Assert.Same(parsed.Children[0], parsed.DecoderSpecificInfoDescriptor);
        Assert.True(parsed.TryGetPayload(
            parsed.DecoderSpecificInfoDescriptor,
            out var specificInfo));
        Assert.Equal(new byte[] { 0x12, 0x10 }, specificInfo.ToArray());
    }

    [Fact]
    public void Parse_PreservesUpstreamFlagAndUnknownChildren()
    {
        var payload = DecoderConfigPayload(
            Descriptor(0x0A, [1]),
            Descriptor(0x05, [2]));
        payload[1] |= 0x02;

        Assert.Equal(
            PlaybackIsoBmffDecoderConfigDescriptorParseResult.Parsed,
            PlaybackIsoBmffDecoderConfigDescriptorParser.Parse(
                ElementaryStream(payload),
                out var decoderConfig));
        Assert.True(decoderConfig!.Upstream);
        Assert.Equal(2, decoderConfig.Children.Count);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(257, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 1_048_577)]
    public void Parse_InvalidLimitsRejectBeforeInputAccess(
        int maximumDescriptors,
        int maximumBytes)
    {
        Assert.Equal(
            PlaybackIsoBmffDecoderConfigDescriptorParseResult
                .InvalidConfiguration,
            PlaybackIsoBmffDecoderConfigDescriptorParser.Parse(
                null,
                out var decoderConfig,
                new()
                {
                    MaximumDescriptorCount = maximumDescriptors,
                    MaximumDecoderSpecificBytes = maximumBytes,
                }));
        Assert.Null(decoderConfig);
    }

    [Fact]
    public void Parse_MissingElementaryStreamIsDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffDecoderConfigDescriptorParseResult
                .MissingElementaryStreamDescriptor,
            PlaybackIsoBmffDecoderConfigDescriptorParser.Parse(
                null,
                out var decoderConfig));
        Assert.Null(decoderConfig);
    }

    [Fact]
    public void Parse_ReservedBitAndFixedPrefixAreRequired()
    {
        var missingReserved = DecoderConfigPayload(Descriptor(0x05, [1]));
        missingReserved[1] &= 0xFE;
        Assert.Equal(
            PlaybackIsoBmffDecoderConfigDescriptorParseResult.InvalidPayload,
            PlaybackIsoBmffDecoderConfigDescriptorParser.Parse(
                ElementaryStream(missingReserved),
                out var reserved));
        Assert.Null(reserved);

        Assert.Equal(
            PlaybackIsoBmffDecoderConfigDescriptorParseResult.InvalidPayload,
            PlaybackIsoBmffDecoderConfigDescriptorParser.Parse(
                ElementaryStream(new byte[12]),
                out var truncated));
        Assert.Null(truncated);
    }

    [Fact]
    public void Parse_RequiresAudioStreamType()
    {
        var payload = DecoderConfigPayload(Descriptor(0x05, [1]));
        payload[1] = (byte)((4 << 2) | 1);

        Assert.Equal(
            PlaybackIsoBmffDecoderConfigDescriptorParseResult
                .UnsupportedStreamType,
            PlaybackIsoBmffDecoderConfigDescriptorParser.Parse(
                ElementaryStream(payload),
                out var decoderConfig));
        Assert.Null(decoderConfig);
    }

    [Fact]
    public void Parse_MissingDuplicateAndEmptySpecificInfoAreDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffDecoderConfigDescriptorParseResult
                .MissingDecoderSpecificInfo,
            PlaybackIsoBmffDecoderConfigDescriptorParser.Parse(
                ElementaryStream(DecoderConfigPayload()),
                out var missing));
        Assert.Null(missing);

        Assert.Equal(
            PlaybackIsoBmffDecoderConfigDescriptorParseResult
                .DuplicateDecoderSpecificInfo,
            PlaybackIsoBmffDecoderConfigDescriptorParser.Parse(
                ElementaryStream(DecoderConfigPayload(
                    Descriptor(0x05, [1]),
                    Descriptor(0x05, [2]))),
                out var duplicate));
        Assert.Null(duplicate);

        Assert.Equal(
            PlaybackIsoBmffDecoderConfigDescriptorParseResult
                .InvalidDecoderSpecificInfo,
            PlaybackIsoBmffDecoderConfigDescriptorParser.Parse(
                ElementaryStream(DecoderConfigPayload(
                    Descriptor(0x05, []))),
                out var empty));
        Assert.Null(empty);
    }

    [Fact]
    public void Parse_InvalidChildSequenceFailsClosed()
    {
        var payload = DecoderConfigPayload();
        Array.Resize(ref payload, payload.Length + 1);
        payload[^1] = 0x05;

        Assert.Equal(
            PlaybackIsoBmffDecoderConfigDescriptorParseResult
                .InvalidChildSequence,
            PlaybackIsoBmffDecoderConfigDescriptorParser.Parse(
                ElementaryStream(payload),
                out var decoderConfig));
        Assert.Null(decoderConfig);
    }

    [Fact]
    public void Parse_DescriptorCountAndPayloadBytesAreBounded()
    {
        Assert.Equal(
            PlaybackIsoBmffDecoderConfigDescriptorParseResult
                .DescriptorLimitExceeded,
            PlaybackIsoBmffDecoderConfigDescriptorParser.Parse(
                ElementaryStream(DecoderConfigPayload(
                    Descriptor(0x0A, [1]),
                    Descriptor(0x05, [2]))),
                out var tooMany,
                new() { MaximumDescriptorCount = 1 }));
        Assert.Null(tooMany);

        Assert.Equal(
            PlaybackIsoBmffDecoderConfigDescriptorParseResult
                .DescriptorPayloadLimitExceeded,
            PlaybackIsoBmffDecoderConfigDescriptorParser.Parse(
                ElementaryStream(DecoderConfigPayload(
                    Descriptor(0x05, [1, 2]))),
                out var tooLarge,
                new() { MaximumDecoderSpecificBytes = 1 }));
        Assert.Null(tooLarge);
    }

    [Fact]
    public void TryGetPayloadRejectsForeignLocation()
    {
        Assert.Equal(
            PlaybackIsoBmffDecoderConfigDescriptorParseResult.Parsed,
            PlaybackIsoBmffDecoderConfigDescriptorParser.Parse(
                ElementaryStream(DecoderConfigPayload(
                    Descriptor(0x05, [1]))),
                out var decoderConfig));

        Assert.False(decoderConfig!.TryGetPayload(null, out var missing));
        Assert.True(missing.IsEmpty);
        Assert.False(decoderConfig.TryGetPayload(
            new(new(0x05, 2, 1), 0),
            out var foreign));
        Assert.True(foreign.IsEmpty);
    }

    private static byte[] DecoderConfigPayload(params byte[][] children)
    {
        var payload = new byte[13 + children.Sum(child => child.Length)];
        payload[0] = 0x40;
        payload[1] = (byte)((5 << 2) | 1);
        payload[2] = 0x01;
        payload[3] = 0x02;
        payload[4] = 0x03;
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(5, 4), 128_000);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(9, 4), 96_000);
        var offset = 13;
        foreach (var child in children)
        {
            child.CopyTo(payload, offset);
            offset += child.Length;
        }

        return payload;
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

    private static byte[] Descriptor(byte tag, byte[] payload)
    {
        Assert.True(payload.Length < 128);
        return [tag, (byte)payload.Length, .. payload];
    }

    private static PlaybackIsoBmffBoxLocation BoxLocation(uint type) =>
        new(new(type, 8, 8, null), 0, 8);
}
