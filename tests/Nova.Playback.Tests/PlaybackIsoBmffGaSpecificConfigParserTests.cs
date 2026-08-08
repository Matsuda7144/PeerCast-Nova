namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffGaSpecificConfigParserTests
{
    [Fact]
    public void Parse_ProjectsAacLowComplexityFlags()
    {
        var header = Header(Bits((2, 5), (4, 4), (2, 4), (0, 3)));

        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigParseResult.Parsed,
            PlaybackIsoBmffGaSpecificConfigParser.Parse(header, out var config));
        var parsed = Assert.IsType<PlaybackIsoBmffGaSpecificConfig>(config);
        Assert.Same(header, parsed.AudioSpecificConfigHeader);
        Assert.Null(parsed.ExplicitSbrPsExtension);
        Assert.Equal((byte)2, parsed.AudioObjectType);
        Assert.False(parsed.FrameLengthFlag);
        Assert.False(parsed.DependsOnCoreCoder);
        Assert.Null(parsed.CoreCoderDelay);
        Assert.False(parsed.ExtensionFlag);
        Assert.Null(parsed.ExtensionFlag3);
        Assert.Equal(16, parsed.ConsumedBitLength);
    }

    [Fact]
    public void Parse_ProjectsCoreCoderDelay()
    {
        var header = Header(Bits(
            (2, 5), (4, 4), (2, 4),
            (1, 1), (1, 1), (0x1234, 14), (0, 1)));

        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigParseResult.Parsed,
            PlaybackIsoBmffGaSpecificConfigParser.Parse(header, out var config));
        Assert.True(config!.FrameLengthFlag);
        Assert.True(config.DependsOnCoreCoder);
        Assert.Equal((ushort)0x1234, config.CoreCoderDelay);
        Assert.Equal(30, config.ConsumedBitLength);
    }

    [Fact]
    public void Parse_ProjectsLayerNumberForScalableObjectType()
    {
        var header = Header(Bits(
            (6, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (5, 3)));

        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigParseResult.Parsed,
            PlaybackIsoBmffGaSpecificConfigParser.Parse(header, out var config));
        Assert.Equal((byte)5, config!.LayerNumber);
        Assert.Equal(19, config.ConsumedBitLength);
    }

    [Fact]
    public void Parse_ProjectsErBsacExtensionFields()
    {
        var header = Header(Bits(
            (22, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (1, 1),
            (17, 5), (0x345, 11), (1, 1)));

        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigParseResult.Parsed,
            PlaybackIsoBmffGaSpecificConfigParser.Parse(header, out var config));
        Assert.True(config!.ExtensionFlag);
        Assert.Equal((byte)17, config.NumberOfSubFrames);
        Assert.Equal((ushort)0x345, config.LayerLength);
        Assert.True(config.ExtensionFlag3);
        Assert.Equal(33, config.ConsumedBitLength);
    }

    [Fact]
    public void Parse_ProjectsErrorResilienceFlags()
    {
        var header = Header(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (1, 1),
            (1, 1), (0, 1), (1, 1), (0, 1)));

        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigParseResult.Parsed,
            PlaybackIsoBmffGaSpecificConfigParser.Parse(header, out var config));
        Assert.True(config!.SectionDataResilienceFlag);
        Assert.False(config.ScalefactorDataResilienceFlag);
        Assert.True(config.SpectralDataResilienceFlag);
        Assert.False(config.ExtensionFlag3);
        Assert.Equal(20, config.ConsumedBitLength);
    }

    [Fact]
    public void Parse_MissingHeaderIsDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigParseResult
                .MissingAudioSpecificConfigHeader,
            PlaybackIsoBmffGaSpecificConfigParser.Parse(null, out var config));
        Assert.Null(config);
    }

    [Fact]
    public void ParseExplicitExtension_ProjectsCoreGaConfig()
    {
        var header = Header(Bits(
            (5, 5), (7, 4), (2, 4),
            (4, 4), (2, 5),
            (1, 1), (0, 1), (0, 1)));
        Assert.Equal(
            PlaybackIsoBmffExplicitSbrPsExtensionParseResult.Parsed,
            PlaybackIsoBmffExplicitSbrPsExtensionParser.Parse(
                header,
                out var extension));

        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigParseResult.Parsed,
            PlaybackIsoBmffGaSpecificConfigParser.ParseExplicitExtension(
                extension,
                out var config));
        Assert.Same(header, config!.AudioSpecificConfigHeader);
        Assert.Same(extension, config.ExplicitSbrPsExtension);
        Assert.Equal((byte)2, config.AudioObjectType);
        Assert.True(config.FrameLengthFlag);
        Assert.Equal(25, config.ConsumedBitLength);
    }

    [Fact]
    public void ParseExplicitExtension_MissingExtensionIsDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigParseResult
                .MissingExplicitSbrPsExtension,
            PlaybackIsoBmffGaSpecificConfigParser.ParseExplicitExtension(
                null,
                out var config));
        Assert.Null(config);
    }

    [Fact]
    public void ParseExplicitExtension_NonGeneralCoreTypeIsUnsupported()
    {
        var header = Header(Bits(
            (5, 5), (7, 4), (2, 4),
            (4, 4), (31, 5), (10, 6)));
        Assert.Equal(
            PlaybackIsoBmffExplicitSbrPsExtensionParseResult.Parsed,
            PlaybackIsoBmffExplicitSbrPsExtensionParser.Parse(
                header,
                out var extension));

        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigParseResult.UnsupportedAudioObjectType,
            PlaybackIsoBmffGaSpecificConfigParser.ParseExplicitExtension(
                extension,
                out var config));
        Assert.Null(config);
    }

    [Fact]
    public void Parse_NonGeneralAudioObjectTypeIsUnsupported()
    {
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigParseResult.UnsupportedAudioObjectType,
            PlaybackIsoBmffGaSpecificConfigParser.Parse(
                Header(Bits((5, 5), (4, 4), (2, 4))),
                out var config));
        Assert.Null(config);
    }

    [Fact]
    public void Parse_ChannelConfigurationZeroRequiresProgramConfigElement()
    {
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigParseResult
                .UnsupportedProgramConfigElement,
            PlaybackIsoBmffGaSpecificConfigParser.Parse(
                Header(Bits((2, 5), (4, 4), (0, 4))),
                out var config));
        Assert.Null(config);
    }

    [Fact]
    public void Parse_TruncatedGaSpecificConfigNeedsMoreData()
    {
        byte[][] configurations =
        {
            Bits((2, 5), (4, 4), (2, 4), (0, 1), (1, 1)),
            Bits((6, 5), (4, 4), (2, 4), (0, 3)),
            Bits((22, 5), (4, 4), (2, 4), (0, 1), (0, 1), (1, 1)),
            Bits((17, 5), (4, 4), (2, 4), (0, 1), (0, 1), (1, 1)),
        };

        foreach (var configuration in configurations)
        {
            Assert.Equal(
                PlaybackIsoBmffGaSpecificConfigParseResult.NeedMoreData,
                PlaybackIsoBmffGaSpecificConfigParser.Parse(
                    Header(configuration),
                    out var config));
            Assert.Null(config);
        }
    }

    [Fact]
    public void ParsePrefix_ProjectsDirectFlagsAndProgramConfigRequirement()
    {
        var source = Header(Bits(
            (2, 5), (4, 4), (0, 4),
            (1, 1), (1, 1), (0x1234, 14), (1, 1)));

        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigPrefixParseResult.Parsed,
            PlaybackIsoBmffGaSpecificConfigPrefixParser.Parse(
                source,
                out var prefix));

        var parsed = Assert.IsType<PlaybackIsoBmffGaSpecificConfigPrefix>(
            prefix);
        Assert.Same(source, parsed.AudioSpecificConfigHeader);
        Assert.Null(parsed.ExplicitSbrPsExtension);
        Assert.Equal(2, parsed.AudioObjectType);
        Assert.True(parsed.FrameLengthFlag);
        Assert.True(parsed.DependsOnCoreCoder);
        Assert.Equal((ushort)0x1234, parsed.CoreCoderDelay);
        Assert.True(parsed.ExtensionFlag);
        Assert.True(parsed.RequiresProgramConfigElement);
        Assert.Equal(30, parsed.ConsumedBitLength);
    }

    [Fact]
    public void ParsePrefix_ProjectsExplicitCoreBoundary()
    {
        var source = Header(Bits(
            (5, 5), (7, 4), (2, 4),
            (4, 4), (2, 5),
            (0, 1), (0, 1), (0, 1)));
        Assert.Equal(
            PlaybackIsoBmffExplicitSbrPsExtensionParseResult.Parsed,
            PlaybackIsoBmffExplicitSbrPsExtensionParser.Parse(
                source,
                out var extension));

        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigPrefixParseResult.Parsed,
            PlaybackIsoBmffGaSpecificConfigPrefixParser.ParseExplicitExtension(
                extension,
                out var prefix));

        var parsed = Assert.IsType<PlaybackIsoBmffGaSpecificConfigPrefix>(
            prefix);
        Assert.Same(extension, parsed.ExplicitSbrPsExtension);
        Assert.Equal(2, parsed.AudioObjectType);
        Assert.False(parsed.FrameLengthFlag);
        Assert.False(parsed.DependsOnCoreCoder);
        Assert.Null(parsed.CoreCoderDelay);
        Assert.False(parsed.ExtensionFlag);
        Assert.False(parsed.RequiresProgramConfigElement);
        Assert.Equal(25, parsed.ConsumedBitLength);
    }

    [Fact]
    public void ParsePrefix_MissingInputsAreDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigPrefixParseResult
                .MissingAudioSpecificConfigHeader,
            PlaybackIsoBmffGaSpecificConfigPrefixParser.Parse(
                null,
                out var direct));
        Assert.Null(direct);
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigPrefixParseResult
                .MissingExplicitSbrPsExtension,
            PlaybackIsoBmffGaSpecificConfigPrefixParser.ParseExplicitExtension(
                null,
                out var explicitPrefix));
        Assert.Null(explicitPrefix);
    }

    [Fact]
    public void ParsePrefix_UnsupportedTypeAndTruncationAreDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigPrefixParseResult
                .UnsupportedAudioObjectType,
            PlaybackIsoBmffGaSpecificConfigPrefixParser.Parse(
                Header(Bits((5, 5), (4, 4), (2, 4))),
                out var unsupported));
        Assert.Null(unsupported);

        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigPrefixParseResult.NeedMoreData,
            PlaybackIsoBmffGaSpecificConfigPrefixParser.Parse(
                Header(Bits(
                    (2, 5), (4, 4), (0, 4),
                    (0, 1), (1, 1))),
                out var truncated));
        Assert.Null(truncated);
    }

    [Fact]
    public void ParseProgramConfigHeader_ProjectsFixedFields()
    {
        var source = Header(Bits(
            (2, 5), (4, 4), (0, 4),
            (0, 1), (0, 1), (0, 1),
            (3, 4), (1, 2), (4, 4),
            (2, 4), (1, 4), (3, 4),
            (2, 2), (5, 3), (7, 4)));
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigPrefixParseResult.Parsed,
            PlaybackIsoBmffGaSpecificConfigPrefixParser.Parse(
                source,
                out var prefix));

        Assert.Equal(
            PlaybackIsoBmffProgramConfigElementHeaderParseResult.Parsed,
            PlaybackIsoBmffProgramConfigElementHeaderParser.Parse(
                prefix,
                out var header));

        var parsed =
            Assert.IsType<PlaybackIsoBmffProgramConfigElementHeader>(header);
        Assert.Same(prefix, parsed.GaSpecificConfigPrefix);
        Assert.Equal(3, parsed.ElementInstanceTag);
        Assert.Equal(1, parsed.ObjectType);
        Assert.Equal(4, parsed.SamplingFrequencyIndex);
        Assert.Equal(2, parsed.FrontChannelElementCount);
        Assert.Equal(1, parsed.SideChannelElementCount);
        Assert.Equal(3, parsed.BackChannelElementCount);
        Assert.Equal(2, parsed.LfeChannelElementCount);
        Assert.Equal(5, parsed.AssociatedDataElementCount);
        Assert.Equal(7, parsed.ValidCcElementCount);
        Assert.Equal(47, parsed.ConsumedBitLength);
    }

    [Fact]
    public void ParseProgramConfigHeader_MissingAndNotRequiredAreDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffProgramConfigElementHeaderParseResult
                .MissingGaSpecificConfigPrefix,
            PlaybackIsoBmffProgramConfigElementHeaderParser.Parse(
                null,
                out var missing));
        Assert.Null(missing);

        var source = Header(Bits(
            (2, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1)));
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigPrefixParseResult.Parsed,
            PlaybackIsoBmffGaSpecificConfigPrefixParser.Parse(
                source,
                out var prefix));
        Assert.Equal(
            PlaybackIsoBmffProgramConfigElementHeaderParseResult
                .ProgramConfigElementNotRequired,
            PlaybackIsoBmffProgramConfigElementHeaderParser.Parse(
                prefix,
                out var notRequired));
        Assert.Null(notRequired);
    }

    [Theory]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    public void ParseProgramConfigHeader_RejectsReservedFrequencyIndex(
        uint samplingFrequencyIndex)
    {
        var source = Header(Bits(
            (2, 5), (4, 4), (0, 4),
            (0, 1), (0, 1), (0, 1),
            (0, 4), (0, 2), (samplingFrequencyIndex, 4),
            (0, 4), (0, 4), (0, 4),
            (0, 2), (0, 3), (0, 4)));
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigPrefixParseResult.Parsed,
            PlaybackIsoBmffGaSpecificConfigPrefixParser.Parse(
                source,
                out var prefix));

        Assert.Equal(
            PlaybackIsoBmffProgramConfigElementHeaderParseResult
                .InvalidSamplingFrequencyIndex,
            PlaybackIsoBmffProgramConfigElementHeaderParser.Parse(
                prefix,
                out var header));
        Assert.Null(header);
    }

    [Fact]
    public void ParseProgramConfigHeader_TruncationNeedsMoreData()
    {
        var source = Header(Bits(
            (2, 5), (4, 4), (0, 4),
            (0, 1), (0, 1), (0, 1),
            (0, 4), (0, 2), (4, 4), (1, 4)));
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigPrefixParseResult.Parsed,
            PlaybackIsoBmffGaSpecificConfigPrefixParser.Parse(
                source,
                out var prefix));

        Assert.Equal(
            PlaybackIsoBmffProgramConfigElementHeaderParseResult.NeedMoreData,
            PlaybackIsoBmffProgramConfigElementHeaderParser.Parse(
                prefix,
                out var header));
        Assert.Null(header);
    }

    [Fact]
    public void ParseProgramConfigElement_ProjectsVariableBodyAndOwnedComment()
    {
        var bytes = Bits(
            (2, 5), (4, 4), (0, 4), (0, 1), (0, 1), (0, 1),
            (0, 4), (1, 2), (4, 4),
            (1, 4), (1, 4), (1, 4), (1, 2), (1, 3), (1, 4),
            (1, 1), (2, 4), (1, 1), (3, 4),
            (1, 1), (2, 2), (1, 1),
            (1, 1), (4, 4), (0, 1), (5, 4), (1, 1), (6, 4),
            (7, 4), (8, 4), (1, 1), (9, 4),
            (0, 7), (2, 8), (0x41, 8), (0x42, 8));
        var header = ProgramConfigHeader(bytes);

        Assert.Equal(
            PlaybackIsoBmffProgramConfigElementParseResult.Parsed,
            PlaybackIsoBmffProgramConfigElementParser.Parse(
                header,
                out var element));

        var parsed = Assert.IsType<PlaybackIsoBmffProgramConfigElement>(element);
        Assert.Same(header, parsed.Header);
        Assert.Equal((byte)2, parsed.MonoMixdownElementTag);
        Assert.Equal((byte)3, parsed.StereoMixdownElementTag);
        Assert.Equal(2, parsed.MatrixMixdown!.Index);
        Assert.True(parsed.MatrixMixdown.PseudoSurroundEnabled);
        Assert.Equal(new(true, 4), Assert.Single(parsed.FrontChannelElements));
        Assert.Equal(new(false, 5), Assert.Single(parsed.SideChannelElements));
        Assert.Equal(new(true, 6), Assert.Single(parsed.BackChannelElements));
        Assert.Equal(7, Assert.Single(parsed.LfeChannelElementTags));
        Assert.Equal(8, Assert.Single(parsed.AssociatedDataElementTags));
        Assert.Equal(new(true, 9), Assert.Single(parsed.ValidCcElements));
        Assert.Equal(new byte[] { 0x41, 0x42 }, parsed.CommentBytes.ToArray());
        Assert.Equal(120, parsed.ConsumedBitLength);

        bytes[^1] = 0;
        Assert.Equal(new byte[] { 0x41, 0x42 }, parsed.CommentBytes.ToArray());
    }

    [Fact]
    public void ParseProgramConfigElement_ProjectsAbsentOptionsAndEmptyLists()
    {
        var header = ProgramConfigHeader(Bits(
            (2, 5), (4, 4), (0, 4), (0, 1), (0, 1), (0, 1),
            (0, 4), (1, 2), (4, 4),
            (0, 4), (0, 4), (0, 4), (0, 2), (0, 3), (0, 4),
            (0, 1), (0, 1), (0, 1), (0, 6), (0, 8)));

        Assert.Equal(
            PlaybackIsoBmffProgramConfigElementParseResult.Parsed,
            PlaybackIsoBmffProgramConfigElementParser.Parse(header, out var element));
        var parsed = Assert.IsType<PlaybackIsoBmffProgramConfigElement>(element);
        Assert.Null(parsed.MonoMixdownElementTag);
        Assert.Null(parsed.StereoMixdownElementTag);
        Assert.Null(parsed.MatrixMixdown);
        Assert.Empty(parsed.FrontChannelElements);
        Assert.Empty(parsed.CommentBytes.ToArray());
        Assert.Equal(64, parsed.ConsumedBitLength);
    }

    [Fact]
    public void ParseProgramConfigElement_RejectsNonZeroAlignmentBits()
    {
        var header = ProgramConfigHeader(Bits(
            (2, 5), (4, 4), (0, 4), (0, 1), (0, 1), (0, 1),
            (0, 4), (1, 2), (4, 4),
            (0, 4), (0, 4), (0, 4), (0, 2), (0, 3), (0, 4),
            (0, 1), (0, 1), (0, 1), (1, 6), (0, 8)));

        Assert.Equal(
            PlaybackIsoBmffProgramConfigElementParseResult.NonZeroAlignmentBits,
            PlaybackIsoBmffProgramConfigElementParser.Parse(header, out var element));
        Assert.Null(element);
    }

    [Fact]
    public void ParseProgramConfigElement_MissingAndTruncatedAreDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffProgramConfigElementParseResult.MissingHeader,
            PlaybackIsoBmffProgramConfigElementParser.Parse(null, out var missing));
        Assert.Null(missing);

        var header = ProgramConfigHeader(Bits(
            (2, 5), (4, 4), (0, 4), (0, 1), (0, 1), (0, 1),
            (0, 4), (1, 2), (4, 4),
            (0, 4), (0, 4), (0, 4), (0, 2), (0, 3), (0, 4),
            (0, 1), (0, 1), (0, 1), (0, 6), (2, 8), (0x41, 8)));
        Assert.Equal(
            PlaybackIsoBmffProgramConfigElementParseResult.NeedMoreData,
            PlaybackIsoBmffProgramConfigElementParser.Parse(header, out var truncated));
        Assert.Null(truncated);
    }

    [Fact]
    public void ProjectProgramConfigTopology_CountsChannelsWithoutSpeakerInference()
    {
        var element = ProgramConfigElement(Bits(
            (2, 5), (4, 4), (0, 4), (0, 1), (0, 1), (0, 1),
            (0, 4), (1, 2), (4, 4),
            (1, 4), (1, 4), (1, 4), (1, 2), (0, 3), (0, 4),
            (0, 1), (0, 1), (0, 1),
            (1, 1), (1, 4), (0, 1), (2, 4), (1, 1), (3, 4),
            (4, 4), (0, 4), (0, 8)));

        Assert.Equal(
            PlaybackIsoBmffProgramConfigChannelTopologyResult.Projected,
            PlaybackIsoBmffProgramConfigChannelTopologyProjector.Project(
                element,
                out var topology));
        var projected =
            Assert.IsType<PlaybackIsoBmffProgramConfigChannelTopology>(topology);
        Assert.Same(element, projected.ProgramConfigElement);
        Assert.Equal(1, projected.SingleChannelElementCount);
        Assert.Equal(2, projected.ChannelPairElementCount);
        Assert.Equal(2, projected.FrontChannelCount);
        Assert.Equal(1, projected.SideChannelCount);
        Assert.Equal(2, projected.BackChannelCount);
        Assert.Equal(1, projected.LfeChannelCount);
        Assert.Equal(5, projected.CoreChannelCount);
        Assert.Equal(6, projected.TotalChannelCount);
    }

    [Fact]
    public void ProjectProgramConfigTopology_MissingAndDuplicateChannelsAreDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffProgramConfigChannelTopologyResult
                .MissingProgramConfigElement,
            PlaybackIsoBmffProgramConfigChannelTopologyProjector.Project(
                null,
                out var missing));
        Assert.Null(missing);

        var element = ProgramConfigElement(Bits(
            (2, 5), (4, 4), (0, 4), (0, 1), (0, 1), (0, 1),
            (0, 4), (1, 2), (4, 4),
            (1, 4), (1, 4), (0, 4), (0, 2), (0, 3), (0, 4),
            (0, 1), (0, 1), (0, 1),
            (0, 1), (2, 4), (0, 1), (2, 4),
            (0, 4), (0, 8)));
        Assert.Equal(
            PlaybackIsoBmffProgramConfigChannelTopologyResult
                .DuplicateChannelElementIdentity,
            PlaybackIsoBmffProgramConfigChannelTopologyProjector.Project(
                element,
                out var duplicate));
        Assert.Null(duplicate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ProjectProgramConfigTopology_RejectsDuplicateNonChannelTags(
        int kind)
    {
        var element = kind switch
        {
            0 => ProgramConfigElement(Bits(
                (2, 5), (4, 4), (0, 4), (0, 1), (0, 1), (0, 1),
                (0, 4), (1, 2), (4, 4),
                (0, 4), (0, 4), (0, 4), (2, 2), (0, 3), (0, 4),
                (0, 1), (0, 1), (0, 1), (3, 4), (3, 4),
                (0, 6), (0, 8))),
            1 => ProgramConfigElement(Bits(
                (2, 5), (4, 4), (0, 4), (0, 1), (0, 1), (0, 1),
                (0, 4), (1, 2), (4, 4),
                (0, 4), (0, 4), (0, 4), (0, 2), (2, 3), (0, 4),
                (0, 1), (0, 1), (0, 1), (3, 4), (3, 4),
                (0, 6), (0, 8))),
            _ => ProgramConfigElement(Bits(
                (2, 5), (4, 4), (0, 4), (0, 1), (0, 1), (0, 1),
                (0, 4), (1, 2), (4, 4),
                (0, 4), (0, 4), (0, 4), (0, 2), (0, 3), (2, 4),
                (0, 1), (0, 1), (0, 1),
                (0, 1), (3, 4), (1, 1), (3, 4),
                (0, 4), (0, 8))),
        };
        var expected = kind switch
        {
            0 => PlaybackIsoBmffProgramConfigChannelTopologyResult
                .DuplicateLfeElementTag,
            1 => PlaybackIsoBmffProgramConfigChannelTopologyResult
                .DuplicateAssociatedDataElementTag,
            _ => PlaybackIsoBmffProgramConfigChannelTopologyResult
                .DuplicateCouplingElementTag,
        };

        Assert.Equal(
            expected,
            PlaybackIsoBmffProgramConfigChannelTopologyProjector.Project(
                element,
                out var topology));
        Assert.Null(topology);
    }

    [Fact]
    public void MapProgramConfigChannelSlots_PreservesRegionAndElementOrder()
    {
        var element = ProgramConfigElement(Bits(
            (2, 5), (4, 4), (0, 4), (0, 1), (0, 1), (0, 1),
            (0, 4), (1, 2), (4, 4),
            (1, 4), (1, 4), (1, 4), (1, 2), (0, 3), (0, 4),
            (0, 1), (0, 1), (0, 1),
            (1, 1), (1, 4), (0, 1), (2, 4), (1, 1), (3, 4),
            (4, 4), (0, 4), (0, 8)));
        Assert.Equal(
            PlaybackIsoBmffProgramConfigChannelTopologyResult.Projected,
            PlaybackIsoBmffProgramConfigChannelTopologyProjector.Project(
                element, out var topology));

        Assert.Equal(
            PlaybackIsoBmffProgramConfigChannelSlotMappingResult.Mapped,
            PlaybackIsoBmffProgramConfigChannelSlotMapper.Map(
                topology, out var map));
        var mapped =
            Assert.IsType<PlaybackIsoBmffProgramConfigChannelSlotMap>(map);
        Assert.Same(topology, mapped.Topology);
        Assert.Collection(
            mapped.Slots,
            static slot => Assert.Equal(
                new(0, PlaybackIsoBmffProgramConfigChannelRegion.Front, 0, 1,
                    PlaybackIsoBmffProgramConfigChannelComponent.PairFirst), slot),
            static slot => Assert.Equal(
                new(1, PlaybackIsoBmffProgramConfigChannelRegion.Front, 0, 1,
                    PlaybackIsoBmffProgramConfigChannelComponent.PairSecond), slot),
            static slot => Assert.Equal(
                new(2, PlaybackIsoBmffProgramConfigChannelRegion.Side, 0, 2,
                    PlaybackIsoBmffProgramConfigChannelComponent.SingleChannel), slot),
            static slot => Assert.Equal(
                new(3, PlaybackIsoBmffProgramConfigChannelRegion.Back, 0, 3,
                    PlaybackIsoBmffProgramConfigChannelComponent.PairFirst), slot),
            static slot => Assert.Equal(
                new(4, PlaybackIsoBmffProgramConfigChannelRegion.Back, 0, 3,
                    PlaybackIsoBmffProgramConfigChannelComponent.PairSecond), slot),
            static slot => Assert.Equal(
                new(5, PlaybackIsoBmffProgramConfigChannelRegion.Lfe, 0, 4,
                    PlaybackIsoBmffProgramConfigChannelComponent.Lfe), slot));
    }

    [Fact]
    public void MapProgramConfigChannelSlots_MissingAndInconsistentFailClosed()
    {
        Assert.Equal(
            PlaybackIsoBmffProgramConfigChannelSlotMappingResult.MissingTopology,
            PlaybackIsoBmffProgramConfigChannelSlotMapper.Map(
                null, out var missing));
        Assert.Null(missing);

        var element = ProgramConfigElement(Bits(
            (2, 5), (4, 4), (0, 4), (0, 1), (0, 1), (0, 1),
            (0, 4), (1, 2), (4, 4),
            (1, 4), (0, 4), (0, 4), (0, 2), (0, 3), (0, 4),
            (0, 1), (0, 1), (0, 1),
            (0, 1), (1, 4), (0, 4), (0, 8)));
        var inconsistent = new PlaybackIsoBmffProgramConfigChannelTopology(
            element,
            singleChannelElementCount: 1,
            channelPairElementCount: 0,
            frontChannelCount: 2,
            sideChannelCount: 0,
            backChannelCount: 0,
            lfeChannelCount: 0);

        Assert.Equal(
            PlaybackIsoBmffProgramConfigChannelSlotMappingResult
                .InconsistentTopology,
            PlaybackIsoBmffProgramConfigChannelSlotMapper.Map(
                inconsistent, out var rejected));
        Assert.Null(rejected);
    }

    [Fact]
    public void AssignProgramConfigSpeakerPositions_RetainsExactSlotsAndOrder()
    {
        var map = ProgramConfigChannelSlotMap();
        var positions = new[]
        {
            PlaybackIsoBmffProgramConfigSpeakerPosition.FrontLeft,
            PlaybackIsoBmffProgramConfigSpeakerPosition.FrontRight,
            PlaybackIsoBmffProgramConfigSpeakerPosition.SideLeft,
            PlaybackIsoBmffProgramConfigSpeakerPosition.BackLeft,
            PlaybackIsoBmffProgramConfigSpeakerPosition.BackRight,
            PlaybackIsoBmffProgramConfigSpeakerPosition.LowFrequency,
        };

        Assert.Equal(
            PlaybackIsoBmffProgramConfigSpeakerPositionAssignmentResult.Assigned,
            PlaybackIsoBmffProgramConfigSpeakerPositionAssigner.Assign(
                map, positions, out var assignment));
        var assigned = Assert.IsType<
            PlaybackIsoBmffProgramConfigSpeakerPositionAssignment>(assignment);
        Assert.Same(map, assigned.SlotMap);
        Assert.Equal(positions, assigned.Bindings.Select(binding => binding.Position));
        for (var index = 0; index < map.Slots.Count; index++)
        {
            Assert.Same(map.Slots[index], assigned.Bindings[index].Slot);
        }
    }

    [Fact]
    public void AssignProgramConfigSpeakerPositions_RequiresMapAndExactCount()
    {
        Assert.Equal(
            PlaybackIsoBmffProgramConfigSpeakerPositionAssignmentResult
                .MissingSlotMap,
            PlaybackIsoBmffProgramConfigSpeakerPositionAssigner.Assign(
                null, [], out var missingMap));
        Assert.Null(missingMap);

        var map = ProgramConfigChannelSlotMap();
        Assert.Equal(
            PlaybackIsoBmffProgramConfigSpeakerPositionAssignmentResult
                .MissingPositions,
            PlaybackIsoBmffProgramConfigSpeakerPositionAssigner.Assign(
                map, null, out var missingPositions));
        Assert.Null(missingPositions);
        Assert.Equal(
            PlaybackIsoBmffProgramConfigSpeakerPositionAssignmentResult
                .PositionCountMismatch,
            PlaybackIsoBmffProgramConfigSpeakerPositionAssigner.Assign(
                map, [], out var wrongCount));
        Assert.Null(wrongCount);
    }

    [Fact]
    public void AssignProgramConfigSpeakerPositions_RejectsUndefinedAndDuplicate()
    {
        var map = ProgramConfigChannelSlotMap();
        var valid = new[]
        {
            PlaybackIsoBmffProgramConfigSpeakerPosition.FrontLeft,
            PlaybackIsoBmffProgramConfigSpeakerPosition.FrontRight,
            PlaybackIsoBmffProgramConfigSpeakerPosition.SideLeft,
            PlaybackIsoBmffProgramConfigSpeakerPosition.BackLeft,
            PlaybackIsoBmffProgramConfigSpeakerPosition.BackRight,
            PlaybackIsoBmffProgramConfigSpeakerPosition.LowFrequency,
        };
        var undefined = valid.ToArray();
        undefined[0] = (PlaybackIsoBmffProgramConfigSpeakerPosition)int.MaxValue;
        Assert.Equal(
            PlaybackIsoBmffProgramConfigSpeakerPositionAssignmentResult
                .UndefinedPosition,
            PlaybackIsoBmffProgramConfigSpeakerPositionAssigner.Assign(
                map, undefined, out var undefinedAssignment));
        Assert.Null(undefinedAssignment);

        var duplicate = valid.ToArray();
        duplicate[1] = duplicate[0];
        Assert.Equal(
            PlaybackIsoBmffProgramConfigSpeakerPositionAssignmentResult
                .DuplicatePosition,
            PlaybackIsoBmffProgramConfigSpeakerPositionAssigner.Assign(
                map, duplicate, out var duplicateAssignment));
        Assert.Null(duplicateAssignment);
    }

    [Fact]
    public void AssignProgramConfigSpeakerPositions_RequiresExactLfeAgreement()
    {
        var map = ProgramConfigChannelSlotMap();
        var lowFrequencyOnFront = new[]
        {
            PlaybackIsoBmffProgramConfigSpeakerPosition.LowFrequency,
            PlaybackIsoBmffProgramConfigSpeakerPosition.FrontRight,
            PlaybackIsoBmffProgramConfigSpeakerPosition.SideLeft,
            PlaybackIsoBmffProgramConfigSpeakerPosition.BackLeft,
            PlaybackIsoBmffProgramConfigSpeakerPosition.BackRight,
            PlaybackIsoBmffProgramConfigSpeakerPosition.FrontLeft,
        };

        Assert.Equal(
            PlaybackIsoBmffProgramConfigSpeakerPositionAssignmentResult
                .LowFrequencyMismatch,
            PlaybackIsoBmffProgramConfigSpeakerPositionAssigner.Assign(
                map, lowFrequencyOnFront, out var rejected));
        Assert.Null(rejected);
    }

    [Fact]
    public void ProjectProgramConfigWaveFormatLayout_MapsMaskAndPlatformOrder()
    {
        var assignment = ProgramConfigSpeakerPositionAssignment();

        Assert.Equal(
            PlaybackIsoBmffProgramConfigWaveFormatChannelLayoutResult.Projected,
            PlaybackIsoBmffProgramConfigWaveFormatChannelLayoutProjector.Project(
                assignment, out var layout));
        var projected = Assert.IsType<
            PlaybackIsoBmffProgramConfigWaveFormatChannelLayout>(layout);
        Assert.Same(assignment, projected.Assignment);
        Assert.Equal(
            PlaybackIsoBmffWaveFormatSpeakerMask.FrontLeft |
            PlaybackIsoBmffWaveFormatSpeakerMask.FrontRight |
            PlaybackIsoBmffWaveFormatSpeakerMask.LowFrequency |
            PlaybackIsoBmffWaveFormatSpeakerMask.BackLeft |
            PlaybackIsoBmffWaveFormatSpeakerMask.BackRight |
            PlaybackIsoBmffWaveFormatSpeakerMask.SideLeft,
            projected.ChannelMask);
        Assert.Equal([0, 1, 5, 3, 4, 2],
            projected.Bindings.Select(binding => binding.SourceChannelIndex));
        Assert.Equal(Enumerable.Range(0, 6),
            projected.Bindings.Select(binding => binding.PlatformChannelIndex));
    }

    [Fact]
    public void ProjectProgramConfigWaveFormatLayout_RequiresAssignment()
    {
        Assert.Equal(
            PlaybackIsoBmffProgramConfigWaveFormatChannelLayoutResult
                .MissingAssignment,
            PlaybackIsoBmffProgramConfigWaveFormatChannelLayoutProjector.Project(
                null, out var layout));
        Assert.Null(layout);
    }

    [Fact]
    public void ProjectProgramConfigWaveFormatLayout_RejectsInconsistentSource()
    {
        var assignment = ProgramConfigSpeakerPositionAssignment();
        var bindings = assignment.Bindings.ToArray();
        bindings[0] = bindings[0] with { Slot = assignment.SlotMap.Slots[1] };
        var inconsistent = new
            PlaybackIsoBmffProgramConfigSpeakerPositionAssignment(
                assignment.SlotMap, Array.AsReadOnly(bindings));

        Assert.Equal(
            PlaybackIsoBmffProgramConfigWaveFormatChannelLayoutResult
                .InconsistentAssignment,
            PlaybackIsoBmffProgramConfigWaveFormatChannelLayoutProjector.Project(
                inconsistent, out var layout));
        Assert.Null(layout);
    }

    [Fact]
    public void PlanProgramConfigChannelReorder_RetainsExactPermutation()
    {
        var layout = ProgramConfigWaveFormatChannelLayout();

        Assert.Equal(
            PlaybackIsoBmffProgramConfigChannelReorderPlanningResult.Planned,
            PlaybackIsoBmffProgramConfigChannelReorderPlanner.Plan(
                layout, out var plan));
        var planned = Assert.IsType<
            PlaybackIsoBmffProgramConfigChannelReorderPlan>(plan);
        Assert.Same(layout, planned.Layout);
        Assert.True(planned.RequiresReordering);
        Assert.Equal([0, 1, 5, 3, 4, 2],
            planned.Channels.Select(channel => channel.SourceChannelIndex));
        Assert.Equal(Enumerable.Range(0, 6),
            planned.Channels.Select(channel => channel.DestinationChannelIndex));
        for (var index = 0; index < layout.Bindings.Count; index++)
        {
            Assert.Same(layout.Bindings[index],
                planned.Channels[index].LayoutBinding);
        }
    }

    [Fact]
    public void PlanProgramConfigChannelReorder_RejectsMissingAndInconsistent()
    {
        Assert.Equal(
            PlaybackIsoBmffProgramConfigChannelReorderPlanningResult
                .MissingLayout,
            PlaybackIsoBmffProgramConfigChannelReorderPlanner.Plan(
                null, out var missing));
        Assert.Null(missing);

        var layout = ProgramConfigWaveFormatChannelLayout();
        var bindings = layout.Bindings.ToArray();
        bindings[0] = bindings[0] with { SourceChannelIndex = 1 };
        var inconsistent = new
            PlaybackIsoBmffProgramConfigWaveFormatChannelLayout(
                layout.Assignment,
                layout.ChannelMask,
                Array.AsReadOnly(bindings));
        Assert.Equal(
            PlaybackIsoBmffProgramConfigChannelReorderPlanningResult
                .InconsistentLayout,
            PlaybackIsoBmffProgramConfigChannelReorderPlanner.Plan(
                inconsistent, out var rejected));
        Assert.Null(rejected);
    }

    [Fact]
    public void ComposeProgramConfigAacPreparation_RetainsExactPlanAndTopology()
    {
        var plan = ProgramConfigChannelReorderPlan();
        var intent = ProgramConfigFormatIntent(plan);

        Assert.Equal(
            PlaybackIsoBmffProgramConfigAacManagedDecoderPreparationResult
                .Prepared,
            PlaybackIsoBmffProgramConfigAacManagedDecoderPreparationComposer
                .Compose(
                    plan,
                    intent,
                    PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols,
                    new(PlaybackDecoderFfmpegCodecId.Aac, 8_000, 96_000, 6),
                    out var preparation));
        using var owned = Assert.IsType<
            PlaybackIsoBmffProgramConfigAacManagedDecoderPreparation>(
                preparation);
        Assert.Same(plan, owned.ReorderPlan);
        Assert.Same(intent, owned.FormatIntent);
        Assert.Same(
            plan.Layout.Assignment.SlotMap.Topology,
            owned.FormatIntent.ExtensionSignallingFacts.CoreFormatFacts
                .ProgramConfigTopology);
    }

    [Fact]
    public void ComposeProgramConfigAacPreparation_RejectsMissingAndMismatch()
    {
        var declaration = new PlaybackIsoBmffAacDecoderCapabilityDeclaration(
            PlaybackDecoderFfmpegCodecId.Aac, 8_000, 96_000, 6);
        Assert.Equal(
            PlaybackIsoBmffProgramConfigAacManagedDecoderPreparationResult
                .MissingReorderPlan,
            PlaybackIsoBmffProgramConfigAacManagedDecoderPreparationComposer
                .Compose(
                    null,
                    PlainFormatIntent(),
                    PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols,
                    declaration,
                    out var missing));
        Assert.Null(missing);

        Assert.Equal(
            PlaybackIsoBmffProgramConfigAacManagedDecoderPreparationResult
                .TopologySourceMismatch,
            PlaybackIsoBmffProgramConfigAacManagedDecoderPreparationComposer
                .Compose(
                    ProgramConfigChannelReorderPlan(),
                    PlainFormatIntent(),
                    PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols,
                    declaration,
                    out var mismatch));
        Assert.Null(mismatch);
    }

    [Fact]
    public void ComposeProgramConfigAacPreparation_RejectsDecoderPreparation()
    {
        var plan = ProgramConfigChannelReorderPlan();
        Assert.Equal(
            PlaybackIsoBmffProgramConfigAacManagedDecoderPreparationResult
                .DecoderPreparationRejected,
            PlaybackIsoBmffProgramConfigAacManagedDecoderPreparationComposer
                .Compose(
                    plan,
                    ProgramConfigFormatIntent(plan),
                    Array.AsReadOnly(["avcodec_parameters_alloc"]),
                    new(PlaybackDecoderFfmpegCodecId.Aac, 8_000, 96_000, 6),
                    out var rejected));
        Assert.Null(rejected);
    }

    [Fact]
    public void ReorderProgramConfigInterleavedPcm_CopiesExactChannelPermutation()
    {
        var plan = ProgramConfigChannelReorderPlan();
        var bytes = new byte[2 * 6 * 2];
        for (var sample = 0; sample < 2; sample++)
        {
            for (var channel = 0; channel < 6; channel++)
            {
                var offset = (sample * 6 + channel) * 2;
                bytes[offset] = (byte)(sample * 10 + channel);
                bytes[offset + 1] = (byte)(0xA0 + sample * 10 + channel);
            }
        }

        var source = PlaybackDecodedAudioFrame.Create(
            7,
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(40),
            PlaybackDecodedFrameDiscontinuityKind.DecoderReset,
            PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
            48_000,
            6,
            2,
            bytes);
        Assert.Equal(
            PlaybackIsoBmffProgramConfigInterleavedPcmReorderResult.Reordered,
            PlaybackIsoBmffProgramConfigInterleavedPcmReorderer.Reorder(
                plan, source, out var reordered));
        var owned = Assert.IsType<
            PlaybackIsoBmffProgramConfigReorderedAudioFrame>(reordered);
        Assert.Same(plan, owned.Plan);
        Assert.Equal(source, owned.SourceFrame);
        Assert.Equal(source.Sequence, owned.Frame.Sequence);
        Assert.Equal(source.Timestamp, owned.Frame.Timestamp);
        Assert.Equal(source.Duration, owned.Frame.Duration);
        Assert.Equal(source.Discontinuity, owned.Frame.Discontinuity);
        Assert.Equal(
            new byte[]
            {
                0, 0xA0, 1, 0xA1, 5, 0xA5,
                3, 0xA3, 4, 0xA4, 2, 0xA2,
                10, 0xAA, 11, 0xAB, 15, 0xAF,
                13, 0xAD, 14, 0xAE, 12, 0xAC,
            },
            owned.Frame.Payload.ToArray());
    }

    [Fact]
    public void ReorderProgramConfigInterleavedPcm_RejectsInputsAndLimit()
    {
        var source = PlaybackDecodedAudioFrame.Create(
            0,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(20),
            PlaybackDecodedFrameDiscontinuityKind.None,
            PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
            48_000,
            2,
            1,
            new byte[4]);
        Assert.Equal(
            PlaybackIsoBmffProgramConfigInterleavedPcmReorderResult.MissingPlan,
            PlaybackIsoBmffProgramConfigInterleavedPcmReorderer.Reorder(
                null, source, out var missing));
        Assert.Null(missing);

        var plan = ProgramConfigChannelReorderPlan();
        Assert.Equal(
            PlaybackIsoBmffProgramConfigInterleavedPcmReorderResult
                .ChannelCountMismatch,
            PlaybackIsoBmffProgramConfigInterleavedPcmReorderer.Reorder(
                plan, source, out var mismatch));
        Assert.Null(mismatch);

        var sixChannel = PlaybackDecodedAudioFrame.Create(
            0,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(20),
            PlaybackDecodedFrameDiscontinuityKind.None,
            PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
            48_000,
            6,
            1,
            new byte[12]);
        Assert.Equal(
            PlaybackIsoBmffProgramConfigInterleavedPcmReorderResult
                .PayloadLimitExceeded,
            PlaybackIsoBmffProgramConfigInterleavedPcmReorderer.Reorder(
                plan,
                sixChannel,
                out var limited,
                new() { MaximumPayloadBytes = 11 }));
        Assert.Null(limited);
    }

    [Fact]
    public void ProjectProgramConfigAacChannelLayoutRequirement_RetainsMask()
    {
        using var preparation = ProgramConfigAacManagedDecoderPreparation();

        Assert.Equal(
            PlaybackIsoBmffProgramConfigAacChannelLayoutRequirementResult
                .Projected,
            PlaybackIsoBmffProgramConfigAacChannelLayoutRequirementProjector
                .Project(preparation, out var requirement));
        var projected = Assert.IsType<
            PlaybackIsoBmffProgramConfigAacChannelLayoutRequirement>(
                requirement);
        Assert.Same(preparation, projected.Preparation);
        Assert.Equal(6, projected.ChannelCount);
        Assert.Equal(0x23Bul, projected.ChannelMask);
        Assert.Equal(
            "av_channel_layout_from_mask",
            PlaybackIsoBmffProgramConfigAacChannelLayoutRequirement
                .InitializationSymbol);
    }

    [Fact]
    public void ProjectProgramConfigAacChannelLayoutRequirement_FailsClosed()
    {
        Assert.Equal(
            PlaybackIsoBmffProgramConfigAacChannelLayoutRequirementResult
                .MissingPreparation,
            PlaybackIsoBmffProgramConfigAacChannelLayoutRequirementProjector
                .Project(null, out var missing));
        Assert.Null(missing);

        var preparation = ProgramConfigAacManagedDecoderPreparation();
        preparation.Dispose();
        Assert.Equal(
            PlaybackIsoBmffProgramConfigAacChannelLayoutRequirementResult
                .PreparationUnavailable,
            PlaybackIsoBmffProgramConfigAacChannelLayoutRequirementProjector
                .Project(preparation, out var unavailable));
        Assert.Null(unavailable);
    }

    [Fact]
    public void VerifyProgramConfigAacChannelLayoutSymbol_RetainsRequirement()
    {
        using var preparation = ProgramConfigAacManagedDecoderPreparation();
        var requirement = ProgramConfigAacChannelLayoutRequirement(preparation);

        Assert.Equal(
            PlaybackIsoBmffProgramConfigAacChannelLayoutSymbolCoverageResult
                .Covered,
            PlaybackIsoBmffProgramConfigAacChannelLayoutSymbolCoverageVerifier
                .Verify(
                    requirement,
                    Array.AsReadOnly(
                    [
                        "av_channel_layout_default",
                        "av_channel_layout_from_mask",
                    ]),
                    out var coverage));
        var covered = Assert.IsType<
            PlaybackIsoBmffProgramConfigAacChannelLayoutSymbolCoverage>(
                coverage);
        Assert.Same(requirement, covered.Requirement);
        Assert.Equal("av_channel_layout_from_mask", covered.CoveredSymbol);
    }

    [Fact]
    public void VerifyProgramConfigAacChannelLayoutSymbol_RejectsDeclarations()
    {
        using var preparation = ProgramConfigAacManagedDecoderPreparation();
        var requirement = ProgramConfigAacChannelLayoutRequirement(preparation);
        Assert.Equal(
            PlaybackIsoBmffProgramConfigAacChannelLayoutSymbolCoverageResult
                .MissingDeclaredSymbols,
            PlaybackIsoBmffProgramConfigAacChannelLayoutSymbolCoverageVerifier
                .Verify(requirement, null, out var missing));
        Assert.Null(missing);
        Assert.Equal(
            PlaybackIsoBmffProgramConfigAacChannelLayoutSymbolCoverageResult
                .InvalidDeclaredSymbol,
            PlaybackIsoBmffProgramConfigAacChannelLayoutSymbolCoverageVerifier
                .Verify(
                    requirement,
                    Array.AsReadOnly(["av channel layout from mask"]),
                    out var invalid));
        Assert.Null(invalid);
        Assert.Equal(
            PlaybackIsoBmffProgramConfigAacChannelLayoutSymbolCoverageResult
                .MissingRequiredSymbol,
            PlaybackIsoBmffProgramConfigAacChannelLayoutSymbolCoverageVerifier
                .Verify(
                    requirement,
                    Array.AsReadOnly(["av_channel_layout_default"]),
                    out var uncovered));
        Assert.Null(uncovered);
    }

    [Fact]
    public void VerifyProgramConfigAacChannelLayoutSymbol_RejectsDisposedSource()
    {
        var preparation = ProgramConfigAacManagedDecoderPreparation();
        var requirement = ProgramConfigAacChannelLayoutRequirement(preparation);
        preparation.Dispose();

        Assert.Equal(
            PlaybackIsoBmffProgramConfigAacChannelLayoutSymbolCoverageResult
                .PreparationUnavailable,
            PlaybackIsoBmffProgramConfigAacChannelLayoutSymbolCoverageVerifier
                .Verify(
                    requirement,
                    Array.AsReadOnly(["av_channel_layout_from_mask"]),
                    out var coverage));
        Assert.Null(coverage);
    }

    [Fact]
    public void ComposeProgramConfigAacExplicitLayoutPreparation_BuildsExactChain()
    {
        var plan = ProgramConfigChannelReorderPlan();

        Assert.Equal(
            PlaybackIsoBmffProgramConfigAacExplicitLayoutPreparationResult
                .Prepared,
            PlaybackIsoBmffProgramConfigAacExplicitLayoutPreparationComposer
                .Compose(
                    plan,
                    ProgramConfigFormatIntent(plan),
                    PlaybackIsoBmffProgramConfigAacExplicitLayoutPreparation
                        .RequiredSymbols,
                    new(PlaybackDecoderFfmpegCodecId.Aac, 8_000, 96_000, 6),
                    out var preparation));
        using var owned = Assert.IsType<
            PlaybackIsoBmffProgramConfigAacExplicitLayoutPreparation>(
                preparation);
        Assert.Same(plan, owned.DecoderPreparation.ReorderPlan);
        Assert.Same(
            owned.DecoderPreparation,
            owned.LayoutRequirement.Preparation);
        Assert.Same(
            owned.LayoutRequirement,
            owned.LayoutSymbolCoverage.Requirement);
        Assert.Equal(0x23Bul, owned.LayoutRequirement.ChannelMask);
        Assert.Equal(
            "av_channel_layout_from_mask",
            owned.LayoutSymbolCoverage.CoveredSymbol);
    }

    [Fact]
    public void ComposeProgramConfigAacExplicitLayoutPreparation_RejectsStages()
    {
        var plan = ProgramConfigChannelReorderPlan();
        var declaration = new PlaybackIsoBmffAacDecoderCapabilityDeclaration(
            PlaybackDecoderFfmpegCodecId.Aac, 8_000, 96_000, 6);
        Assert.Equal(
            PlaybackIsoBmffProgramConfigAacExplicitLayoutPreparationResult
                .DecoderPreparationRejected,
            PlaybackIsoBmffProgramConfigAacExplicitLayoutPreparationComposer
                .Compose(
                    plan,
                    PlainFormatIntent(),
                    PlaybackIsoBmffProgramConfigAacExplicitLayoutPreparation
                        .RequiredSymbols,
                    declaration,
                    out var mismatched));
        Assert.Null(mismatched);

        Assert.Equal(
            PlaybackIsoBmffProgramConfigAacExplicitLayoutPreparationResult
                .SymbolCoverageRejected,
            PlaybackIsoBmffProgramConfigAacExplicitLayoutPreparationComposer
                .Compose(
                    plan,
                    ProgramConfigFormatIntent(plan),
                    PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols,
                    declaration,
                    out var uncovered));
        Assert.Null(uncovered);
    }

    [Fact]
    public async Task ComposeProgramConfigAacExplicitLayoutPreparation_DisposesLease()
    {
        var plan = ProgramConfigChannelReorderPlan();
        Assert.Equal(
            PlaybackIsoBmffProgramConfigAacExplicitLayoutPreparationResult
                .Prepared,
            PlaybackIsoBmffProgramConfigAacExplicitLayoutPreparationComposer
                .Compose(
                    plan,
                    ProgramConfigFormatIntent(plan),
                    PlaybackIsoBmffProgramConfigAacExplicitLayoutPreparation
                        .RequiredSymbols,
                    new(PlaybackDecoderFfmpegCodecId.Aac, 8_000, 96_000, 6),
                    out var preparation));
        var owned = Assert.IsType<
            PlaybackIsoBmffProgramConfigAacExplicitLayoutPreparation>(
                preparation);

        await owned.DisposeAsync();

        Assert.True(owned.DecoderPreparation.DecoderPreparation
            .CapabilityBinding.Plan.ManagedParameters.Lease.IsDisposed);
        owned.Dispose();
    }

    [Fact]
    public void ComposeGaSpecificConfig_ContinuesAfterProgramConfigElement()
    {
        var element = ProgramConfigElement(Bits(
            (20, 5), (4, 4), (0, 4), (0, 1), (0, 1), (1, 1),
            (0, 4), (1, 2), (4, 4),
            (0, 4), (0, 4), (0, 4), (0, 2), (0, 3), (0, 4),
            (0, 1), (0, 1), (0, 1), (0, 6), (0, 8),
            (5, 3), (1, 1), (0, 1), (1, 1), (1, 1)));
        var prefix = element.Header.GaSpecificConfigPrefix;

        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigCompositionResult.Composed,
            PlaybackIsoBmffGaSpecificConfigComposer.Compose(
                prefix, element, out var config));
        var composed = Assert.IsType<PlaybackIsoBmffGaSpecificConfig>(config);
        Assert.Same(prefix.AudioSpecificConfigHeader,
            composed.AudioSpecificConfigHeader);
        Assert.Equal(20, composed.AudioObjectType);
        Assert.Equal((byte)5, composed.LayerNumber);
        Assert.True(composed.SectionDataResilienceFlag);
        Assert.False(composed.ScalefactorDataResilienceFlag);
        Assert.True(composed.SpectralDataResilienceFlag);
        Assert.True(composed.ExtensionFlag3);
        Assert.Equal(71, composed.ConsumedBitLength);
    }

    [Fact]
    public void ComposeGaSpecificConfig_ContinuesWithoutProgramConfigElement()
    {
        var source = Header(Bits(
            (6, 5), (4, 4), (2, 4),
            (1, 1), (0, 1), (0, 1), (6, 3)));
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigPrefixParseResult.Parsed,
            PlaybackIsoBmffGaSpecificConfigPrefixParser.Parse(
                source, out var prefix));

        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigCompositionResult.Composed,
            PlaybackIsoBmffGaSpecificConfigComposer.Compose(
                prefix, null, out var config));
        var composed = Assert.IsType<PlaybackIsoBmffGaSpecificConfig>(config);
        Assert.True(composed.FrameLengthFlag);
        Assert.Equal((byte)6, composed.LayerNumber);
        Assert.Equal(19, composed.ConsumedBitLength);
    }

    [Fact]
    public void ComposeGaSpecificConfig_RequiresExactProgramConfigSource()
    {
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigCompositionResult.MissingPrefix,
            PlaybackIsoBmffGaSpecificConfigComposer.Compose(
                null, null, out var missingPrefix));
        Assert.Null(missingPrefix);

        var first = ProgramConfigElement(Bits(
            (2, 5), (4, 4), (0, 4), (0, 1), (0, 1), (0, 1),
            (0, 4), (1, 2), (4, 4),
            (0, 4), (0, 4), (0, 4), (0, 2), (0, 3), (0, 4),
            (0, 1), (0, 1), (0, 1), (0, 6), (0, 8)));
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigCompositionResult
                .MissingProgramConfigElement,
            PlaybackIsoBmffGaSpecificConfigComposer.Compose(
                first.Header.GaSpecificConfigPrefix, null, out var missing));
        Assert.Null(missing);

        var second = ProgramConfigElement(Bits(
            (2, 5), (3, 4), (0, 4), (0, 1), (0, 1), (0, 1),
            (0, 4), (1, 2), (3, 4),
            (0, 4), (0, 4), (0, 4), (0, 2), (0, 3), (0, 4),
            (0, 1), (0, 1), (0, 1), (0, 6), (0, 8)));
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigCompositionResult
                .ProgramConfigElementSourceMismatch,
            PlaybackIsoBmffGaSpecificConfigComposer.Compose(
                first.Header.GaSpecificConfigPrefix,
                second,
                out var mismatch));
        Assert.Null(mismatch);
    }

    [Fact]
    public void ComposeGaSpecificConfig_RejectsUnexpectedPceAndTruncation()
    {
        var element = ProgramConfigElement(Bits(
            (2, 5), (4, 4), (0, 4), (0, 1), (0, 1), (0, 1),
            (0, 4), (1, 2), (4, 4),
            (0, 4), (0, 4), (0, 4), (0, 2), (0, 3), (0, 4),
            (0, 1), (0, 1), (0, 1), (0, 6), (0, 8)));
        var direct = Header(Bits(
            (6, 5), (4, 4), (2, 4), (0, 1), (0, 1), (0, 1)));
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigPrefixParseResult.Parsed,
            PlaybackIsoBmffGaSpecificConfigPrefixParser.Parse(
                direct, out var directPrefix));
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigCompositionResult
                .UnexpectedProgramConfigElement,
            PlaybackIsoBmffGaSpecificConfigComposer.Compose(
                directPrefix, element, out var unexpected));
        Assert.Null(unexpected);
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigCompositionResult.NeedMoreData,
            PlaybackIsoBmffGaSpecificConfigComposer.Compose(
                directPrefix, null, out var truncated));
        Assert.Null(truncated);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ParseErPostConfig_ProjectsSupportedEpConfig(uint epConfig)
    {
        var gaConfig = GaConfig(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (epConfig, 2)));

        Assert.Equal(
            PlaybackIsoBmffErAudioPostConfigParseResult.Parsed,
            PlaybackIsoBmffErAudioPostConfigParser.Parse(
                gaConfig, out var postConfig));
        var parsed = Assert.IsType<PlaybackIsoBmffErAudioPostConfig>(postConfig);
        Assert.Same(gaConfig, parsed.GaSpecificConfig);
        Assert.Equal((byte)epConfig, parsed.EpConfig);
        Assert.Equal(18, parsed.ConsumedBitLength);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void ParseErPostConfig_RejectsUnparsedProtectionConfig(uint epConfig)
    {
        var gaConfig = GaConfig(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (epConfig, 2)));

        Assert.Equal(
            PlaybackIsoBmffErAudioPostConfigParseResult
                .UnsupportedErrorProtectionSpecificConfig,
            PlaybackIsoBmffErAudioPostConfigParser.Parse(
                gaConfig, out var postConfig));
        Assert.Null(postConfig);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void ProjectErProtectionRequirement_RetainsEveryEpConfig(
        uint epConfig,
        bool requiresSpecificConfig)
    {
        var gaConfig = GaConfig(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (epConfig, 2)));

        Assert.Equal(
            PlaybackIsoBmffErAudioProtectionRequirementResult.Projected,
            PlaybackIsoBmffErAudioProtectionRequirementProjector.Project(
                gaConfig, out var requirement));
        var projected = Assert.IsType<
            PlaybackIsoBmffErAudioProtectionRequirement>(requirement);
        Assert.Same(gaConfig, projected.GaSpecificConfig);
        Assert.Equal((byte)epConfig, projected.EpConfig);
        Assert.Equal(requiresSpecificConfig,
            projected.RequiresErrorProtectionSpecificConfig);
        Assert.Equal(18, projected.ConsumedBitLength);
    }

    [Fact]
    public void ProjectErProtectionRequirement_MissingNonErAndTruncatedAreDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffErAudioProtectionRequirementResult
                .MissingGaSpecificConfig,
            PlaybackIsoBmffErAudioProtectionRequirementProjector.Project(
                null, out var missing));
        Assert.Null(missing);

        var nonEr = GaConfig(Bits(
            (2, 5), (4, 4), (2, 4), (0, 1), (0, 1), (0, 1)));
        Assert.Equal(
            PlaybackIsoBmffErAudioProtectionRequirementResult.NotRequired,
            PlaybackIsoBmffErAudioProtectionRequirementProjector.Project(
                nonEr, out var notRequired));
        Assert.Null(notRequired);

        var truncated = GaConfig(Bits(
            (17, 5), (4, 4), (2, 4), (0, 1), (0, 1), (0, 1)));
        Assert.Equal(
            PlaybackIsoBmffErAudioProtectionRequirementResult.NeedMoreData,
            PlaybackIsoBmffErAudioProtectionRequirementProjector.Project(
                truncated, out var incomplete));
        Assert.Null(incomplete);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void ParseErProtectionPrefix_StopsBeforeFirstSet(uint epConfig)
    {
        var requirement = ErProtectionRequirement(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (epConfig, 2),
            (2, 8), (2, 2), (5, 3), (3, 3), (4, 6)));

        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionSpecificConfigPrefixParseResult
                .Parsed,
            PlaybackIsoBmffErErrorProtectionSpecificConfigPrefixParser.Parse(
                requirement, out var prefix));
        var parsed = Assert.IsType<
            PlaybackIsoBmffErErrorProtectionSpecificConfigPrefix>(prefix);
        Assert.Same(requirement, parsed.Requirement);
        Assert.Equal((byte)2, parsed.PredefinedSetCount);
        Assert.Equal((byte)2, parsed.InterleaveType);
        Assert.Equal((byte)5, parsed.BitStuffing);
        Assert.Equal((byte)3, parsed.ConcatenatedFrameCount);
        Assert.Equal(34, parsed.ConsumedBitLength);

        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionFirstSetHeaderParseResult.Parsed,
            PlaybackIsoBmffErErrorProtectionFirstSetHeaderParser.Parse(
                parsed, out var firstSet));
        var set = Assert.IsType<
            PlaybackIsoBmffErErrorProtectionFirstSetHeader>(firstSet);
        Assert.Same(parsed, set.Prefix);
        Assert.Equal(0, set.SetIndex);
        Assert.Equal((byte)4, set.ClassCount);
        Assert.Equal(40, set.ConsumedBitLength);
    }

    [Fact]
    public void ParseErProtectionPrefix_MissingAndNotRequiredAreDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionSpecificConfigPrefixParseResult
                .MissingRequirement,
            PlaybackIsoBmffErErrorProtectionSpecificConfigPrefixParser.Parse(
                null, out var missing));
        Assert.Null(missing);

        var notRequired = ErProtectionRequirement(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (1, 2)));
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionSpecificConfigPrefixParseResult
                .NotRequired,
            PlaybackIsoBmffErErrorProtectionSpecificConfigPrefixParser.Parse(
                notRequired, out var absent));
        Assert.Null(absent);
    }

    [Fact]
    public void ParseErProtectionPrefix_EnforcesSetLimitAndOptions()
    {
        var requirement = ErProtectionRequirement(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (2, 2),
            (2, 8), (0, 2), (0, 3), (0, 3)));
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionSpecificConfigPrefixParseResult
                .PredefinedSetLimitExceeded,
            PlaybackIsoBmffErErrorProtectionSpecificConfigPrefixParser.Parse(
                requirement, out var limited,
                new() { MaximumPredefinedSetCount = 1 }));
        Assert.Null(limited);
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionSpecificConfigPrefixParseResult
                .InvalidOptions,
            PlaybackIsoBmffErErrorProtectionSpecificConfigPrefixParser.Parse(
                requirement, out var invalid,
                new() { MaximumPredefinedSetCount = -1 }));
        Assert.Null(invalid);
    }

    [Fact]
    public void ParseErProtectionPrefix_TruncationProducesNoPartialPrefix()
    {
        var fixedHeaderTruncated = ErProtectionRequirement(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (2, 2), (1, 7)));
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionSpecificConfigPrefixParseResult
                .NeedMoreData,
            PlaybackIsoBmffErErrorProtectionSpecificConfigPrefixParser.Parse(
                fixedHeaderTruncated, out var fixedMissing));
        Assert.Null(fixedMissing);

    }

    [Fact]
    public void ParseErProtectionFirstSet_RejectsMissingAndEmpty()
    {
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionFirstSetHeaderParseResult
                .MissingPrefix,
            PlaybackIsoBmffErErrorProtectionFirstSetHeaderParser.Parse(
                null, out var missing));
        Assert.Null(missing);

        var empty = ErProtectionPrefix(0);
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionFirstSetHeaderParseResult
                .NoPredefinedSets,
            PlaybackIsoBmffErErrorProtectionFirstSetHeaderParser.Parse(
                empty, out var noSets));
        Assert.Null(noSets);

    }

    [Fact]
    public void ParseErProtectionFirstClass_ProjectsConditionalFixedFields()
    {
        var header = ErProtectionFirstSetHeader(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (2, 2),
            (1, 8), (2, 2), (0, 3), (3, 3),
            (1, 6),
            (0, 1), (0, 1), (0, 1), (1, 1),
            (0, 2), (1, 1), (3, 2), (1, 1),
            (0x1234, 16), (17, 5), (9, 5)));

        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionFirstClassParseResult.Parsed,
            PlaybackIsoBmffErErrorProtectionFirstClassParser.Parse(
                header, out var firstClass));
        var parsed = Assert.IsType<
            PlaybackIsoBmffErErrorProtectionFirstClass>(firstClass);
        Assert.Same(header, parsed.FirstSetHeader);
        Assert.Equal(0, parsed.ClassIndex);
        Assert.False(parsed.LengthEscape);
        Assert.False(parsed.RateEscape);
        Assert.False(parsed.CrcLengthEscape);
        Assert.True(parsed.Concatenate);
        Assert.Equal((byte)0, parsed.FecType);
        Assert.True(parsed.Termination);
        Assert.Equal((byte)3, parsed.Interleave);
        Assert.True(parsed.Optional);
        Assert.Null(parsed.NumberOfBitsForLength);
        Assert.Equal((ushort)0x1234, parsed.ClassLength);
        Assert.Equal((byte)17, parsed.ClassRate);
        Assert.Equal((byte)9, parsed.ClassCrcLength);
        Assert.Equal(76, parsed.ConsumedBitLength);
    }

    [Fact]
    public void ParseErProtectionFirstClass_ProjectsEscapeFields()
    {
        var header = ErProtectionFirstSetHeader(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (3, 2),
            (1, 8), (1, 2), (0, 3), (1, 3),
            (1, 6),
            (1, 1), (1, 1), (1, 1),
            (2, 2), (0, 1), (7, 4)));

        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionFirstClassParseResult.Parsed,
            PlaybackIsoBmffErErrorProtectionFirstClassParser.Parse(
                header, out var firstClass));
        var parsed = Assert.IsType<
            PlaybackIsoBmffErErrorProtectionFirstClass>(firstClass);
        Assert.True(parsed.LengthEscape);
        Assert.True(parsed.RateEscape);
        Assert.True(parsed.CrcLengthEscape);
        Assert.Null(parsed.Concatenate);
        Assert.Equal((byte)2, parsed.FecType);
        Assert.Null(parsed.Termination);
        Assert.Null(parsed.Interleave);
        Assert.False(parsed.Optional);
        Assert.Equal((byte)7, parsed.NumberOfBitsForLength);
        Assert.Null(parsed.ClassLength);
        Assert.Null(parsed.ClassRate);
        Assert.Null(parsed.ClassCrcLength);
        Assert.Equal(50, parsed.ConsumedBitLength);
    }

    [Fact]
    public void ParseErProtectionFirstClass_MissingEmptyAndTruncatedFailClosed()
    {
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionFirstClassParseResult
                .MissingFirstSetHeader,
            PlaybackIsoBmffErErrorProtectionFirstClassParser.Parse(
                null, out var missing));
        Assert.Null(missing);

        var empty = ErProtectionFirstSetHeader(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (2, 2),
            (1, 8), (1, 2), (0, 3), (1, 3), (0, 6)));
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionFirstClassParseResult.NoClasses,
            PlaybackIsoBmffErErrorProtectionFirstClassParser.Parse(
                empty, out var noClasses));
        Assert.Null(noClasses);

        var truncated = ErProtectionFirstSetHeader(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (2, 2),
            (1, 8), (1, 2), (0, 3), (1, 3), (1, 6),
            (0, 1), (0, 1), (0, 1), (2, 2), (0, 1)));
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionFirstClassParseResult.NeedMoreData,
            PlaybackIsoBmffErErrorProtectionFirstClassParser.Parse(
                truncated, out var incomplete));
        Assert.Null(incomplete);
    }

    [Fact]
    public void ParseErProtectionFirstSet_ParsesClassesAndReorderedOutput()
    {
        var header = ErProtectionFirstSetHeader(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (2, 2),
            (1, 8), (1, 2), (0, 3), (1, 3),
            (2, 6),
            (1, 1), (1, 1), (1, 1), (2, 2), (0, 1), (7, 4),
            (1, 1), (1, 1), (1, 1), (1, 2), (1, 1), (2, 4),
            (1, 1), (1, 6), (0, 6)));

        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionFirstSetParseResult.Parsed,
            PlaybackIsoBmffErErrorProtectionFirstSetParser.Parse(
                header, out var set));
        var parsed = Assert.IsType<
            PlaybackIsoBmffErErrorProtectionFirstSet>(set);
        Assert.Same(header, parsed.Header);
        Assert.Equal(2, parsed.Classes.Count);
        Assert.Equal(0, parsed.Classes[0].ClassIndex);
        Assert.Equal((byte)2, parsed.Classes[0].FecType);
        Assert.Equal((byte)7, parsed.Classes[0].NumberOfBitsForLength);
        Assert.Equal(1, parsed.Classes[1].ClassIndex);
        Assert.Equal((byte)1, parsed.Classes[1].FecType);
        Assert.True(parsed.Classes[1].Optional);
        Assert.Equal((byte)2, parsed.Classes[1].NumberOfBitsForLength);
        Assert.True(parsed.ReorderedOutput);
        Assert.Equal(new byte[] { 1, 0 }, parsed.OutputOrder);
        Assert.Equal(73, parsed.ConsumedBitLength);
    }

    [Fact]
    public void ParseErProtectionFirstSet_AllowsEmptyUnreorderedSet()
    {
        var header = ErProtectionFirstSetHeader(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (2, 2),
            (1, 8), (1, 2), (0, 3), (1, 3),
            (0, 6), (0, 1)));

        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionFirstSetParseResult.Parsed,
            PlaybackIsoBmffErErrorProtectionFirstSetParser.Parse(
                header, out var set));
        var parsed = Assert.IsType<
            PlaybackIsoBmffErErrorProtectionFirstSet>(set);
        Assert.Empty(parsed.Classes);
        Assert.False(parsed.ReorderedOutput);
        Assert.Empty(parsed.OutputOrder);
        Assert.Equal(41, parsed.ConsumedBitLength);
    }

    [Fact]
    public void ParseErProtectionFirstSet_RejectsInvalidOutputOrder()
    {
        var header = ErProtectionFirstSetHeader(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (2, 2),
            (1, 8), (1, 2), (0, 3), (1, 3),
            (2, 6),
            (1, 1), (1, 1), (1, 1), (2, 2), (0, 1), (1, 4),
            (1, 1), (1, 1), (1, 1), (2, 2), (0, 1), (1, 4),
            (1, 1), (0, 6), (0, 6)));

        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionFirstSetParseResult
                .InvalidClassOutputOrder,
            PlaybackIsoBmffErErrorProtectionFirstSetParser.Parse(
                header, out var rejected));
        Assert.Null(rejected);
    }

    [Fact]
    public void ParseErProtectionFirstSet_MissingAndTruncatedFailClosed()
    {
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionFirstSetParseResult
                .MissingFirstSetHeader,
            PlaybackIsoBmffErErrorProtectionFirstSetParser.Parse(
                null, out var missing));
        Assert.Null(missing);

        var truncated = ErProtectionFirstSetHeader(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (2, 2),
            (1, 8), (1, 2), (0, 3), (1, 3),
            (2, 6),
            (1, 1), (1, 1), (1, 1), (2, 2), (0, 1), (7, 4)));
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionFirstSetParseResult.NeedMoreData,
            PlaybackIsoBmffErErrorProtectionFirstSetParser.Parse(
                truncated, out var incomplete));
        Assert.Null(incomplete);
    }

    [Fact]
    public void ParseErProtectionSecondSetHeader_StartsAtExactFirstSetEnd()
    {
        var firstSet = ErProtectionFirstSet(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (2, 2),
            (2, 8), (1, 2), (0, 3), (1, 3),
            (0, 6), (0, 1),
            (5, 6)));

        Assert.Equal(41, firstSet.ConsumedBitLength);
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionSecondSetHeaderParseResult.Parsed,
            PlaybackIsoBmffErErrorProtectionSecondSetHeaderParser.Parse(
                firstSet, out var header));
        var parsed = Assert.IsType<
            PlaybackIsoBmffErErrorProtectionSecondSetHeader>(header);
        Assert.Same(firstSet, parsed.FirstSet);
        Assert.Equal(1, parsed.SetIndex);
        Assert.Equal((byte)5, parsed.ClassCount);
        Assert.Equal(47, parsed.ConsumedBitLength);
    }

    [Fact]
    public void ParseErProtectionSecondSetHeader_MissingAndSingleSetAreDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionSecondSetHeaderParseResult
                .MissingFirstSet,
            PlaybackIsoBmffErErrorProtectionSecondSetHeaderParser.Parse(
                null, out var missing));
        Assert.Null(missing);

        var onlySet = ErProtectionFirstSet(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (2, 2),
            (1, 8), (1, 2), (0, 3), (1, 3),
            (0, 6), (0, 1)));
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionSecondSetHeaderParseResult
                .NoAdditionalSets,
            PlaybackIsoBmffErErrorProtectionSecondSetHeaderParser.Parse(
                onlySet, out var absent));
        Assert.Null(absent);
    }

    [Fact]
    public void ParseErProtectionSecondSetHeader_TruncationFailsClosed()
    {
        var firstSet = ErProtectionFirstSet(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (2, 2),
            (2, 8), (1, 2), (0, 3), (1, 3),
            (1, 6),
            (1, 1), (1, 1), (1, 1), (2, 2), (0, 1), (7, 4),
            (0, 1)));
        Assert.Equal(51, firstSet.ConsumedBitLength);
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionSecondSetHeaderParseResult
                .NeedMoreData,
            PlaybackIsoBmffErErrorProtectionSecondSetHeaderParser.Parse(
                firstSet, out var incomplete));
        Assert.Null(incomplete);
    }

    [Fact]
    public void ParseErProtectionSecondSetFirstClass_StartsAfterSecondCount()
    {
        var header = ErProtectionSecondSetHeader(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (2, 2),
            (2, 8), (1, 2), (0, 3), (1, 3),
            (0, 6), (0, 1),
            (1, 6),
            (0, 1), (0, 1), (0, 1), (2, 2), (1, 1),
            (0x1234, 16), (0x55, 7), (13, 5)));

        Assert.Equal(47, header.ConsumedBitLength);
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionSecondSetFirstClassParseResult
                .Parsed,
            PlaybackIsoBmffErErrorProtectionSecondSetFirstClassParser.Parse(
                header, out var firstClass));
        var parsed = Assert.IsType<
            PlaybackIsoBmffErErrorProtectionSecondSetFirstClass>(firstClass);
        Assert.Same(header, parsed.SecondSetHeader);
        Assert.Equal(0, parsed.ClassIndex);
        Assert.False(parsed.LengthEscape);
        Assert.False(parsed.RateEscape);
        Assert.False(parsed.CrcLengthEscape);
        Assert.Null(parsed.Concatenate);
        Assert.Equal((byte)2, parsed.FecType);
        Assert.Null(parsed.Termination);
        Assert.Null(parsed.Interleave);
        Assert.True(parsed.Optional);
        Assert.Null(parsed.NumberOfBitsForLength);
        Assert.Equal((ushort)0x1234, parsed.ClassLength);
        Assert.Equal((byte)0x55, parsed.ClassRate);
        Assert.Equal((byte)13, parsed.ClassCrcLength);
        Assert.Equal(81, parsed.ConsumedBitLength);
    }

    [Fact]
    public void ParseErProtectionSecondSetFirstClass_MissingAndEmptyAreDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionSecondSetFirstClassParseResult
                .MissingSecondSetHeader,
            PlaybackIsoBmffErErrorProtectionSecondSetFirstClassParser.Parse(
                null, out var missing));
        Assert.Null(missing);

        var empty = ErProtectionSecondSetHeader(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (2, 2),
            (2, 8), (1, 2), (0, 3), (1, 3),
            (0, 6), (0, 1),
            (0, 6)));
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionSecondSetFirstClassParseResult
                .NoClasses,
            PlaybackIsoBmffErErrorProtectionSecondSetFirstClassParser.Parse(
                empty, out var absent));
        Assert.Null(absent);
    }

    [Fact]
    public void ParseErProtectionSecondSetFirstClass_TruncationFailsClosed()
    {
        var header = ErProtectionSecondSetHeader(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (2, 2),
            (2, 8), (1, 2), (0, 3), (1, 3),
            (0, 6), (0, 1),
            (1, 6),
            (0, 1)));
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionSecondSetFirstClassParseResult
                .NeedMoreData,
            PlaybackIsoBmffErErrorProtectionSecondSetFirstClassParser.Parse(
                header, out var incomplete));
        Assert.Null(incomplete);
    }

    [Fact]
    public void ParseErProtectionSecondSet_ParsesClassesAndReorderedOutput()
    {
        var header = ErProtectionSecondSetHeader(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (2, 2),
            (2, 8), (1, 2), (0, 3), (1, 3),
            (0, 6), (0, 1),
            (2, 6),
            (1, 1), (1, 1), (1, 1), (2, 2), (0, 1), (7, 4),
            (1, 1), (1, 1), (1, 1), (2, 2), (1, 1), (3, 4),
            (1, 1), (1, 6), (0, 6)));

        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionSecondSetParseResult.Parsed,
            PlaybackIsoBmffErErrorProtectionSecondSetParser.Parse(
                header, out var set));
        var parsed = Assert.IsType<
            PlaybackIsoBmffErErrorProtectionSecondSet>(set);
        Assert.Same(header, parsed.Header);
        Assert.Equal(2, parsed.Classes.Count);
        Assert.Equal(0, parsed.Classes[0].ClassIndex);
        Assert.Equal((byte)7, parsed.Classes[0].NumberOfBitsForLength);
        Assert.Equal(1, parsed.Classes[1].ClassIndex);
        Assert.Equal((byte)3, parsed.Classes[1].NumberOfBitsForLength);
        Assert.True(parsed.ReorderedOutput);
        Assert.Equal(new byte[] { 1, 0 }, parsed.OutputOrder);
        Assert.Equal(80, parsed.ConsumedBitLength);
    }

    [Fact]
    public void ParseErProtectionSecondSet_AllowsEmptyUnreorderedSet()
    {
        var header = ErProtectionSecondSetHeader(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (2, 2),
            (2, 8), (1, 2), (0, 3), (1, 3),
            (0, 6), (0, 1),
            (0, 6), (0, 1)));

        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionSecondSetParseResult.Parsed,
            PlaybackIsoBmffErErrorProtectionSecondSetParser.Parse(
                header, out var set));
        var parsed = Assert.IsType<
            PlaybackIsoBmffErErrorProtectionSecondSet>(set);
        Assert.Empty(parsed.Classes);
        Assert.False(parsed.ReorderedOutput);
        Assert.Empty(parsed.OutputOrder);
        Assert.Equal(48, parsed.ConsumedBitLength);
    }

    [Fact]
    public void ParseErProtectionSecondSet_RejectsInvalidOutputOrder()
    {
        var header = ErProtectionSecondSetHeader(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (2, 2),
            (2, 8), (1, 2), (0, 3), (1, 3),
            (0, 6), (0, 1),
            (2, 6),
            (1, 1), (1, 1), (1, 1), (2, 2), (0, 1), (7, 4),
            (1, 1), (1, 1), (1, 1), (2, 2), (1, 1), (3, 4),
            (1, 1), (0, 6), (0, 6)));

        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionSecondSetParseResult
                .InvalidClassOutputOrder,
            PlaybackIsoBmffErErrorProtectionSecondSetParser.Parse(
                header, out var invalid));
        Assert.Null(invalid);
    }

    [Fact]
    public void ParseErProtectionSecondSet_MissingAndTruncatedFailClosed()
    {
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionSecondSetParseResult
                .MissingSecondSetHeader,
            PlaybackIsoBmffErErrorProtectionSecondSetParser.Parse(
                null, out var missing));
        Assert.Null(missing);

        var truncated = ErProtectionSecondSetHeader(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (2, 2),
            (2, 8), (1, 2), (0, 3), (1, 3),
            (0, 6), (0, 1),
            (1, 6),
            (1, 1)));
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionSecondSetParseResult.NeedMoreData,
            PlaybackIsoBmffErErrorProtectionSecondSetParser.Parse(
                truncated, out var incomplete));
        Assert.Null(incomplete);
    }

    [Fact]
    public void ParseErProtectionThirdSetHeader_StartsAtExactSecondSetEnd()
    {
        var secondSet = ErProtectionSecondSet(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (2, 2),
            (3, 8), (1, 2), (0, 3), (1, 3),
            (0, 6), (0, 1),
            (0, 6), (0, 1),
            (5, 6)));

        Assert.Equal(48, secondSet.ConsumedBitLength);
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionThirdSetHeaderParseResult.Parsed,
            PlaybackIsoBmffErErrorProtectionThirdSetHeaderParser.Parse(
                secondSet, out var header));
        var parsed = Assert.IsType<
            PlaybackIsoBmffErErrorProtectionThirdSetHeader>(header);
        Assert.Same(secondSet, parsed.SecondSet);
        Assert.Equal(2, parsed.SetIndex);
        Assert.Equal((byte)5, parsed.ClassCount);
        Assert.Equal(54, parsed.ConsumedBitLength);
    }

    [Fact]
    public void ParseErProtectionThirdSetHeader_MissingAndTwoSetsAreDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionThirdSetHeaderParseResult
                .MissingSecondSet,
            PlaybackIsoBmffErErrorProtectionThirdSetHeaderParser.Parse(
                null, out var missing));
        Assert.Null(missing);

        var onlyTwoSets = ErProtectionSecondSet(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (2, 2),
            (2, 8), (1, 2), (0, 3), (1, 3),
            (0, 6), (0, 1),
            (0, 6), (0, 1)));
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionThirdSetHeaderParseResult
                .NoAdditionalSets,
            PlaybackIsoBmffErErrorProtectionThirdSetHeaderParser.Parse(
                onlyTwoSets, out var absent));
        Assert.Null(absent);
    }

    [Fact]
    public void ParseErProtectionThirdSetHeader_TruncationFailsClosed()
    {
        var secondSet = ErProtectionSecondSet(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (2, 2),
            (3, 8), (1, 2), (0, 3), (1, 3),
            (0, 6), (0, 1),
            (0, 6), (0, 1)));
        Assert.Equal(48, secondSet.ConsumedBitLength);
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionThirdSetHeaderParseResult
                .NeedMoreData,
            PlaybackIsoBmffErErrorProtectionThirdSetHeaderParser.Parse(
                secondSet, out var incomplete));
        Assert.Null(incomplete);
    }

    [Fact]
    public void ParseErProtectionThirdSetFirstClass_StartsAfterThirdCount()
    {
        var header = ErProtectionThirdSetHeader(Bits(
            (17, 5), (4, 4), (2, 4), (0, 1), (0, 1), (0, 1), (2, 2),
            (3, 8), (1, 2), (0, 3), (1, 3),
            (0, 6), (0, 1), (0, 6), (0, 1), (1, 6),
            (1, 1), (1, 1), (1, 1), (2, 2), (1, 1), (9, 4)));

        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionThirdSetFirstClassParseResult.Parsed,
            PlaybackIsoBmffErErrorProtectionThirdSetFirstClassParser.Parse(
                header, out var firstClass));
        var parsed = Assert.IsType<
            PlaybackIsoBmffErErrorProtectionThirdSetFirstClass>(firstClass);
        Assert.Same(header, parsed.ThirdSetHeader);
        Assert.Equal(0, parsed.ClassIndex);
        Assert.Equal((byte)9, parsed.NumberOfBitsForLength);
        Assert.Equal(64, parsed.ConsumedBitLength);
    }

    [Fact]
    public void ParseErProtectionThirdSet_ParsesClassesAndReorderedOutput()
    {
        var header = ErProtectionThirdSetHeader(Bits(
            (17, 5), (4, 4), (2, 4), (0, 1), (0, 1), (0, 1), (2, 2),
            (3, 8), (1, 2), (0, 3), (1, 3),
            (0, 6), (0, 1), (0, 6), (0, 1), (2, 6),
            (1, 1), (1, 1), (1, 1), (2, 2), (0, 1), (7, 4),
            (1, 1), (1, 1), (1, 1), (2, 2), (1, 1), (3, 4),
            (1, 1), (1, 6), (0, 6)));

        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionThirdSetParseResult.Parsed,
            PlaybackIsoBmffErErrorProtectionThirdSetParser.Parse(
                header, out var set));
        var parsed = Assert.IsType<PlaybackIsoBmffErErrorProtectionThirdSet>(set);
        Assert.Equal(2, parsed.Classes.Count);
        Assert.Equal(0, parsed.Classes[0].ClassIndex);
        Assert.Equal(1, parsed.Classes[1].ClassIndex);
        Assert.True(parsed.ReorderedOutput);
        Assert.Equal(new byte[] { 1, 0 }, parsed.OutputOrder);
        Assert.Equal(87, parsed.ConsumedBitLength);
    }

    [Fact]
    public void ParseErProtectionThirdSet_AllowsEmptyAndRejectsInvalidOrder()
    {
        var empty = ErProtectionThirdSetHeader(Bits(
            (17, 5), (4, 4), (2, 4), (0, 1), (0, 1), (0, 1), (2, 2),
            (3, 8), (1, 2), (0, 3), (1, 3),
            (0, 6), (0, 1), (0, 6), (0, 1), (0, 6), (0, 1)));
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionThirdSetParseResult.Parsed,
            PlaybackIsoBmffErErrorProtectionThirdSetParser.Parse(
                empty, out var emptySet));
        Assert.Empty(Assert.IsType<PlaybackIsoBmffErErrorProtectionThirdSet>(
            emptySet).Classes);

        var invalid = ErProtectionThirdSetHeader(Bits(
            (17, 5), (4, 4), (2, 4), (0, 1), (0, 1), (0, 1), (2, 2),
            (3, 8), (1, 2), (0, 3), (1, 3),
            (0, 6), (0, 1), (0, 6), (0, 1), (2, 6),
            (1, 1), (1, 1), (1, 1), (2, 2), (0, 1), (7, 4),
            (1, 1), (1, 1), (1, 1), (2, 2), (1, 1), (3, 4),
            (1, 1), (0, 6), (0, 6)));
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionThirdSetParseResult
                .InvalidClassOutputOrder,
            PlaybackIsoBmffErErrorProtectionThirdSetParser.Parse(
                invalid, out var rejected));
        Assert.Null(rejected);
    }

    [Fact]
    public void ParseErProtectionThirdSet_MissingAndTruncatedFailClosed()
    {
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionThirdSetParseResult
                .MissingThirdSetHeader,
            PlaybackIsoBmffErErrorProtectionThirdSetParser.Parse(
                null, out var missing));
        Assert.Null(missing);

        var truncated = ErProtectionThirdSetHeader(Bits(
            (17, 5), (4, 4), (2, 4), (0, 1), (0, 1), (0, 1), (2, 2),
            (3, 8), (1, 2), (0, 3), (1, 3),
            (0, 6), (0, 1), (0, 6), (0, 1), (1, 6), (1, 1)));
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionThirdSetParseResult.NeedMoreData,
            PlaybackIsoBmffErErrorProtectionThirdSetParser.Parse(
                truncated, out var incomplete));
        Assert.Null(incomplete);
    }

    [Fact]
    public void ParseErProtectionPredefinedSets_TraversesFourthSet()
    {
        var prefix = ErProtectionPrefix(Bits(
            (17, 5), (4, 4), (2, 4), (0, 1), (0, 1), (0, 1), (2, 2),
            (4, 8), (1, 2), (0, 3), (1, 3),
            (0, 6), (0, 1), (0, 6), (0, 1), (0, 6), (0, 1),
            (1, 6), (1, 1), (1, 1), (1, 1), (2, 2), (1, 1),
            (9, 4), (0, 1)));

        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionPredefinedSetsParseResult.Parsed,
            PlaybackIsoBmffErErrorProtectionPredefinedSetsParser.Parse(
                prefix, out var sets));
        var parsed = Assert.IsType<
            PlaybackIsoBmffErErrorProtectionPredefinedSets>(sets);
        Assert.Equal(4, parsed.Sets.Count);
        Assert.Equal(1, parsed.TotalClassCount);
        Assert.Equal(3, parsed.Sets[3].SetIndex);
        Assert.Equal(3, parsed.Sets[3].Classes[0].SetIndex);
        Assert.Equal((byte)9,
            parsed.Sets[3].Classes[0].NumberOfBitsForLength);
        Assert.Equal(72, parsed.ConsumedBitLength);
    }

    [Fact]
    public void ParseErProtectionPredefinedSets_AllowsZeroSets()
    {
        var prefix = ErProtectionPrefix(0);
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionPredefinedSetsParseResult.Parsed,
            PlaybackIsoBmffErErrorProtectionPredefinedSetsParser.Parse(
                prefix, out var sets));
        var parsed = Assert.IsType<
            PlaybackIsoBmffErErrorProtectionPredefinedSets>(sets);
        Assert.Empty(parsed.Sets);
        Assert.Equal(0, parsed.TotalClassCount);
        Assert.Equal(prefix.ConsumedBitLength, parsed.ConsumedBitLength);
    }

    [Fact]
    public void ParseErProtectionPredefinedSets_EnforcesAggregateClassLimit()
    {
        var prefix = ErProtectionPrefix(Bits(
            (17, 5), (4, 4), (2, 4), (0, 1), (0, 1), (0, 1), (2, 2),
            (1, 8), (1, 2), (0, 3), (1, 3), (2, 6)));
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionPredefinedSetsParseResult
                .AggregateClassLimitExceeded,
            PlaybackIsoBmffErErrorProtectionPredefinedSetsParser.Parse(
                prefix, out var rejected,
                new() { MaximumTotalClassCount = 1 }));
        Assert.Null(rejected);
    }

    [Fact]
    public void ParseErProtectionPredefinedSets_MissingAndTruncatedFailClosed()
    {
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionPredefinedSetsParseResult
                .MissingPrefix,
            PlaybackIsoBmffErErrorProtectionPredefinedSetsParser.Parse(
                null, out var missing));
        Assert.Null(missing);

        var truncated = ErProtectionPrefix(Bits(
            (17, 5), (4, 4), (2, 4), (0, 1), (0, 1), (0, 1), (2, 2),
            (1, 8), (1, 2), (0, 3), (1, 3)));
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionPredefinedSetsParseResult.NeedMoreData,
            PlaybackIsoBmffErErrorProtectionPredefinedSetsParser.Parse(
                truncated, out var incomplete));
        Assert.Null(incomplete);
    }

    [Fact]
    public void ParseErProtectionHeaderProtection_ProjectsEnabledFields()
    {
        var sets = ErProtectionPredefinedSets(Bits(
            (17, 5), (4, 4), (2, 4), (0, 1), (0, 1), (0, 1), (2, 2),
            (0, 8), (1, 2), (0, 3), (1, 3),
            (1, 1), (17, 5), (9, 5)));
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionHeaderProtectionParseResult.Parsed,
            PlaybackIsoBmffErErrorProtectionHeaderProtectionParser.Parse(
                sets, out var headerProtection));
        var parsed = Assert.IsType<
            PlaybackIsoBmffErErrorProtectionHeaderProtection>(headerProtection);
        Assert.Same(sets, parsed.PredefinedSets);
        Assert.True(parsed.Enabled);
        Assert.Equal((byte)17, parsed.HeaderRate);
        Assert.Equal((byte)9, parsed.HeaderCrcLength);
        Assert.Equal(45, parsed.ConsumedBitLength);
    }

    [Fact]
    public void ParseErProtectionHeaderProtection_AllowsDisabledHeader()
    {
        var sets = ErProtectionPredefinedSets(Bits(
            (17, 5), (4, 4), (2, 4), (0, 1), (0, 1), (0, 1), (2, 2),
            (0, 8), (1, 2), (0, 3), (1, 3), (0, 1)));
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionHeaderProtectionParseResult.Parsed,
            PlaybackIsoBmffErErrorProtectionHeaderProtectionParser.Parse(
                sets, out var headerProtection));
        var parsed = Assert.IsType<
            PlaybackIsoBmffErErrorProtectionHeaderProtection>(headerProtection);
        Assert.False(parsed.Enabled);
        Assert.Null(parsed.HeaderRate);
        Assert.Null(parsed.HeaderCrcLength);
        Assert.Equal(35, parsed.ConsumedBitLength);
    }

    [Fact]
    public void ParseErProtectionHeaderProtection_MissingAndTruncatedFailClosed()
    {
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionHeaderProtectionParseResult
                .MissingPredefinedSets,
            PlaybackIsoBmffErErrorProtectionHeaderProtectionParser.Parse(
                null, out var missing));
        Assert.Null(missing);

        var sets = ErProtectionPredefinedSets(Bits(
            (17, 5), (4, 4), (2, 4), (0, 1), (0, 1), (0, 1), (2, 2),
            (0, 8), (1, 2), (0, 3), (1, 3), (1, 1), (17, 5)));
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionHeaderProtectionParseResult
                .NeedMoreData,
            PlaybackIsoBmffErErrorProtectionHeaderProtectionParser.Parse(
                sets, out var incomplete));
        Assert.Null(incomplete);
    }

    [Fact]
    public void ComposeErProtectionSpecificConfig_RetainsCompleteOwnership()
    {
        var headerProtection = ErProtectionHeaderProtection(Bits(
            (17, 5), (4, 4), (2, 4), (0, 1), (0, 1), (0, 1), (2, 2),
            (0, 8), (1, 2), (0, 3), (1, 3), (0, 1)));
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionSpecificConfigCompositionResult
                .Composed,
            PlaybackIsoBmffErErrorProtectionSpecificConfigComposer.Compose(
                headerProtection, out var config));
        var parsed = Assert.IsType<
            PlaybackIsoBmffErErrorProtectionSpecificConfig>(config);
        Assert.Same(headerProtection, parsed.HeaderProtection);
        Assert.Same(headerProtection.PredefinedSets, parsed.PredefinedSets);
        Assert.Equal(35, parsed.ConsumedBitLength);
    }

    [Fact]
    public void ParseErDirectMapping_ProjectsBothValuesForEpConfigThree()
    {
        foreach (var value in new uint[] { 0, 1 })
        {
            var config = ErProtectionSpecificConfig(Bits(
                (17, 5), (4, 4), (2, 4),
                (0, 1), (0, 1), (0, 1), (3, 2),
                (0, 8), (1, 2), (0, 3), (1, 3), (0, 1), (value, 1)));
            Assert.Equal(
                PlaybackIsoBmffErDirectMappingParseResult.Parsed,
                PlaybackIsoBmffErDirectMappingParser.Parse(
                    config, out var directMapping));
            var parsed = Assert.IsType<PlaybackIsoBmffErDirectMapping>(
                directMapping);
            Assert.Same(config, parsed.SpecificConfig);
            Assert.Equal(value != 0, parsed.Enabled);
            Assert.Equal(36, parsed.ConsumedBitLength);
        }
    }

    [Fact]
    public void ParseErDirectMapping_MissingNotRequiredAndTruncatedAreDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffErDirectMappingParseResult.MissingSpecificConfig,
            PlaybackIsoBmffErDirectMappingParser.Parse(
                null, out var missing));
        Assert.Null(missing);

        var epConfigTwo = ErProtectionSpecificConfig(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (2, 2),
            (0, 8), (1, 2), (0, 3), (1, 3), (0, 1)));
        Assert.Equal(
            PlaybackIsoBmffErDirectMappingParseResult.NotRequired,
            PlaybackIsoBmffErDirectMappingParser.Parse(
                epConfigTwo, out var absent));
        Assert.Null(absent);

        var truncated = ErProtectionSpecificConfig(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (3, 2),
            (3, 8), (1, 2), (0, 3), (1, 3),
            (0, 6), (0, 1),
            (0, 6), (0, 1),
            (0, 6), (0, 1),
            (0, 1)));
        Assert.Equal(56, truncated.ConsumedBitLength);
        Assert.Equal(
            PlaybackIsoBmffErDirectMappingParseResult.NeedMoreData,
            PlaybackIsoBmffErDirectMappingParser.Parse(
                truncated, out var incomplete));
        Assert.Null(incomplete);
    }

    [Fact]
    public void DecideErDirectMapping_RequiresEnabledValue()
    {
        foreach (var value in new uint[] { 0, 1 })
        {
            var config = ErProtectionSpecificConfig(Bits(
                (17, 5), (4, 4), (2, 4),
                (0, 1), (0, 1), (0, 1), (3, 2),
                (0, 8), (1, 2), (0, 3), (1, 3), (0, 1), (value, 1)));
            Assert.Equal(
                PlaybackIsoBmffErDirectMappingParseResult.Parsed,
                PlaybackIsoBmffErDirectMappingParser.Parse(
                    config, out var mapping));
            var result = PlaybackIsoBmffErDirectMappingDecisionResolver.Resolve(
                mapping,
                PlaybackIsoBmffErDirectMappingPolicy.RequireEnabled,
                out var decision);
            Assert.Equal(value == 1
                ? PlaybackIsoBmffErDirectMappingDecisionResult.Admitted
                : PlaybackIsoBmffErDirectMappingDecisionResult.DisabledRejected,
                result);
            Assert.Equal(value == 1, decision is not null);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void AdmitErConfiguration_EpConfigWithoutSpecificConfig(byte epConfig)
    {
        var requirement = ErProtectionRequirement(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (epConfig, 2)));
        Assert.Equal(
            PlaybackIsoBmffErConfigurationAdmissionResult.Admitted,
            PlaybackIsoBmffErConfigurationAdmissionResolver.Resolve(
                requirement, null, null, out var admission));
        var parsed = Assert.IsType<PlaybackIsoBmffErConfigurationAdmission>(
            admission);
        Assert.Same(requirement, parsed.Requirement);
        Assert.Null(parsed.SpecificConfig);
        Assert.Null(parsed.DirectMappingDecision);
    }

    [Fact]
    public void AdmitErConfiguration_EpConfigTwoRequiresOwnedSpecificConfig()
    {
        var config = ErProtectionSpecificConfig(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (2, 2),
            (0, 8), (1, 2), (0, 3), (1, 3), (0, 1)));
        var requirement = config.Prefix.Requirement;
        Assert.Equal(
            PlaybackIsoBmffErConfigurationAdmissionResult.Admitted,
            PlaybackIsoBmffErConfigurationAdmissionResolver.Resolve(
                requirement, config, null, out var admission));
        Assert.Same(config, Assert.IsType<
            PlaybackIsoBmffErConfigurationAdmission>(admission).SpecificConfig);
    }

    [Fact]
    public void AdmitErConfiguration_EpConfigThreeRequiresMappingDecision()
    {
        var config = ErProtectionSpecificConfig(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (3, 2),
            (0, 8), (1, 2), (0, 3), (1, 3), (0, 1), (1, 1)));
        var decision = ErDirectMappingDecision(config);
        Assert.Equal(
            PlaybackIsoBmffErConfigurationAdmissionResult.Admitted,
            PlaybackIsoBmffErConfigurationAdmissionResolver.Resolve(
                config.Prefix.Requirement, config, decision,
                out var admission));
        Assert.Same(decision, Assert.IsType<
            PlaybackIsoBmffErConfigurationAdmission>(admission)
                .DirectMappingDecision);
    }

    [Fact]
    public void AdmitErConfiguration_RejectsSourceMismatchAndMissingInputs()
    {
        Assert.Equal(
            PlaybackIsoBmffErConfigurationAdmissionResult.MissingRequirement,
            PlaybackIsoBmffErConfigurationAdmissionResolver.Resolve(
                null, null, null, out var missing));
        Assert.Null(missing);

        var first = ErProtectionSpecificConfig(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (2, 2),
            (0, 8), (1, 2), (0, 3), (1, 3), (0, 1)));
        var second = ErProtectionSpecificConfig(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (2, 2),
            (0, 8), (1, 2), (0, 3), (1, 3), (0, 1)));
        Assert.Equal(
            PlaybackIsoBmffErConfigurationAdmissionResult
                .SpecificConfigSourceMismatch,
            PlaybackIsoBmffErConfigurationAdmissionResolver.Resolve(
                first.Prefix.Requirement, second, null, out var mismatch));
        Assert.Null(mismatch);
    }

    [Fact]
    public void ParseErPostConfig_MissingNotRequiredAndTruncatedAreDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffErAudioPostConfigParseResult.MissingGaSpecificConfig,
            PlaybackIsoBmffErAudioPostConfigParser.Parse(
                null, out var missing));
        Assert.Null(missing);

        var nonEr = GaConfig(Bits(
            (2, 5), (4, 4), (2, 4), (0, 1), (0, 1), (0, 1)));
        Assert.Equal(
            PlaybackIsoBmffErAudioPostConfigParseResult.NotRequired,
            PlaybackIsoBmffErAudioPostConfigParser.Parse(
                nonEr, out var notRequired));
        Assert.Null(notRequired);

        var truncated = GaConfig(Bits(
            (17, 5), (4, 4), (2, 4), (0, 1), (0, 1), (0, 1)));
        Assert.Equal(
            PlaybackIsoBmffErAudioPostConfigParseResult.NeedMoreData,
            PlaybackIsoBmffErAudioPostConfigParser.Parse(
                truncated, out var incomplete));
        Assert.Null(incomplete);
    }

    [Fact]
    public void ParseImplicitSbrAfterErPostConfig_ProjectsSbrAndFollowingPs()
    {
        var gaConfig = GaConfig(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (0, 2),
            (0x2B7, 11), (5, 5), (1, 1), (4, 4),
            (0x548, 11), (1, 1)));
        Assert.Equal(
            PlaybackIsoBmffErAudioPostConfigParseResult.Parsed,
            PlaybackIsoBmffErAudioPostConfigParser.Parse(
                gaConfig, out var postConfig));

        Assert.Equal(
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult.Parsed,
            PlaybackIsoBmffImplicitSbrSyncExtensionParser
                .ParseAfterErPostConfig(postConfig, out var sbr));
        var parsedSbr =
            Assert.IsType<PlaybackIsoBmffImplicitSbrSyncExtension>(sbr);
        Assert.Same(gaConfig, parsedSbr.GaSpecificConfig);
        Assert.True(parsedSbr.SbrPresent);
        Assert.Equal((byte)4, parsedSbr.ExtensionSamplingFrequencyIndex);
        Assert.Equal(44_100u, parsedSbr.ExtensionSamplingFrequency);
        Assert.Equal(39, parsedSbr.ConsumedBitLength);

        Assert.Equal(
            PlaybackIsoBmffImplicitPsSyncExtensionParseResult.Parsed,
            PlaybackIsoBmffImplicitPsSyncExtensionParser.Parse(
                parsedSbr, out var ps));
        var parsedPs = Assert.IsType<PlaybackIsoBmffImplicitPsSyncExtension>(ps);
        Assert.True(parsedPs.PsPresent);
        Assert.Equal(51, parsedPs.ConsumedBitLength);
    }

    [Fact]
    public void ParseImplicitSbrAfterErPostConfig_AbsentMarkerIsNotPresent()
    {
        var gaConfig = GaConfig(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (1, 2)));
        Assert.Equal(
            PlaybackIsoBmffErAudioPostConfigParseResult.Parsed,
            PlaybackIsoBmffErAudioPostConfigParser.Parse(
                gaConfig, out var postConfig));

        Assert.Equal(
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult.NotPresent,
            PlaybackIsoBmffImplicitSbrSyncExtensionParser
                .ParseAfterErPostConfig(postConfig, out var extension));
        Assert.Null(extension);
    }

    [Fact]
    public void ParseImplicitSbrAfterErPostConfig_MissingAndTruncatedAreDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
                .MissingErAudioPostConfig,
            PlaybackIsoBmffImplicitSbrSyncExtensionParser
                .ParseAfterErPostConfig(null, out var missing));
        Assert.Null(missing);

        var gaConfig = GaConfig(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (0, 2),
            (0x2B7, 11)));
        Assert.Equal(
            PlaybackIsoBmffErAudioPostConfigParseResult.Parsed,
            PlaybackIsoBmffErAudioPostConfigParser.Parse(
                gaConfig, out var postConfig));
        Assert.Equal(
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult.NeedMoreData,
            PlaybackIsoBmffImplicitSbrSyncExtensionParser
                .ParseAfterErPostConfig(postConfig, out var truncated));
        Assert.Null(truncated);
    }

    [Theory]
    [InlineData(2, 56, 68)]
    [InlineData(3, 57, 69)]
    public void ParseImplicitSbrAfterErAdmission_ContinuesProtectedConfig(
        uint epConfig,
        int expectedSbrEnd,
        int expectedPsEnd)
    {
        var fields = new List<(uint Value, int Count)>
        {
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (epConfig, 2),
            (0, 8), (1, 2), (0, 3), (1, 3), (0, 1),
        };
        if (epConfig == 3)
        {
            fields.Add((1, 1));
        }

        fields.AddRange(
        [
            (0x2B7, 11), (5, 5), (1, 1), (4, 4),
            (0x548, 11), (1, 1),
        ]);
        var config = ErProtectionSpecificConfig(Bits(fields.ToArray()));
        var decision = epConfig == 3
            ? ErDirectMappingDecision(config)
            : null;
        Assert.Equal(
            PlaybackIsoBmffErConfigurationAdmissionResult.Admitted,
            PlaybackIsoBmffErConfigurationAdmissionResolver.Resolve(
                config.Prefix.Requirement,
                config,
                decision,
                out var admission));

        Assert.Equal(
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult.Parsed,
            PlaybackIsoBmffImplicitSbrSyncExtensionParser
                .ParseAfterErConfigurationAdmission(admission, out var sbr));
        var parsedSbr = Assert.IsType<
            PlaybackIsoBmffImplicitSbrSyncExtension>(sbr);
        Assert.Same(
            config.Prefix.Requirement.GaSpecificConfig,
            parsedSbr.GaSpecificConfig);
        Assert.True(parsedSbr.SbrPresent);
        Assert.Equal(44_100u, parsedSbr.ExtensionSamplingFrequency);
        Assert.Equal(expectedSbrEnd, parsedSbr.ConsumedBitLength);

        Assert.Equal(
            PlaybackIsoBmffImplicitPsSyncExtensionParseResult.Parsed,
            PlaybackIsoBmffImplicitPsSyncExtensionParser.Parse(
                parsedSbr, out var ps));
        var parsedPs = Assert.IsType<
            PlaybackIsoBmffImplicitPsSyncExtension>(ps);
        Assert.True(parsedPs.PsPresent);
        Assert.Equal(expectedPsEnd, parsedPs.ConsumedBitLength);
    }

    [Fact]
    public void ParseImplicitSbrAfterErAdmission_RequiresAdmission()
    {
        Assert.Equal(
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
                .MissingErConfigurationAdmission,
            PlaybackIsoBmffImplicitSbrSyncExtensionParser
                .ParseAfterErConfigurationAdmission(null, out var extension));
        Assert.Null(extension);
    }

    [Fact]
    public void ComposeAdmittedErAacFormatIntent_RetainsExactSources()
    {
        var (admission, sbr, ps) = AdmittedErImplicitExtensions(
            epConfig: 2,
            channelConfiguration: 1);

        Assert.Equal(
            PlaybackIsoBmffAdmittedErAacDecoderFormatIntentCompositionResult
                .Composed,
            PlaybackIsoBmffAdmittedErAacDecoderFormatIntentComposer.Compose(
                admission, null, sbr, ps, out var composition));
        var composed = Assert.IsType<
            PlaybackIsoBmffAdmittedErAacDecoderFormatIntent>(composition);
        Assert.Same(admission, composed.Admission);
        Assert.Same(sbr,
            composed.FormatIntent.ExtensionSignallingFacts.ImplicitSbrExtension);
        Assert.Same(ps,
            composed.FormatIntent.ExtensionSignallingFacts.ImplicitPsExtension);
        Assert.Equal(1, composed.FormatIntent.CoreChannelCount);
        Assert.Equal(2, composed.FormatIntent.EffectiveChannelCount);
        Assert.True(composed.FormatIntent.SbrPresent);
        Assert.True(composed.FormatIntent.PsPresent);
    }

    [Fact]
    public void ComposeAdmittedErAacFormatIntent_RejectsMissingAndMismatch()
    {
        Assert.Equal(
            PlaybackIsoBmffAdmittedErAacDecoderFormatIntentCompositionResult
                .MissingAdmission,
            PlaybackIsoBmffAdmittedErAacDecoderFormatIntentComposer.Compose(
                null, null, null, null, out var missing));
        Assert.Null(missing);

        var (firstAdmission, firstSbr, firstPs) =
            AdmittedErImplicitExtensions(2, 1);
        var (secondAdmission, _, _) = AdmittedErImplicitExtensions(2, 1);
        Assert.Equal(
            PlaybackIsoBmffAdmittedErAacDecoderFormatIntentCompositionResult
                .ExtensionSignallingFactsRejected,
            PlaybackIsoBmffAdmittedErAacDecoderFormatIntentComposer.Compose(
                secondAdmission,
                null,
                firstSbr,
                firstPs,
                out var mismatch));
        Assert.Null(mismatch);
        Assert.NotSame(firstAdmission, secondAdmission);
    }

    [Fact]
    public void ComposeAdmittedErAacFormatIntent_RejectsPreAdmissionExtension()
    {
        var (admission, sbr, _) = AdmittedErImplicitExtensions(2, 1);
        var early = new PlaybackIsoBmffImplicitSbrSyncExtension(
            admission.Requirement.GaSpecificConfig,
            sbrPresent: true,
            extensionSamplingFrequencyIndex: 4,
            extensionSamplingFrequency: 44_100,
            consumedBitLength: admission.ConsumedBitLength);

        Assert.Equal(
            PlaybackIsoBmffAdmittedErAacDecoderFormatIntentCompositionResult
                .ImplicitSbrPrecedesAdmission,
            PlaybackIsoBmffAdmittedErAacDecoderFormatIntentComposer.Compose(
                admission, null, early, null, out var rejected));
        Assert.Null(rejected);
    }

    [Fact]
    public void ComposeAdmittedErAacManagedDecoderPreparation_RetainsExactChain()
    {
        var composition = AdmittedErAacFormatIntent();

        Assert.Equal(
            PlaybackIsoBmffAdmittedErAacManagedDecoderPreparationResult.Prepared,
            PlaybackIsoBmffAdmittedErAacManagedDecoderPreparationComposer
                .Compose(
                    composition,
                    PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols,
                    new(PlaybackDecoderFfmpegCodecId.Aac, 8_000, 96_000, 6),
                    out var preparation));
        using var owned = Assert.IsType<
            PlaybackIsoBmffAdmittedErAacManagedDecoderPreparation>(preparation);
        Assert.Same(composition, owned.Composition);
        Assert.Same(composition.Admission, owned.Admission);
        Assert.Same(composition.FormatIntent, owned.FormatIntent);
        Assert.Same(
            composition.Admission,
            owned.DecoderPreparation.Admission);
        Assert.Same(
            composition.FormatIntent,
            owned.DecoderPreparation.FormatIntent);
        Assert.Equal(44_100u, owned.FormatIntent.EffectiveSamplingFrequency);
        Assert.Equal(2, owned.FormatIntent.EffectiveChannelCount);
    }

    [Fact]
    public void ComposeAdmittedErAacManagedDecoderPreparation_RejectsStages()
    {
        var declaration = new PlaybackIsoBmffAacDecoderCapabilityDeclaration(
            PlaybackDecoderFfmpegCodecId.Aac, 8_000, 96_000, 6);
        Assert.Equal(
            PlaybackIsoBmffAdmittedErAacManagedDecoderPreparationResult
                .MissingComposition,
            PlaybackIsoBmffAdmittedErAacManagedDecoderPreparationComposer
                .Compose(
                    null,
                    PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols,
                    declaration,
                    out var missing));
        Assert.Null(missing);

        Assert.Equal(
            PlaybackIsoBmffAdmittedErAacManagedDecoderPreparationResult
                .DecoderPreparationRejected,
            PlaybackIsoBmffAdmittedErAacManagedDecoderPreparationComposer
                .Compose(
                    AdmittedErAacFormatIntent(),
                    Array.AsReadOnly(["avcodec_parameters_alloc"]),
                    declaration,
                    out var rejected));
        Assert.Null(rejected);
    }

    [Fact]
    public async Task
        ComposeAdmittedErAacManagedDecoderPreparation_DisposesManagedLease()
    {
        Assert.Equal(
            PlaybackIsoBmffAdmittedErAacManagedDecoderPreparationResult.Prepared,
            PlaybackIsoBmffAdmittedErAacManagedDecoderPreparationComposer
                .Compose(
                    AdmittedErAacFormatIntent(),
                    PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols,
                    new(PlaybackDecoderFfmpegCodecId.Aac, 8_000, 96_000, 6),
                    out var preparation));
        var owned = Assert.IsType<
            PlaybackIsoBmffAdmittedErAacManagedDecoderPreparation>(preparation);

        await owned.DisposeAsync();

        Assert.True(
            owned.DecoderPreparation.DecoderPreparation.CapabilityBinding.Plan
                .ManagedParameters.Lease.IsDisposed);
        owned.Dispose();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(5, 5)]
    [InlineData(6, 6)]
    [InlineData(7, 8)]
    public void ResolveAacCoreFormatFacts_MapsIndexedChannelConfiguration(
        uint channelConfiguration,
        int channelCount)
    {
        var gaConfig = GaConfig(Bits(
            (2, 5), (4, 4), (channelConfiguration, 4),
            (0, 1), (0, 1), (0, 1)));

        Assert.Equal(
            PlaybackIsoBmffAacCoreFormatFactsResolutionResult.Resolved,
            PlaybackIsoBmffAacCoreFormatFactsResolver.Resolve(
                gaConfig, null, out var facts));
        var resolved = Assert.IsType<PlaybackIsoBmffAacCoreFormatFacts>(facts);
        Assert.Same(gaConfig, resolved.GaSpecificConfig);
        Assert.Null(resolved.ProgramConfigTopology);
        Assert.Equal(2, resolved.AudioObjectType);
        Assert.Equal(44_100u, resolved.DeclaredSamplingFrequency);
        Assert.Equal((byte)channelConfiguration, resolved.ChannelConfiguration);
        Assert.Equal(channelCount, resolved.ChannelCount);
        Assert.False(resolved.UsesProgramConfigElement);
    }

    [Fact]
    public void ResolveAacCoreFormatFacts_UsesExactProgramConfigTopology()
    {
        var element = ProgramConfigElement(Bits(
            (2, 5), (4, 4), (0, 4), (0, 1), (0, 1), (0, 1),
            (0, 4), (1, 2), (4, 4),
            (1, 4), (0, 4), (0, 4), (1, 2), (0, 3), (0, 4),
            (0, 1), (0, 1), (0, 1),
            (1, 1), (2, 4), (3, 4),
            (0, 1), (0, 8)));
        Assert.Equal(
            PlaybackIsoBmffProgramConfigChannelTopologyResult.Projected,
            PlaybackIsoBmffProgramConfigChannelTopologyProjector.Project(
                element, out var topology));
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigCompositionResult.Composed,
            PlaybackIsoBmffGaSpecificConfigComposer.Compose(
                element.Header.GaSpecificConfigPrefix,
                element,
                out var gaConfig));

        Assert.Equal(
            PlaybackIsoBmffAacCoreFormatFactsResolutionResult.Resolved,
            PlaybackIsoBmffAacCoreFormatFactsResolver.Resolve(
                gaConfig, topology, out var facts));
        var resolved = Assert.IsType<PlaybackIsoBmffAacCoreFormatFacts>(facts);
        Assert.Same(topology, resolved.ProgramConfigTopology);
        Assert.Equal(3, resolved.ChannelCount);
        Assert.True(resolved.UsesProgramConfigElement);
    }

    [Fact]
    public void ResolveAacCoreFormatFacts_RejectsMissingUnexpectedAndReserved()
    {
        Assert.Equal(
            PlaybackIsoBmffAacCoreFormatFactsResolutionResult
                .MissingGaSpecificConfig,
            PlaybackIsoBmffAacCoreFormatFactsResolver.Resolve(
                null, null, out var missingGa));
        Assert.Null(missingGa);

        var indexed = GaConfig(Bits(
            (2, 5), (4, 4), (2, 4), (0, 1), (0, 1), (0, 1)));
        var element = ProgramConfigElement(Bits(
            (2, 5), (4, 4), (0, 4), (0, 1), (0, 1), (0, 1),
            (0, 4), (1, 2), (4, 4),
            (0, 4), (0, 4), (0, 4), (0, 2), (0, 3), (0, 4),
            (0, 1), (0, 1), (0, 1), (0, 6), (0, 8)));
        Assert.Equal(
            PlaybackIsoBmffProgramConfigChannelTopologyResult.Projected,
            PlaybackIsoBmffProgramConfigChannelTopologyProjector.Project(
                element, out var topology));
        Assert.Equal(
            PlaybackIsoBmffAacCoreFormatFactsResolutionResult
                .UnexpectedProgramConfigTopology,
            PlaybackIsoBmffAacCoreFormatFactsResolver.Resolve(
                indexed, topology, out var unexpected));
        Assert.Null(unexpected);

        var reserved = GaConfig(Bits(
            (2, 5), (4, 4), (8, 4), (0, 1), (0, 1), (0, 1)));
        Assert.Equal(
            PlaybackIsoBmffAacCoreFormatFactsResolutionResult
                .UnsupportedChannelConfiguration,
            PlaybackIsoBmffAacCoreFormatFactsResolver.Resolve(
                reserved, null, out var unsupported));
        Assert.Null(unsupported);
    }

    [Fact]
    public void ResolveAacCoreFormatFacts_RequiresMatchingPceSource()
    {
        var first = ProgramConfigElement(Bits(
            (2, 5), (4, 4), (0, 4), (0, 1), (0, 1), (0, 1),
            (0, 4), (1, 2), (4, 4),
            (0, 4), (0, 4), (0, 4), (0, 2), (0, 3), (0, 4),
            (0, 1), (0, 1), (0, 1), (0, 6), (0, 8)));
        var second = ProgramConfigElement(Bits(
            (2, 5), (3, 4), (0, 4), (0, 1), (0, 1), (0, 1),
            (0, 4), (1, 2), (3, 4),
            (0, 4), (0, 4), (0, 4), (0, 2), (0, 3), (0, 4),
            (0, 1), (0, 1), (0, 1), (0, 6), (0, 8)));
        Assert.Equal(
            PlaybackIsoBmffProgramConfigChannelTopologyResult.Projected,
            PlaybackIsoBmffProgramConfigChannelTopologyProjector.Project(
                first, out var topology));
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigCompositionResult.Composed,
            PlaybackIsoBmffGaSpecificConfigComposer.Compose(
                second.Header.GaSpecificConfigPrefix,
                second,
                out var gaConfig));
        Assert.Equal(
            PlaybackIsoBmffAacCoreFormatFactsResolutionResult
                .MissingProgramConfigTopology,
            PlaybackIsoBmffAacCoreFormatFactsResolver.Resolve(
                gaConfig, null, out var missing));
        Assert.Null(missing);
        Assert.Equal(
            PlaybackIsoBmffAacCoreFormatFactsResolutionResult
                .ProgramConfigTopologySourceMismatch,
            PlaybackIsoBmffAacCoreFormatFactsResolver.Resolve(
                gaConfig, topology, out var mismatch));
        Assert.Null(mismatch);
    }

    [Fact]
    public void ResolveAacExtensionFacts_NoSignallingKeepsDeclaredRate()
    {
        var coreFacts = CoreFacts(GaConfig(Bits(
            (2, 5), (4, 4), (2, 4), (0, 1), (0, 1), (0, 1))));

        Assert.Equal(
            PlaybackIsoBmffAacExtensionSignallingFactsResolutionResult.Resolved,
            PlaybackIsoBmffAacExtensionSignallingFactsResolver.Resolve(
                coreFacts, null, null, out var facts));
        var resolved =
            Assert.IsType<PlaybackIsoBmffAacExtensionSignallingFacts>(facts);
        Assert.Same(coreFacts, resolved.CoreFormatFacts);
        Assert.Equal(PlaybackIsoBmffAacExtensionSignallingKind.None,
            resolved.SignallingKind);
        Assert.False(resolved.SbrPresent);
        Assert.False(resolved.PsPresent);
        Assert.Equal(44_100u, resolved.EffectiveSamplingFrequency);
    }

    [Fact]
    public void ResolveAacExtensionFacts_UsesExplicitSbrAndPs()
    {
        var header = Header(Bits(
            (29, 5), (7, 4), (2, 4),
            (4, 4), (2, 5),
            (0, 1), (0, 1), (0, 1)));
        Assert.Equal(
            PlaybackIsoBmffExplicitSbrPsExtensionParseResult.Parsed,
            PlaybackIsoBmffExplicitSbrPsExtensionParser.Parse(
                header, out var explicitExtension));
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigPrefixParseResult.Parsed,
            PlaybackIsoBmffGaSpecificConfigPrefixParser.ParseExplicitExtension(
                explicitExtension, out var prefix));
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigCompositionResult.Composed,
            PlaybackIsoBmffGaSpecificConfigComposer.Compose(
                prefix, null, out var gaConfig));
        var coreFacts = CoreFacts(
            Assert.IsType<PlaybackIsoBmffGaSpecificConfig>(gaConfig));

        Assert.Equal(
            PlaybackIsoBmffAacExtensionSignallingFactsResolutionResult.Resolved,
            PlaybackIsoBmffAacExtensionSignallingFactsResolver.Resolve(
                coreFacts, null, null, out var facts));
        var resolved =
            Assert.IsType<PlaybackIsoBmffAacExtensionSignallingFacts>(facts);
        Assert.Equal(PlaybackIsoBmffAacExtensionSignallingKind.Explicit,
            resolved.SignallingKind);
        Assert.True(resolved.SbrPresent);
        Assert.True(resolved.PsPresent);
        Assert.Equal(44_100u, resolved.EffectiveSamplingFrequency);
    }

    [Fact]
    public void ResolveAacExtensionFacts_UsesSourceBoundImplicitSbrAndPs()
    {
        var gaConfig = GaConfig(Bits(
            (2, 5), (4, 4), (2, 4), (0, 1), (0, 1), (0, 1),
            (0x2B7, 11), (5, 5), (1, 1), (3, 4),
            (0x548, 11), (1, 1)));
        Assert.Equal(
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult.Parsed,
            PlaybackIsoBmffImplicitSbrSyncExtensionParser.Parse(
                gaConfig, out var sbr));
        Assert.Equal(
            PlaybackIsoBmffImplicitPsSyncExtensionParseResult.Parsed,
            PlaybackIsoBmffImplicitPsSyncExtensionParser.Parse(
                sbr, out var ps));
        var coreFacts = CoreFacts(gaConfig);

        Assert.Equal(
            PlaybackIsoBmffAacExtensionSignallingFactsResolutionResult.Resolved,
            PlaybackIsoBmffAacExtensionSignallingFactsResolver.Resolve(
                coreFacts, sbr, ps, out var facts));
        var resolved =
            Assert.IsType<PlaybackIsoBmffAacExtensionSignallingFacts>(facts);
        Assert.Equal(PlaybackIsoBmffAacExtensionSignallingKind.Implicit,
            resolved.SignallingKind);
        Assert.Same(sbr, resolved.ImplicitSbrExtension);
        Assert.Same(ps, resolved.ImplicitPsExtension);
        Assert.True(resolved.SbrPresent);
        Assert.True(resolved.PsPresent);
        Assert.Equal(48_000u, resolved.EffectiveSamplingFrequency);
    }

    [Fact]
    public void ResolveAacExtensionFacts_RejectsMissingAndMismatchedSources()
    {
        Assert.Equal(
            PlaybackIsoBmffAacExtensionSignallingFactsResolutionResult
                .MissingCoreFormatFacts,
            PlaybackIsoBmffAacExtensionSignallingFactsResolver.Resolve(
                null, null, null, out var missing));
        Assert.Null(missing);

        var first = GaConfig(Bits(
            (2, 5), (4, 4), (2, 4), (0, 1), (0, 1), (0, 1),
            (0x2B7, 11), (5, 5), (0, 1)));
        Assert.Equal(
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult.Parsed,
            PlaybackIsoBmffImplicitSbrSyncExtensionParser.Parse(
                first, out var firstSbr));
        var secondFacts = CoreFacts(GaConfig(Bits(
            (2, 5), (3, 4), (2, 4), (0, 1), (0, 1), (0, 1))));
        Assert.Equal(
            PlaybackIsoBmffAacExtensionSignallingFactsResolutionResult
                .ImplicitSbrSourceMismatch,
            PlaybackIsoBmffAacExtensionSignallingFactsResolver.Resolve(
                secondFacts, firstSbr, null, out var mismatch));
        Assert.Null(mismatch);
    }

    [Fact]
    public void ResolveAacDecoderFormatIntent_PreservesPlainCoreFormat()
    {
        var coreFacts = CoreFacts(GaConfig(Bits(
            (2, 5), (4, 4), (2, 4), (0, 1), (0, 1), (0, 1))));
        Assert.Equal(
            PlaybackIsoBmffAacExtensionSignallingFactsResolutionResult.Resolved,
            PlaybackIsoBmffAacExtensionSignallingFactsResolver.Resolve(
                coreFacts, null, null, out var extensionFacts));

        Assert.Equal(
            PlaybackIsoBmffAacDecoderFormatIntentResolutionResult.Resolved,
            PlaybackIsoBmffAacDecoderFormatIntentResolver.Resolve(
                extensionFacts, out var intent));
        var resolved = Assert.IsType<PlaybackIsoBmffAacDecoderFormatIntent>(intent);
        Assert.Same(extensionFacts, resolved.ExtensionSignallingFacts);
        Assert.Equal(2, resolved.CoreAudioObjectType);
        Assert.Equal(44_100u, resolved.EffectiveSamplingFrequency);
        Assert.Equal(2, resolved.CoreChannelCount);
        Assert.Equal(2, resolved.EffectiveChannelCount);
        Assert.False(resolved.SbrPresent);
        Assert.False(resolved.PsPresent);
    }

    [Fact]
    public void ResolveAacDecoderFormatIntent_ProjectsMonoPsAsStereo()
    {
        var extensionFacts = ExplicitExtensionFacts(29, 1);

        Assert.Equal(
            PlaybackIsoBmffAacDecoderFormatIntentResolutionResult.Resolved,
            PlaybackIsoBmffAacDecoderFormatIntentResolver.Resolve(
                extensionFacts, out var intent));
        var resolved = Assert.IsType<PlaybackIsoBmffAacDecoderFormatIntent>(intent);
        Assert.Equal(2, resolved.CoreAudioObjectType);
        Assert.Equal(44_100u, resolved.EffectiveSamplingFrequency);
        Assert.Equal(1, resolved.CoreChannelCount);
        Assert.Equal(2, resolved.EffectiveChannelCount);
        Assert.True(resolved.SbrPresent);
        Assert.True(resolved.PsPresent);
    }

    [Fact]
    public void ResolveAacDecoderFormatIntent_RejectsMissingAndNonMonoPs()
    {
        Assert.Equal(
            PlaybackIsoBmffAacDecoderFormatIntentResolutionResult
                .MissingExtensionSignallingFacts,
            PlaybackIsoBmffAacDecoderFormatIntentResolver.Resolve(
                null, out var missing));
        Assert.Null(missing);

        Assert.Equal(
            PlaybackIsoBmffAacDecoderFormatIntentResolutionResult
                .ParametricStereoRequiresMonoCore,
            PlaybackIsoBmffAacDecoderFormatIntentResolver.Resolve(
                ExplicitExtensionFacts(29, 2), out var invalid));
        Assert.Null(invalid);
    }

    [Fact]
    public void MaterializeAacDecoderExtradata_CopiesExactConfiguration()
    {
        var intent = PlainFormatIntent();
        var source = intent.ExtensionSignallingFacts.CoreFormatFacts
            .GaSpecificConfig.AudioSpecificConfigHeader.ConfigurationBytes;

        Assert.Equal(
            PlaybackIsoBmffAacDecoderExtradataMaterializationResult.Materialized,
            PlaybackIsoBmffAacDecoderExtradataMaterializer.Materialize(
                intent, out var extradata));
        var materialized =
            Assert.IsType<PlaybackIsoBmffAacDecoderExtradata>(extradata);
        Assert.Same(intent, materialized.FormatIntent);
        Assert.Equal(source.ToArray(), materialized.Bytes.ToArray());

        var callerCopy = materialized.Bytes.ToArray();
        callerCopy[0] ^= 0xFF;
        Assert.Equal(source.ToArray(), materialized.Bytes.ToArray());
    }

    [Fact]
    public void MaterializeAacDecoderExtradata_RejectsMissingInvalidAndLimit()
    {
        Assert.Equal(
            PlaybackIsoBmffAacDecoderExtradataMaterializationResult
                .MissingFormatIntent,
            PlaybackIsoBmffAacDecoderExtradataMaterializer.Materialize(
                null, out var missing));
        Assert.Null(missing);

        var intent = PlainFormatIntent();
        Assert.Equal(
            PlaybackIsoBmffAacDecoderExtradataMaterializationResult
                .InvalidConfiguration,
            PlaybackIsoBmffAacDecoderExtradataMaterializer.Materialize(
                intent, out var invalid,
                new() { MaximumExtradataBytes = 0 }));
        Assert.Null(invalid);
        Assert.Equal(
            PlaybackIsoBmffAacDecoderExtradataMaterializationResult
                .ConfigurationLimitExceeded,
            PlaybackIsoBmffAacDecoderExtradataMaterializer.Materialize(
                intent, out var limited,
                new()
                {
                    MaximumExtradataBytes = intent.ExtensionSignallingFacts
                        .CoreFormatFacts.GaSpecificConfig
                        .AudioSpecificConfigHeader.ConfigurationBytes.Length - 1,
                }));
        Assert.Null(limited);
    }

    [Fact]
    public void ResolveAacFfmpegInput_ProjectsExactManagedInput()
    {
        var extradata = DecoderExtradata(PlainFormatIntent());

        Assert.Equal(
            PlaybackIsoBmffAacFfmpegCodecParametersInputResolutionResult.Resolved,
            PlaybackIsoBmffAacFfmpegCodecParametersInputResolver.Resolve(
                extradata, out var resolution));
        var resolved = Assert.IsType<
            PlaybackIsoBmffAacFfmpegCodecParametersInputResolution>(resolution);
        Assert.Same(extradata, resolved.DecoderExtradata);
        Assert.Equal(PlaybackDecoderFfmpegCodecId.Aac, resolved.Input.CodecId);
        Assert.Equal(44_100, resolved.Input.SampleRate);
        Assert.Equal(2, resolved.Input.ChannelCount);
        Assert.Equal(0, resolved.Input.Width);
        Assert.Equal(0, resolved.Input.Height);
        Assert.Equal(extradata.Bytes.ToArray(), resolved.Input.Extradata.ToArray());
    }

    [Fact]
    public void ResolveAacFfmpegInput_RejectsMissingAndOutOfRangeRate()
    {
        Assert.Equal(
            PlaybackIsoBmffAacFfmpegCodecParametersInputResolutionResult
                .MissingDecoderExtradata,
            PlaybackIsoBmffAacFfmpegCodecParametersInputResolver.Resolve(
                null, out var missing));
        Assert.Null(missing);

        var coreFacts = CoreFacts(GaConfig(Bits(
            (2, 5), (15, 4), (400_000, 24), (2, 4),
            (0, 1), (0, 1), (0, 1))));
        Assert.Equal(
            PlaybackIsoBmffAacExtensionSignallingFactsResolutionResult.Resolved,
            PlaybackIsoBmffAacExtensionSignallingFactsResolver.Resolve(
                coreFacts, null, null, out var extensionFacts));
        Assert.Equal(
            PlaybackIsoBmffAacDecoderFormatIntentResolutionResult.Resolved,
            PlaybackIsoBmffAacDecoderFormatIntentResolver.Resolve(
                extensionFacts, out var intent));
        var extradata = DecoderExtradata(
            Assert.IsType<PlaybackIsoBmffAacDecoderFormatIntent>(intent));
        Assert.Equal(
            PlaybackIsoBmffAacFfmpegCodecParametersInputResolutionResult
                .SampleRateOutOfRange,
            PlaybackIsoBmffAacFfmpegCodecParametersInputResolver.Resolve(
                extradata, out var outOfRange));
        Assert.Null(outOfRange);
    }

    [Fact]
    public void ResolveAacFfmpegInput_RejectsMoreThanThirtyTwoChannels()
    {
        var fields = new List<(uint Value, int Count)>
        {
            (2, 5), (4, 4), (0, 4), (0, 1), (0, 1), (0, 1),
            (0, 4), (1, 2), (4, 4),
            (15, 4), (1, 4), (0, 4), (1, 2), (0, 3), (0, 4),
            (0, 1), (0, 1), (0, 1),
        };
        for (uint tag = 0; tag < 15; tag++)
        {
            fields.Add((1, 1));
            fields.Add((tag, 4));
        }
        fields.Add((1, 1));
        fields.Add((15, 4));
        fields.Add((0, 4));
        fields.Add((0, 2));
        fields.Add((0, 8));
        var element = ProgramConfigElement(Bits(fields.ToArray()));
        Assert.Equal(
            PlaybackIsoBmffProgramConfigChannelTopologyResult.Projected,
            PlaybackIsoBmffProgramConfigChannelTopologyProjector.Project(
                element, out var topology));
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigCompositionResult.Composed,
            PlaybackIsoBmffGaSpecificConfigComposer.Compose(
                element.Header.GaSpecificConfigPrefix, element, out var gaConfig));
        Assert.Equal(
            PlaybackIsoBmffAacCoreFormatFactsResolutionResult.Resolved,
            PlaybackIsoBmffAacCoreFormatFactsResolver.Resolve(
                gaConfig, topology, out var coreFacts));
        Assert.Equal(
            PlaybackIsoBmffAacExtensionSignallingFactsResolutionResult.Resolved,
            PlaybackIsoBmffAacExtensionSignallingFactsResolver.Resolve(
                coreFacts, null, null, out var extensionFacts));
        Assert.Equal(
            PlaybackIsoBmffAacDecoderFormatIntentResolutionResult.Resolved,
            PlaybackIsoBmffAacDecoderFormatIntentResolver.Resolve(
                extensionFacts, out var intent));
        var extradata = DecoderExtradata(
            Assert.IsType<PlaybackIsoBmffAacDecoderFormatIntent>(intent));

        Assert.Equal(
            PlaybackIsoBmffAacFfmpegCodecParametersInputResolutionResult
                .ChannelCountOutOfRange,
            PlaybackIsoBmffAacFfmpegCodecParametersInputResolver.Resolve(
                extradata, out var outOfRange));
        Assert.Null(outOfRange);
    }

    [Fact]
    public void CreateAacManagedCodecParameters_OwnsZeroPaddedLease()
    {
        var extradata = DecoderExtradata(PlainFormatIntent());
        Assert.Equal(
            PlaybackIsoBmffAacFfmpegCodecParametersInputResolutionResult.Resolved,
            PlaybackIsoBmffAacFfmpegCodecParametersInputResolver.Resolve(
                extradata, out var inputResolution));

        Assert.Equal(
            PlaybackIsoBmffAacManagedCodecParametersCreationResult.Created,
            PlaybackIsoBmffAacManagedCodecParametersFactory.Create(
                inputResolution, out var parameters));
        using var owned =
            Assert.IsType<PlaybackIsoBmffAacManagedCodecParameters>(parameters);
        Assert.Same(inputResolution, owned.InputResolution);
        Assert.Equal(PlaybackDecoderFfmpegCodecId.Aac, owned.Lease.CodecId);
        Assert.Equal(44_100, owned.Lease.SampleRate);
        Assert.Equal(2, owned.Lease.ChannelCount);
        Assert.Equal(extradata.Bytes.Length, owned.Lease.ExtradataLength);
        var snapshot = owned.Lease.UseOwnedParameters(
            static value => (
                value.Extradata.ToArray(),
                value.Padding.ToArray(),
                value.PaddedExtradata.Length));
        Assert.Equal(extradata.Bytes.ToArray(), snapshot.Item1);
        Assert.Equal(
            PlaybackDecoderFfmpegCodecParametersFactory
                .FfmpegInputBufferPaddingSize,
            snapshot.Item2.Length);
        Assert.All(snapshot.Item2,
            static value => Assert.Equal((byte)0, value));
        Assert.Equal(
            extradata.Bytes.Length +
                PlaybackDecoderFfmpegCodecParametersFactory
                    .FfmpegInputBufferPaddingSize,
            snapshot.Item3);
    }

    [Fact]
    public void CreateAacManagedCodecParameters_MissingFailsAndDisposeClearsLease()
    {
        Assert.Equal(
            PlaybackIsoBmffAacManagedCodecParametersCreationResult
                .MissingInputResolution,
            PlaybackIsoBmffAacManagedCodecParametersFactory.Create(
                null, out var missing));
        Assert.Null(missing);

        var extradata = DecoderExtradata(PlainFormatIntent());
        Assert.Equal(
            PlaybackIsoBmffAacFfmpegCodecParametersInputResolutionResult.Resolved,
            PlaybackIsoBmffAacFfmpegCodecParametersInputResolver.Resolve(
                extradata, out var inputResolution));
        Assert.Equal(
            PlaybackIsoBmffAacManagedCodecParametersCreationResult.Created,
            PlaybackIsoBmffAacManagedCodecParametersFactory.Create(
                inputResolution, out var parameters));
        var owned =
            Assert.IsType<PlaybackIsoBmffAacManagedCodecParameters>(parameters);
        owned.Dispose();
        Assert.True(owned.Lease.IsDisposed);
        owned.Dispose();
    }

    [Fact]
    public void PlanAacParameterTransfer_RetainsManagedOwnerAndExactFacts()
    {
        var managed = ManagedCodecParameters(PlainFormatIntent());

        Assert.Equal(
            PlaybackIsoBmffAacParameterTransferPlanningResult.Planned,
            PlaybackIsoBmffAacParameterTransferPlanner.Plan(
                managed, out var plan));
        using var owned =
            Assert.IsType<PlaybackIsoBmffAacParameterTransferPlan>(plan);
        Assert.Same(managed, owned.ManagedParameters);
        Assert.Equal(PlaybackDecoderFfmpegCodecId.Aac, owned.CodecId);
        Assert.Equal(44_100, owned.SampleRate);
        Assert.Equal(2, owned.ChannelCount);
        Assert.Equal(managed.Lease.ExtradataLength, owned.ExtradataLength);
        Assert.Collection(
            PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols,
            static value => Assert.Equal("avcodec_parameters_alloc", value),
            static value => Assert.Equal("avcodec_parameters_free", value),
            static value => Assert.Equal("av_mallocz", value),
            static value => Assert.Equal("av_channel_layout_default", value),
            static value => Assert.Equal("avcodec_parameters_to_context", value));
        Assert.False(managed.Lease.IsDisposed);
    }

    [Fact]
    public void PlanAacParameterTransfer_MissingAndDisposedFailWithoutPlan()
    {
        Assert.Equal(
            PlaybackIsoBmffAacParameterTransferPlanningResult
                .MissingManagedParameters,
            PlaybackIsoBmffAacParameterTransferPlanner.Plan(
                null, out var missing));
        Assert.Null(missing);

        var managed = ManagedCodecParameters(PlainFormatIntent());
        managed.Dispose();

        Assert.Equal(
            PlaybackIsoBmffAacParameterTransferPlanningResult
                .ParametersUnavailable,
            PlaybackIsoBmffAacParameterTransferPlanner.Plan(
                managed, out var unavailable));
        Assert.Null(unavailable);
    }

    [Fact]
    public async Task PlanAacParameterTransfer_DisposeForwardsToManagedOwner()
    {
        var managed = ManagedCodecParameters(PlainFormatIntent());
        Assert.Equal(
            PlaybackIsoBmffAacParameterTransferPlanningResult.Planned,
            PlaybackIsoBmffAacParameterTransferPlanner.Plan(
                managed, out var plan));
        var owned =
            Assert.IsType<PlaybackIsoBmffAacParameterTransferPlan>(plan);

        await owned.DisposeAsync();

        Assert.True(managed.Lease.IsDisposed);
        owned.Dispose();
    }

    [Fact]
    public void VerifyAacTransferSymbols_CoversExactRequirementsWithoutAddresses()
    {
        var plan = ParameterTransferPlan();
        IReadOnlyCollection<string> declared = Array.AsReadOnly(
        [
            "avcodec_open2",
            "av_mallocz",
            "avcodec_parameters_free",
            "avcodec_parameters_to_context",
            "av_channel_layout_default",
            "avcodec_parameters_alloc",
        ]);

        Assert.Equal(
            PlaybackIsoBmffAacParameterTransferSymbolCoverageResult.Covered,
            PlaybackIsoBmffAacParameterTransferSymbolCoverageVerifier.Verify(
                plan, declared, out var coverage));
        using var owned = Assert.IsType<
            PlaybackIsoBmffAacParameterTransferSymbolCoverage>(coverage);
        Assert.Same(plan, owned.Plan);
        Assert.Equal(
            PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols,
            owned.CoveredSymbols);
        Assert.False(plan.ManagedParameters.Lease.IsDisposed);
    }

    [Fact]
    public void VerifyAacTransferSymbols_MissingInputsAndRequirementFailClosed()
    {
        Assert.Equal(
            PlaybackIsoBmffAacParameterTransferSymbolCoverageResult.MissingPlan,
            PlaybackIsoBmffAacParameterTransferSymbolCoverageVerifier.Verify(
                null, Array.Empty<string>(), out var missingPlan));
        Assert.Null(missingPlan);

        using var plan = ParameterTransferPlan();
        Assert.Equal(
            PlaybackIsoBmffAacParameterTransferSymbolCoverageResult
                .MissingDeclaredSymbols,
            PlaybackIsoBmffAacParameterTransferSymbolCoverageVerifier.Verify(
                plan, null, out var missingSymbols));
        Assert.Null(missingSymbols);

        IReadOnlyCollection<string> incomplete = Array.AsReadOnly(
        [
            "avcodec_parameters_alloc",
            "avcodec_parameters_free",
            "av_mallocz",
            "av_channel_layout_default",
        ]);
        Assert.Equal(
            PlaybackIsoBmffAacParameterTransferSymbolCoverageResult
                .MissingRequiredSymbol,
            PlaybackIsoBmffAacParameterTransferSymbolCoverageVerifier.Verify(
                plan, incomplete, out var missingRequired));
        Assert.Null(missingRequired);
    }

    [Fact]
    public async Task VerifyAacTransferSymbols_InvalidFailsAndDisposeConsumesPlan()
    {
        var plan = ParameterTransferPlan();
        Assert.Equal(
            PlaybackIsoBmffAacParameterTransferSymbolCoverageResult
                .InvalidDeclaredSymbol,
            PlaybackIsoBmffAacParameterTransferSymbolCoverageVerifier.Verify(
                plan, Array.AsReadOnly(["avcodec parameters alloc"]),
                out var invalid));
        Assert.Null(invalid);

        Assert.Equal(
            PlaybackIsoBmffAacParameterTransferSymbolCoverageResult.Covered,
            PlaybackIsoBmffAacParameterTransferSymbolCoverageVerifier.Verify(
                plan,
                PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols,
                out var coverage));
        var owned = Assert.IsType<
            PlaybackIsoBmffAacParameterTransferSymbolCoverage>(coverage);

        await owned.DisposeAsync();

        Assert.True(plan.ManagedParameters.Lease.IsDisposed);
        Assert.Equal(
            PlaybackIsoBmffAacParameterTransferSymbolCoverageResult
                .PlanUnavailable,
            PlaybackIsoBmffAacParameterTransferSymbolCoverageVerifier.Verify(
                plan,
                PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols,
                out var unavailable));
        Assert.Null(unavailable);
    }

    [Fact]
    public void BindAacDeclaredCapability_RetainsCoverageAndExactDeclaration()
    {
        var coverage = ParameterTransferSymbolCoverage();
        var declaration = new PlaybackIsoBmffAacDecoderCapabilityDeclaration(
            PlaybackDecoderFfmpegCodecId.Aac,
            MinimumSampleRate: 8_000,
            MaximumSampleRate: 96_000,
            MaximumChannelCount: 6);

        Assert.Equal(
            PlaybackIsoBmffAacDeclaredDecoderCapabilityBindingResult.Bound,
            PlaybackIsoBmffAacDeclaredDecoderCapabilityBinder.Bind(
                coverage, declaration, out var binding));
        using var owned = Assert.IsType<
            PlaybackIsoBmffAacDeclaredDecoderCapabilityBinding>(binding);
        Assert.Same(coverage, owned.SymbolCoverage);
        Assert.Same(coverage.Plan, owned.Plan);
        Assert.Equal(declaration, owned.Declaration);
        Assert.Equal(44_100, owned.Plan.SampleRate);
        Assert.Equal(2, owned.Plan.ChannelCount);
    }

    [Fact]
    public void BindAacDeclaredCapability_MissingInvalidAndCodecMismatchAreDistinct()
    {
        var valid = new PlaybackIsoBmffAacDecoderCapabilityDeclaration(
            PlaybackDecoderFfmpegCodecId.Aac, 8_000, 96_000, 6);
        Assert.Equal(
            PlaybackIsoBmffAacDeclaredDecoderCapabilityBindingResult
                .MissingSymbolCoverage,
            PlaybackIsoBmffAacDeclaredDecoderCapabilityBinder.Bind(
                null, valid, out var missing));
        Assert.Null(missing);

        using var coverage = ParameterTransferSymbolCoverage();
        Assert.Equal(
            PlaybackIsoBmffAacDeclaredDecoderCapabilityBindingResult
                .InvalidDeclaration,
            PlaybackIsoBmffAacDeclaredDecoderCapabilityBinder.Bind(
                coverage,
                new(PlaybackDecoderFfmpegCodecId.Aac, 96_000, 8_000, 6),
                out var invalid));
        Assert.Null(invalid);

        Assert.Equal(
            PlaybackIsoBmffAacDeclaredDecoderCapabilityBindingResult.CodecMismatch,
            PlaybackIsoBmffAacDeclaredDecoderCapabilityBinder.Bind(
                coverage,
                new(PlaybackDecoderFfmpegCodecId.H264, 8_000, 96_000, 6),
                out var mismatch));
        Assert.Null(mismatch);
    }

    [Theory]
    [InlineData(48_000, 96_000, 6,
        PlaybackIsoBmffAacDeclaredDecoderCapabilityBindingResult.SampleRateUnsupported)]
    [InlineData(8_000, 96_000, 1,
        PlaybackIsoBmffAacDeclaredDecoderCapabilityBindingResult.ChannelCountUnsupported)]
    public void BindAacDeclaredCapability_UnsupportedFactsFailClosed(
        int minimumSampleRate,
        int maximumSampleRate,
        int maximumChannelCount,
        PlaybackIsoBmffAacDeclaredDecoderCapabilityBindingResult expected)
    {
        using var coverage = ParameterTransferSymbolCoverage();

        Assert.Equal(
            expected,
            PlaybackIsoBmffAacDeclaredDecoderCapabilityBinder.Bind(
                coverage,
                new(
                    PlaybackDecoderFfmpegCodecId.Aac,
                    MinimumSampleRate: minimumSampleRate,
                    MaximumSampleRate: maximumSampleRate,
                    MaximumChannelCount: maximumChannelCount),
                out var binding));
        Assert.Null(binding);
        Assert.False(coverage.Plan.ManagedParameters.Lease.IsDisposed);
    }

    [Fact]
    public async Task BindAacDeclaredCapability_DisposeConsumesCoverage()
    {
        var coverage = ParameterTransferSymbolCoverage();
        Assert.Equal(
            PlaybackIsoBmffAacDeclaredDecoderCapabilityBindingResult.Bound,
            PlaybackIsoBmffAacDeclaredDecoderCapabilityBinder.Bind(
                coverage,
                new(PlaybackDecoderFfmpegCodecId.Aac, 8_000, 96_000, 6),
                out var binding));
        var owned = Assert.IsType<
            PlaybackIsoBmffAacDeclaredDecoderCapabilityBinding>(binding);

        await owned.DisposeAsync();

        Assert.True(coverage.Plan.ManagedParameters.Lease.IsDisposed);
        Assert.Equal(
            PlaybackIsoBmffAacDeclaredDecoderCapabilityBindingResult
                .CoverageUnavailable,
            PlaybackIsoBmffAacDeclaredDecoderCapabilityBinder.Bind(
                coverage,
                new(PlaybackDecoderFfmpegCodecId.Aac, 8_000, 96_000, 6),
                out var unavailable));
        Assert.Null(unavailable);
        owned.Dispose();
    }

    [Fact]
    public void ComposeAacManagedDecoderPreparation_BuildsExactOwnedChain()
    {
        var intent = PlainFormatIntent();
        var declaration = new PlaybackIsoBmffAacDecoderCapabilityDeclaration(
            PlaybackDecoderFfmpegCodecId.Aac, 8_000, 96_000, 6);

        Assert.Equal(
            PlaybackIsoBmffAacManagedDecoderPreparationResult.Prepared,
            PlaybackIsoBmffAacManagedDecoderPreparationComposer.Compose(
                intent,
                PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols,
                declaration,
                out var preparation));
        using var owned = Assert.IsType<
            PlaybackIsoBmffAacManagedDecoderPreparation>(preparation);
        Assert.Same(intent, owned.FormatIntent);
        Assert.Equal(declaration, owned.CapabilityBinding.Declaration);
        Assert.Equal(PlaybackDecoderFfmpegCodecId.Aac,
            owned.CapabilityBinding.Plan.CodecId);
        Assert.Equal(44_100, owned.CapabilityBinding.Plan.SampleRate);
        Assert.Equal(2, owned.CapabilityBinding.Plan.ChannelCount);
        Assert.Equal(
            intent.ExtensionSignallingFacts.CoreFormatFacts.GaSpecificConfig
                .AudioSpecificConfigHeader.ConfigurationBytes.Length,
            owned.CapabilityBinding.Plan.ExtradataLength);
    }

    [Fact]
    public void ComposeAacManagedDecoderPreparation_RejectsExtradataAndSymbols()
    {
        var declaration = new PlaybackIsoBmffAacDecoderCapabilityDeclaration(
            PlaybackDecoderFfmpegCodecId.Aac, 8_000, 96_000, 6);
        Assert.Equal(
            PlaybackIsoBmffAacManagedDecoderPreparationResult.ExtradataRejected,
            PlaybackIsoBmffAacManagedDecoderPreparationComposer.Compose(
                null,
                PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols,
                declaration,
                out var missing));
        Assert.Null(missing);

        Assert.Equal(
            PlaybackIsoBmffAacManagedDecoderPreparationResult
                .SymbolCoverageRejected,
            PlaybackIsoBmffAacManagedDecoderPreparationComposer.Compose(
                PlainFormatIntent(),
                Array.AsReadOnly(["avcodec_parameters_alloc"]),
                declaration,
                out var incomplete));
        Assert.Null(incomplete);
    }

    [Fact]
    public async Task ComposeAacManagedDecoderPreparation_RejectsCapabilityAndDisposes()
    {
        Assert.Equal(
            PlaybackIsoBmffAacManagedDecoderPreparationResult
                .DeclaredCapabilityRejected,
            PlaybackIsoBmffAacManagedDecoderPreparationComposer.Compose(
                PlainFormatIntent(),
                PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols,
                new(PlaybackDecoderFfmpegCodecId.Aac, 48_000, 96_000, 6),
                out var unsupported));
        Assert.Null(unsupported);

        Assert.Equal(
            PlaybackIsoBmffAacManagedDecoderPreparationResult.Prepared,
            PlaybackIsoBmffAacManagedDecoderPreparationComposer.Compose(
                PlainFormatIntent(),
                PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols,
                new(PlaybackDecoderFfmpegCodecId.Aac, 8_000, 96_000, 6),
                out var preparation));
        var owned = Assert.IsType<
            PlaybackIsoBmffAacManagedDecoderPreparation>(preparation);

        await owned.DisposeAsync();

        Assert.True(owned.CapabilityBinding.Plan.ManagedParameters.Lease.IsDisposed);
        owned.Dispose();
    }

    [Fact]
    public void ComposeErAacManagedDecoderPreparation_RetainsExactSources()
    {
        var requirement = ErProtectionRequirement(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (1, 2)));
        Assert.Equal(
            PlaybackIsoBmffErConfigurationAdmissionResult.Admitted,
            PlaybackIsoBmffErConfigurationAdmissionResolver.Resolve(
                requirement, null, null, out var admission));
        var intent = FormatIntent(requirement.GaSpecificConfig);

        Assert.Equal(
            PlaybackIsoBmffErAacManagedDecoderPreparationResult.Prepared,
            PlaybackIsoBmffErAacManagedDecoderPreparationComposer.Compose(
                admission,
                intent,
                PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols,
                new(PlaybackDecoderFfmpegCodecId.Aac, 8_000, 96_000, 6),
                out var preparation));
        using var owned = Assert.IsType<
            PlaybackIsoBmffErAacManagedDecoderPreparation>(preparation);
        Assert.Same(admission, owned.Admission);
        Assert.Same(intent, owned.FormatIntent);
        Assert.Same(requirement.GaSpecificConfig,
            owned.Admission.Requirement.GaSpecificConfig);
    }

    [Fact]
    public void ComposeErAacManagedDecoderPreparation_RejectsMissingAndMismatch()
    {
        var declaration = new PlaybackIsoBmffAacDecoderCapabilityDeclaration(
            PlaybackDecoderFfmpegCodecId.Aac, 8_000, 96_000, 6);
        Assert.Equal(
            PlaybackIsoBmffErAacManagedDecoderPreparationResult
                .MissingAdmission,
            PlaybackIsoBmffErAacManagedDecoderPreparationComposer.Compose(
                null, PlainFormatIntent(),
                PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols,
                declaration, out var missing));
        Assert.Null(missing);

        var requirement = ErProtectionRequirement(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (0, 2)));
        Assert.Equal(
            PlaybackIsoBmffErConfigurationAdmissionResult.Admitted,
            PlaybackIsoBmffErConfigurationAdmissionResolver.Resolve(
                requirement, null, null, out var admission));
        Assert.Equal(
            PlaybackIsoBmffErAacManagedDecoderPreparationResult.SourceMismatch,
            PlaybackIsoBmffErAacManagedDecoderPreparationComposer.Compose(
                admission, PlainFormatIntent(),
                PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols,
                declaration, out var mismatch));
        Assert.Null(mismatch);
    }

    [Fact]
    public void ComposeErAacManagedDecoderPreparation_RejectsDecoderPreparation()
    {
        var requirement = ErProtectionRequirement(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (0, 2)));
        Assert.Equal(
            PlaybackIsoBmffErConfigurationAdmissionResult.Admitted,
            PlaybackIsoBmffErConfigurationAdmissionResolver.Resolve(
                requirement, null, null, out var admission));

        Assert.Equal(
            PlaybackIsoBmffErAacManagedDecoderPreparationResult
                .DecoderPreparationRejected,
            PlaybackIsoBmffErAacManagedDecoderPreparationComposer.Compose(
                admission,
                FormatIntent(requirement.GaSpecificConfig),
                Array.AsReadOnly(["avcodec_parameters_alloc"]),
                new(PlaybackDecoderFfmpegCodecId.Aac, 8_000, 96_000, 6),
                out var rejected));
        Assert.Null(rejected);
    }

    [Fact]
    public void ProjectAacSampleEntryConsistency_ExactFactsRetainBothSources()
    {
        var entry = AudioEntry(channelCount: 2, sampleRate: 44_100);
        var intent = PlainFormatIntent();

        Assert.Equal(
            PlaybackIsoBmffAacSampleEntryConsistencyFactsResult.Projected,
            PlaybackIsoBmffAacSampleEntryConsistencyFactsProjector.Project(
                entry, intent, out var facts));
        var projected = Assert.IsType<
            PlaybackIsoBmffAacSampleEntryConsistencyFacts>(facts);
        Assert.Same(entry, projected.AudioEntry);
        Assert.Same(intent, projected.FormatIntent);
        Assert.Equal(44_100u << 16, projected.DeclaredSampleRateFixedPoint);
        Assert.Equal((ushort)2, projected.DeclaredChannelCount);
        Assert.Equal(44_100u, projected.EffectiveSamplingFrequency);
        Assert.Equal(2, projected.EffectiveChannelCount);
        Assert.True(projected.HasIntegralDeclaredSampleRate);
        Assert.True(projected.SamplingFrequencyMatches);
        Assert.True(projected.ChannelCountMatches);
        Assert.True(projected.IsExactMatch);
    }

    [Fact]
    public void ProjectAacSampleEntryConsistency_ReportsRateAndChannelMismatch()
    {
        var intent = PlainFormatIntent();
        Assert.Equal(
            PlaybackIsoBmffAacSampleEntryConsistencyFactsResult.Projected,
            PlaybackIsoBmffAacSampleEntryConsistencyFactsProjector.Project(
                AudioEntry(channelCount: 2, sampleRate: 48_000),
                intent,
                out var rateFacts));
        var rate = Assert.IsType<
            PlaybackIsoBmffAacSampleEntryConsistencyFacts>(rateFacts);
        Assert.False(rate.SamplingFrequencyMatches);
        Assert.True(rate.ChannelCountMatches);
        Assert.False(rate.IsExactMatch);

        Assert.Equal(
            PlaybackIsoBmffAacSampleEntryConsistencyFactsResult.Projected,
            PlaybackIsoBmffAacSampleEntryConsistencyFactsProjector.Project(
                AudioEntry(channelCount: 1, sampleRate: 44_100),
                intent,
                out var channelFacts));
        var channel = Assert.IsType<
            PlaybackIsoBmffAacSampleEntryConsistencyFacts>(channelFacts);
        Assert.True(channel.SamplingFrequencyMatches);
        Assert.False(channel.ChannelCountMatches);
        Assert.False(channel.IsExactMatch);
    }

    [Fact]
    public void ProjectAacSampleEntryConsistency_MissingInputsAreDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffAacSampleEntryConsistencyFactsResult.MissingAudioEntry,
            PlaybackIsoBmffAacSampleEntryConsistencyFactsProjector.Project(
                null, PlainFormatIntent(), out var missingEntry));
        Assert.Null(missingEntry);

        Assert.Equal(
            PlaybackIsoBmffAacSampleEntryConsistencyFactsResult.MissingFormatIntent,
            PlaybackIsoBmffAacSampleEntryConsistencyFactsProjector.Project(
                AudioEntry(), null, out var missingIntent));
        Assert.Null(missingIntent);
    }

    [Fact]
    public void ResolveAacSampleEntryConsistency_ExactAgreementNeedsNoOverride()
    {
        var facts = SampleEntryConsistencyFacts(
            AudioEntry(channelCount: 2, sampleRate: 44_100));

        Assert.Equal(
            PlaybackIsoBmffAacSampleEntryConsistencyDecisionResult.Admitted,
            PlaybackIsoBmffAacSampleEntryConsistencyDecisionResolver.Resolve(
                facts,
                PlaybackIsoBmffAacSampleEntryConsistencyPolicy
                    .RequireExactAgreement,
                out var decision));
        var admitted = Assert.IsType<
            PlaybackIsoBmffAacSampleEntryConsistencyDecision>(decision);
        Assert.Same(facts, admitted.Facts);
        Assert.Equal(44_100u, admitted.DecoderSamplingFrequency);
        Assert.Equal(2, admitted.DecoderChannelCount);
        Assert.False(admitted.UsedMismatchOverride);
    }

    [Fact]
    public void ResolveAacSampleEntryConsistency_StrictRejectsMismatch()
    {
        var facts = SampleEntryConsistencyFacts(
            AudioEntry(channelCount: 2, sampleRate: 48_000));

        Assert.Equal(
            PlaybackIsoBmffAacSampleEntryConsistencyDecisionResult
                .MismatchRejected,
            PlaybackIsoBmffAacSampleEntryConsistencyDecisionResolver.Resolve(
                facts,
                PlaybackIsoBmffAacSampleEntryConsistencyPolicy
                    .RequireExactAgreement,
                out var decision));
        Assert.Null(decision);
    }

    [Fact]
    public void ResolveAacSampleEntryConsistency_ExplicitPolicyUsesEffectiveFacts()
    {
        var facts = SampleEntryConsistencyFacts(
            AudioEntry(channelCount: 1, sampleRate: 48_000));

        Assert.Equal(
            PlaybackIsoBmffAacSampleEntryConsistencyDecisionResult.Admitted,
            PlaybackIsoBmffAacSampleEntryConsistencyDecisionResolver.Resolve(
                facts,
                PlaybackIsoBmffAacSampleEntryConsistencyPolicy
                    .AllowEffectiveDecoderFacts,
                out var decision));
        var admitted = Assert.IsType<
            PlaybackIsoBmffAacSampleEntryConsistencyDecision>(decision);
        Assert.Same(facts, admitted.Facts);
        Assert.Equal(44_100u, admitted.DecoderSamplingFrequency);
        Assert.Equal(2, admitted.DecoderChannelCount);
        Assert.True(admitted.UsedMismatchOverride);
    }

    [Fact]
    public void ResolveAacSampleEntryConsistency_MissingAndUndefinedAreDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffAacSampleEntryConsistencyDecisionResult.MissingFacts,
            PlaybackIsoBmffAacSampleEntryConsistencyDecisionResolver.Resolve(
                null,
                PlaybackIsoBmffAacSampleEntryConsistencyPolicy
                    .RequireExactAgreement,
                out var missing));
        Assert.Null(missing);

        Assert.Equal(
            PlaybackIsoBmffAacSampleEntryConsistencyDecisionResult.UndefinedPolicy,
            PlaybackIsoBmffAacSampleEntryConsistencyDecisionResolver.Resolve(
                SampleEntryConsistencyFacts(
                    AudioEntry(channelCount: 2, sampleRate: 44_100)),
                (PlaybackIsoBmffAacSampleEntryConsistencyPolicy)int.MaxValue,
                out var undefined));
        Assert.Null(undefined);
    }

    [Fact]
    public void ComposeAacSampleEntryPreparation_StrictExactRetainsAllSources()
    {
        var entry = AudioEntry(channelCount: 2, sampleRate: 44_100);
        var intent = PlainFormatIntent();
        var declaration = new PlaybackIsoBmffAacDecoderCapabilityDeclaration(
            PlaybackDecoderFfmpegCodecId.Aac, 8_000, 96_000, 6);

        Assert.Equal(
            PlaybackIsoBmffAacSampleEntryManagedDecoderPreparationResult.Prepared,
            PlaybackIsoBmffAacSampleEntryManagedDecoderPreparationComposer.Compose(
                entry,
                intent,
                PlaybackIsoBmffAacSampleEntryConsistencyPolicy
                    .RequireExactAgreement,
                PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols,
                declaration,
                out var preparation));
        using var owned = Assert.IsType<
            PlaybackIsoBmffAacSampleEntryManagedDecoderPreparation>(preparation);
        Assert.Same(entry, owned.AudioEntry);
        Assert.Same(intent, owned.FormatIntent);
        Assert.Same(intent, owned.ConsistencyDecision.Facts.FormatIntent);
        Assert.False(owned.ConsistencyDecision.UsedMismatchOverride);
        Assert.Equal(declaration,
            owned.DecoderPreparation.CapabilityBinding.Declaration);
    }

    [Fact]
    public void ComposeAacSampleEntryPreparation_StrictMismatchFailsBeforeOwner()
    {
        Assert.Equal(
            PlaybackIsoBmffAacSampleEntryManagedDecoderPreparationResult
                .ConsistencyDecisionRejected,
            PlaybackIsoBmffAacSampleEntryManagedDecoderPreparationComposer.Compose(
                AudioEntry(channelCount: 2, sampleRate: 48_000),
                PlainFormatIntent(),
                PlaybackIsoBmffAacSampleEntryConsistencyPolicy
                    .RequireExactAgreement,
                PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols,
                new(PlaybackDecoderFfmpegCodecId.Aac, 8_000, 96_000, 6),
                out var preparation));
        Assert.Null(preparation);
    }

    [Fact]
    public async Task ComposeAacSampleEntryPreparation_ExplicitOverrideOwnsAndDisposes()
    {
        Assert.Equal(
            PlaybackIsoBmffAacSampleEntryManagedDecoderPreparationResult.Prepared,
            PlaybackIsoBmffAacSampleEntryManagedDecoderPreparationComposer.Compose(
                AudioEntry(channelCount: 1, sampleRate: 48_000),
                PlainFormatIntent(),
                PlaybackIsoBmffAacSampleEntryConsistencyPolicy
                    .AllowEffectiveDecoderFacts,
                PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols,
                new(PlaybackDecoderFfmpegCodecId.Aac, 8_000, 96_000, 6),
                out var preparation));
        var owned = Assert.IsType<
            PlaybackIsoBmffAacSampleEntryManagedDecoderPreparation>(preparation);
        Assert.True(owned.ConsistencyDecision.UsedMismatchOverride);
        Assert.Equal(44_100u,
            owned.ConsistencyDecision.DecoderSamplingFrequency);
        Assert.Equal(2, owned.ConsistencyDecision.DecoderChannelCount);

        await owned.DisposeAsync();

        Assert.True(owned.DecoderPreparation.CapabilityBinding.Plan
            .ManagedParameters.Lease.IsDisposed);
        owned.Dispose();
    }

    [Fact]
    public void ComposeAacSampleEntryPreparation_MissingEntryFailsAtFacts()
    {
        Assert.Equal(
            PlaybackIsoBmffAacSampleEntryManagedDecoderPreparationResult
                .ConsistencyFactsRejected,
            PlaybackIsoBmffAacSampleEntryManagedDecoderPreparationComposer.Compose(
                null,
                PlainFormatIntent(),
                PlaybackIsoBmffAacSampleEntryConsistencyPolicy
                    .RequireExactAgreement,
                PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols,
                new(PlaybackDecoderFfmpegCodecId.Aac, 8_000, 96_000, 6),
                out var preparation));
        Assert.Null(preparation);
    }

    private static PlaybackIsoBmffAacSampleEntryConsistencyFacts
        SampleEntryConsistencyFacts(PlaybackIsoBmffAudioSampleEntry audioEntry)
    {
        Assert.Equal(
            PlaybackIsoBmffAacSampleEntryConsistencyFactsResult.Projected,
            PlaybackIsoBmffAacSampleEntryConsistencyFactsProjector.Project(
                audioEntry, PlainFormatIntent(), out var facts));
        return Assert.IsType<
            PlaybackIsoBmffAacSampleEntryConsistencyFacts>(facts);
    }

    private static PlaybackIsoBmffAacParameterTransferSymbolCoverage
        ParameterTransferSymbolCoverage()
    {
        var plan = ParameterTransferPlan();
        Assert.Equal(
            PlaybackIsoBmffAacParameterTransferSymbolCoverageResult.Covered,
            PlaybackIsoBmffAacParameterTransferSymbolCoverageVerifier.Verify(
                plan,
                PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols,
                out var coverage));
        return Assert.IsType<
            PlaybackIsoBmffAacParameterTransferSymbolCoverage>(coverage);
    }

    private static PlaybackIsoBmffAacParameterTransferPlan ParameterTransferPlan()
    {
        var managed = ManagedCodecParameters(PlainFormatIntent());
        Assert.Equal(
            PlaybackIsoBmffAacParameterTransferPlanningResult.Planned,
            PlaybackIsoBmffAacParameterTransferPlanner.Plan(
                managed, out var plan));
        return Assert.IsType<PlaybackIsoBmffAacParameterTransferPlan>(plan);
    }

    private static PlaybackIsoBmffAacManagedCodecParameters ManagedCodecParameters(
        PlaybackIsoBmffAacDecoderFormatIntent intent)
    {
        var extradata = DecoderExtradata(intent);
        Assert.Equal(
            PlaybackIsoBmffAacFfmpegCodecParametersInputResolutionResult.Resolved,
            PlaybackIsoBmffAacFfmpegCodecParametersInputResolver.Resolve(
                extradata, out var inputResolution));
        Assert.Equal(
            PlaybackIsoBmffAacManagedCodecParametersCreationResult.Created,
            PlaybackIsoBmffAacManagedCodecParametersFactory.Create(
                inputResolution, out var parameters));
        return Assert.IsType<PlaybackIsoBmffAacManagedCodecParameters>(parameters);
    }

    private static PlaybackIsoBmffAacDecoderExtradata DecoderExtradata(
        PlaybackIsoBmffAacDecoderFormatIntent intent)
    {
        Assert.Equal(
            PlaybackIsoBmffAacDecoderExtradataMaterializationResult.Materialized,
            PlaybackIsoBmffAacDecoderExtradataMaterializer.Materialize(
                intent, out var extradata));
        return Assert.IsType<PlaybackIsoBmffAacDecoderExtradata>(extradata);
    }

    private static PlaybackIsoBmffAacDecoderFormatIntent PlainFormatIntent()
    {
        return FormatIntent(GaConfig(Bits(
            (2, 5), (4, 4), (2, 4), (0, 1), (0, 1), (0, 1))));
    }

    private static PlaybackIsoBmffAacDecoderFormatIntent FormatIntent(
        PlaybackIsoBmffGaSpecificConfig gaConfig)
    {
        var coreFacts = CoreFacts(gaConfig);
        Assert.Equal(
            PlaybackIsoBmffAacExtensionSignallingFactsResolutionResult.Resolved,
            PlaybackIsoBmffAacExtensionSignallingFactsResolver.Resolve(
                coreFacts, null, null, out var extensionFacts));
        Assert.Equal(
            PlaybackIsoBmffAacDecoderFormatIntentResolutionResult.Resolved,
            PlaybackIsoBmffAacDecoderFormatIntentResolver.Resolve(
                extensionFacts, out var intent));
        return Assert.IsType<PlaybackIsoBmffAacDecoderFormatIntent>(intent);
    }

    private static PlaybackIsoBmffAacExtensionSignallingFacts
        ExplicitExtensionFacts(uint audioObjectType, uint channelConfiguration)
    {
        var header = Header(Bits(
            (audioObjectType, 5), (7, 4), (channelConfiguration, 4),
            (4, 4), (2, 5), (0, 1), (0, 1), (0, 1)));
        Assert.Equal(
            PlaybackIsoBmffExplicitSbrPsExtensionParseResult.Parsed,
            PlaybackIsoBmffExplicitSbrPsExtensionParser.Parse(
                header, out var explicitExtension));
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigPrefixParseResult.Parsed,
            PlaybackIsoBmffGaSpecificConfigPrefixParser.ParseExplicitExtension(
                explicitExtension, out var prefix));
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigCompositionResult.Composed,
            PlaybackIsoBmffGaSpecificConfigComposer.Compose(
                prefix, null, out var gaConfig));
        Assert.Equal(
            PlaybackIsoBmffAacCoreFormatFactsResolutionResult.Resolved,
            PlaybackIsoBmffAacCoreFormatFactsResolver.Resolve(
                gaConfig, null, out var coreFacts));
        Assert.Equal(
            PlaybackIsoBmffAacExtensionSignallingFactsResolutionResult.Resolved,
            PlaybackIsoBmffAacExtensionSignallingFactsResolver.Resolve(
                coreFacts, null, null, out var extensionFacts));
        return Assert.IsType<PlaybackIsoBmffAacExtensionSignallingFacts>(
            extensionFacts);
    }

    private static PlaybackIsoBmffAacCoreFormatFacts CoreFacts(
        PlaybackIsoBmffGaSpecificConfig gaConfig)
    {
        Assert.Equal(
            PlaybackIsoBmffAacCoreFormatFactsResolutionResult.Resolved,
            PlaybackIsoBmffAacCoreFormatFactsResolver.Resolve(
                gaConfig, null, out var facts));
        return Assert.IsType<PlaybackIsoBmffAacCoreFormatFacts>(facts);
    }

    private static PlaybackIsoBmffErAudioProtectionRequirement
        ErProtectionRequirement(byte[] configuration)
    {
        var gaConfig = GaConfig(configuration);
        Assert.Equal(
            PlaybackIsoBmffErAudioProtectionRequirementResult.Projected,
            PlaybackIsoBmffErAudioProtectionRequirementProjector.Project(
                gaConfig, out var requirement));
        return Assert.IsType<PlaybackIsoBmffErAudioProtectionRequirement>(
            requirement);
    }

    private static PlaybackIsoBmffErErrorProtectionSpecificConfigPrefix
        ErProtectionPrefix(byte predefinedSetCount)
    {
        var fields = new List<(uint Value, int Count)>
        {
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (2, 2),
            (predefinedSetCount, 8),
            (1, 2), (0, 3), (1, 3),
        };
        var requirement = ErProtectionRequirement(Bits(fields.ToArray()));
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionSpecificConfigPrefixParseResult
                .Parsed,
            PlaybackIsoBmffErErrorProtectionSpecificConfigPrefixParser.Parse(
                requirement, out var prefix));
        return Assert.IsType<
            PlaybackIsoBmffErErrorProtectionSpecificConfigPrefix>(prefix);
    }

    private static PlaybackIsoBmffErErrorProtectionSpecificConfigPrefix
        ErProtectionPrefix(byte[] configuration)
    {
        var requirement = ErProtectionRequirement(configuration);
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionSpecificConfigPrefixParseResult
                .Parsed,
            PlaybackIsoBmffErErrorProtectionSpecificConfigPrefixParser.Parse(
                requirement, out var prefix));
        return Assert.IsType<
            PlaybackIsoBmffErErrorProtectionSpecificConfigPrefix>(prefix);
    }

    private static PlaybackIsoBmffErErrorProtectionPredefinedSets
        ErProtectionPredefinedSets(byte[] configuration)
    {
        var prefix = ErProtectionPrefix(configuration);
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionPredefinedSetsParseResult.Parsed,
            PlaybackIsoBmffErErrorProtectionPredefinedSetsParser.Parse(
                prefix, out var sets));
        return Assert.IsType<
            PlaybackIsoBmffErErrorProtectionPredefinedSets>(sets);
    }

    private static PlaybackIsoBmffErErrorProtectionHeaderProtection
        ErProtectionHeaderProtection(byte[] configuration)
    {
        var sets = ErProtectionPredefinedSets(configuration);
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionHeaderProtectionParseResult.Parsed,
            PlaybackIsoBmffErErrorProtectionHeaderProtectionParser.Parse(
                sets, out var headerProtection));
        return Assert.IsType<
            PlaybackIsoBmffErErrorProtectionHeaderProtection>(headerProtection);
    }

    private static PlaybackIsoBmffErErrorProtectionSpecificConfig
        ErProtectionSpecificConfig(byte[] configuration)
    {
        var headerProtection = ErProtectionHeaderProtection(configuration);
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionSpecificConfigCompositionResult
                .Composed,
            PlaybackIsoBmffErErrorProtectionSpecificConfigComposer.Compose(
                headerProtection, out var config));
        return Assert.IsType<
            PlaybackIsoBmffErErrorProtectionSpecificConfig>(config);
    }

    private static PlaybackIsoBmffErDirectMappingDecision
        ErDirectMappingDecision(
            PlaybackIsoBmffErErrorProtectionSpecificConfig config)
    {
        Assert.Equal(
            PlaybackIsoBmffErDirectMappingParseResult.Parsed,
            PlaybackIsoBmffErDirectMappingParser.Parse(
                config, out var mapping));
        Assert.Equal(
            PlaybackIsoBmffErDirectMappingDecisionResult.Admitted,
            PlaybackIsoBmffErDirectMappingDecisionResolver.Resolve(
                mapping,
                PlaybackIsoBmffErDirectMappingPolicy.RequireEnabled,
                out var decision));
        return Assert.IsType<PlaybackIsoBmffErDirectMappingDecision>(decision);
    }

    private static (
        PlaybackIsoBmffErConfigurationAdmission Admission,
        PlaybackIsoBmffImplicitSbrSyncExtension Sbr,
        PlaybackIsoBmffImplicitPsSyncExtension Ps)
        AdmittedErImplicitExtensions(
            uint epConfig,
            uint channelConfiguration)
    {
        var fields = new List<(uint Value, int Count)>
        {
            (17, 5), (4, 4), (channelConfiguration, 4),
            (0, 1), (0, 1), (0, 1), (epConfig, 2),
            (0, 8), (1, 2), (0, 3), (1, 3), (0, 1),
        };
        if (epConfig == 3)
        {
            fields.Add((1, 1));
        }

        fields.AddRange(
        [
            (0x2B7, 11), (5, 5), (1, 1), (4, 4),
            (0x548, 11), (1, 1),
        ]);
        var config = ErProtectionSpecificConfig(Bits(fields.ToArray()));
        var decision = epConfig == 3
            ? ErDirectMappingDecision(config)
            : null;
        Assert.Equal(
            PlaybackIsoBmffErConfigurationAdmissionResult.Admitted,
            PlaybackIsoBmffErConfigurationAdmissionResolver.Resolve(
                config.Prefix.Requirement,
                config,
                decision,
                out var admission));
        var admitted = Assert.IsType<
            PlaybackIsoBmffErConfigurationAdmission>(admission);
        Assert.Equal(
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult.Parsed,
            PlaybackIsoBmffImplicitSbrSyncExtensionParser
                .ParseAfterErConfigurationAdmission(admitted, out var sbr));
        var parsedSbr = Assert.IsType<
            PlaybackIsoBmffImplicitSbrSyncExtension>(sbr);
        Assert.Equal(
            PlaybackIsoBmffImplicitPsSyncExtensionParseResult.Parsed,
            PlaybackIsoBmffImplicitPsSyncExtensionParser.Parse(
                parsedSbr, out var ps));
        return (
            admitted,
            parsedSbr,
            Assert.IsType<PlaybackIsoBmffImplicitPsSyncExtension>(ps));
    }

    private static PlaybackIsoBmffAdmittedErAacDecoderFormatIntent
        AdmittedErAacFormatIntent()
    {
        var (admission, sbr, ps) = AdmittedErImplicitExtensions(2, 1);
        Assert.Equal(
            PlaybackIsoBmffAdmittedErAacDecoderFormatIntentCompositionResult
                .Composed,
            PlaybackIsoBmffAdmittedErAacDecoderFormatIntentComposer.Compose(
                admission, null, sbr, ps, out var composition));
        return Assert.IsType<
            PlaybackIsoBmffAdmittedErAacDecoderFormatIntent>(composition);
    }

    private static PlaybackIsoBmffErErrorProtectionFirstSetHeader
        ErProtectionFirstSetHeader(byte[] configuration)
    {
        var requirement = ErProtectionRequirement(configuration);
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionSpecificConfigPrefixParseResult
                .Parsed,
            PlaybackIsoBmffErErrorProtectionSpecificConfigPrefixParser.Parse(
                requirement, out var prefix));
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionFirstSetHeaderParseResult.Parsed,
            PlaybackIsoBmffErErrorProtectionFirstSetHeaderParser.Parse(
                prefix, out var header));
        return Assert.IsType<
            PlaybackIsoBmffErErrorProtectionFirstSetHeader>(header);
    }

    private static PlaybackIsoBmffErErrorProtectionFirstSet
        ErProtectionFirstSet(byte[] configuration)
    {
        var header = ErProtectionFirstSetHeader(configuration);
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionFirstSetParseResult.Parsed,
            PlaybackIsoBmffErErrorProtectionFirstSetParser.Parse(
                header, out var set));
        return Assert.IsType<PlaybackIsoBmffErErrorProtectionFirstSet>(set);
    }

    private static PlaybackIsoBmffErErrorProtectionSecondSetHeader
        ErProtectionSecondSetHeader(byte[] configuration)
    {
        var firstSet = ErProtectionFirstSet(configuration);
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionSecondSetHeaderParseResult.Parsed,
            PlaybackIsoBmffErErrorProtectionSecondSetHeaderParser.Parse(
                firstSet, out var header));
        return Assert.IsType<
            PlaybackIsoBmffErErrorProtectionSecondSetHeader>(header);
    }

    private static PlaybackIsoBmffErErrorProtectionSecondSet
        ErProtectionSecondSet(byte[] configuration)
    {
        var header = ErProtectionSecondSetHeader(configuration);
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionSecondSetParseResult.Parsed,
            PlaybackIsoBmffErErrorProtectionSecondSetParser.Parse(
                header, out var set));
        return Assert.IsType<PlaybackIsoBmffErErrorProtectionSecondSet>(set);
    }

    private static PlaybackIsoBmffErErrorProtectionThirdSetHeader
        ErProtectionThirdSetHeader(byte[] configuration)
    {
        var secondSet = ErProtectionSecondSet(configuration);
        Assert.Equal(
            PlaybackIsoBmffErErrorProtectionThirdSetHeaderParseResult.Parsed,
            PlaybackIsoBmffErErrorProtectionThirdSetHeaderParser.Parse(
                secondSet, out var header));
        return Assert.IsType<
            PlaybackIsoBmffErErrorProtectionThirdSetHeader>(header);
    }

    private static PlaybackIsoBmffGaSpecificConfig GaConfig(
        byte[] configuration)
    {
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigPrefixParseResult.Parsed,
            PlaybackIsoBmffGaSpecificConfigPrefixParser.Parse(
                Header(configuration), out var prefix));
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigCompositionResult.Composed,
            PlaybackIsoBmffGaSpecificConfigComposer.Compose(
                prefix, null, out var config));
        return Assert.IsType<PlaybackIsoBmffGaSpecificConfig>(config);
    }

    private static PlaybackIsoBmffProgramConfigElement ProgramConfigElement(
        byte[] configuration)
    {
        Assert.Equal(
            PlaybackIsoBmffProgramConfigElementParseResult.Parsed,
            PlaybackIsoBmffProgramConfigElementParser.Parse(
                ProgramConfigHeader(configuration), out var element));
        return Assert.IsType<PlaybackIsoBmffProgramConfigElement>(element);
    }

    private static PlaybackIsoBmffProgramConfigChannelSlotMap
        ProgramConfigChannelSlotMap()
    {
        var element = ProgramConfigElement(Bits(
            (2, 5), (4, 4), (0, 4), (0, 1), (0, 1), (0, 1),
            (0, 4), (1, 2), (4, 4),
            (1, 4), (1, 4), (1, 4), (1, 2), (0, 3), (0, 4),
            (0, 1), (0, 1), (0, 1),
            (1, 1), (1, 4), (0, 1), (2, 4), (1, 1), (3, 4),
            (4, 4), (0, 4), (0, 8)));
        Assert.Equal(
            PlaybackIsoBmffProgramConfigChannelTopologyResult.Projected,
            PlaybackIsoBmffProgramConfigChannelTopologyProjector.Project(
                element, out var topology));
        Assert.Equal(
            PlaybackIsoBmffProgramConfigChannelSlotMappingResult.Mapped,
            PlaybackIsoBmffProgramConfigChannelSlotMapper.Map(
                topology, out var map));
        return Assert.IsType<PlaybackIsoBmffProgramConfigChannelSlotMap>(map);
    }

    private static PlaybackIsoBmffProgramConfigSpeakerPositionAssignment
        ProgramConfigSpeakerPositionAssignment()
    {
        var map = ProgramConfigChannelSlotMap();
        Assert.Equal(
            PlaybackIsoBmffProgramConfigSpeakerPositionAssignmentResult.Assigned,
            PlaybackIsoBmffProgramConfigSpeakerPositionAssigner.Assign(
                map,
                [
                    PlaybackIsoBmffProgramConfigSpeakerPosition.FrontLeft,
                    PlaybackIsoBmffProgramConfigSpeakerPosition.FrontRight,
                    PlaybackIsoBmffProgramConfigSpeakerPosition.SideLeft,
                    PlaybackIsoBmffProgramConfigSpeakerPosition.BackLeft,
                    PlaybackIsoBmffProgramConfigSpeakerPosition.BackRight,
                    PlaybackIsoBmffProgramConfigSpeakerPosition.LowFrequency,
                ],
                out var assignment));
        return Assert.IsType<
            PlaybackIsoBmffProgramConfigSpeakerPositionAssignment>(assignment);
    }

    private static PlaybackIsoBmffProgramConfigWaveFormatChannelLayout
        ProgramConfigWaveFormatChannelLayout()
    {
        Assert.Equal(
            PlaybackIsoBmffProgramConfigWaveFormatChannelLayoutResult.Projected,
            PlaybackIsoBmffProgramConfigWaveFormatChannelLayoutProjector.Project(
                ProgramConfigSpeakerPositionAssignment(), out var layout));
        return Assert.IsType<
            PlaybackIsoBmffProgramConfigWaveFormatChannelLayout>(layout);
    }

    private static PlaybackIsoBmffProgramConfigChannelReorderPlan
        ProgramConfigChannelReorderPlan()
    {
        Assert.Equal(
            PlaybackIsoBmffProgramConfigChannelReorderPlanningResult.Planned,
            PlaybackIsoBmffProgramConfigChannelReorderPlanner.Plan(
                ProgramConfigWaveFormatChannelLayout(), out var plan));
        return Assert.IsType<
            PlaybackIsoBmffProgramConfigChannelReorderPlan>(plan);
    }

    private static PlaybackIsoBmffAacDecoderFormatIntent
        ProgramConfigFormatIntent(
            PlaybackIsoBmffProgramConfigChannelReorderPlan plan)
    {
        var topology = plan.Layout.Assignment.SlotMap.Topology;
        var element = topology.ProgramConfigElement;
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigCompositionResult.Composed,
            PlaybackIsoBmffGaSpecificConfigComposer.Compose(
                element.Header.GaSpecificConfigPrefix,
                element,
                out var gaConfig));
        Assert.Equal(
            PlaybackIsoBmffAacCoreFormatFactsResolutionResult.Resolved,
            PlaybackIsoBmffAacCoreFormatFactsResolver.Resolve(
                gaConfig, topology, out var coreFacts));
        Assert.Equal(
            PlaybackIsoBmffAacExtensionSignallingFactsResolutionResult.Resolved,
            PlaybackIsoBmffAacExtensionSignallingFactsResolver.Resolve(
                coreFacts, null, null, out var extensionFacts));
        Assert.Equal(
            PlaybackIsoBmffAacDecoderFormatIntentResolutionResult.Resolved,
            PlaybackIsoBmffAacDecoderFormatIntentResolver.Resolve(
                extensionFacts, out var intent));
        return Assert.IsType<PlaybackIsoBmffAacDecoderFormatIntent>(intent);
    }

    private static PlaybackIsoBmffProgramConfigAacManagedDecoderPreparation
        ProgramConfigAacManagedDecoderPreparation()
    {
        var plan = ProgramConfigChannelReorderPlan();
        Assert.Equal(
            PlaybackIsoBmffProgramConfigAacManagedDecoderPreparationResult
                .Prepared,
            PlaybackIsoBmffProgramConfigAacManagedDecoderPreparationComposer
                .Compose(
                    plan,
                    ProgramConfigFormatIntent(plan),
                    PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols,
                    new(PlaybackDecoderFfmpegCodecId.Aac, 8_000, 96_000, 6),
                    out var preparation));
        return Assert.IsType<
            PlaybackIsoBmffProgramConfigAacManagedDecoderPreparation>(
                preparation);
    }

    private static PlaybackIsoBmffProgramConfigAacChannelLayoutRequirement
        ProgramConfigAacChannelLayoutRequirement(
            PlaybackIsoBmffProgramConfigAacManagedDecoderPreparation preparation)
    {
        Assert.Equal(
            PlaybackIsoBmffProgramConfigAacChannelLayoutRequirementResult
                .Projected,
            PlaybackIsoBmffProgramConfigAacChannelLayoutRequirementProjector
                .Project(preparation, out var requirement));
        return Assert.IsType<
            PlaybackIsoBmffProgramConfigAacChannelLayoutRequirement>(
                requirement);
    }

    private static PlaybackIsoBmffProgramConfigElementHeader ProgramConfigHeader(
        byte[] configuration)
    {
        Assert.Equal(
            PlaybackIsoBmffGaSpecificConfigPrefixParseResult.Parsed,
            PlaybackIsoBmffGaSpecificConfigPrefixParser.Parse(
                Header(configuration), out var prefix));
        Assert.Equal(
            PlaybackIsoBmffProgramConfigElementHeaderParseResult.Parsed,
            PlaybackIsoBmffProgramConfigElementHeaderParser.Parse(
                prefix, out var header));
        return Assert.IsType<PlaybackIsoBmffProgramConfigElementHeader>(header);
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

    private static PlaybackIsoBmffAudioSampleEntry AudioEntry() =>
        AudioEntry(channelCount: 2, sampleRate: 48_000);

    private static PlaybackIsoBmffAudioSampleEntry AudioEntry(
        ushort channelCount,
        ushort sampleRate)
    {
        var selection = new PlaybackIsoBmffEditedSampleDescriptionSelection(
            1,
            BoxLocation(0x6D703461),
            PlaybackIsoBmffSampleEntryCodecCandidate.Mp4a,
            PlaybackIsoBmffHandlerKind.Audio);
        return new(
            selection,
            1,
            channelCount,
            16,
            (uint)sampleRate << 16,
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
