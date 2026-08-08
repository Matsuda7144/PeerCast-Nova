namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffAacCoreFormatFactsResolutionResult
{
    Resolved,
    MissingGaSpecificConfig,
    MissingProgramConfigTopology,
    UnexpectedProgramConfigTopology,
    ProgramConfigTopologySourceMismatch,
    InvalidProgramConfigChannelCount,
    UnsupportedChannelConfiguration,
}

public sealed class PlaybackIsoBmffAacCoreFormatFacts
{
    internal PlaybackIsoBmffAacCoreFormatFacts(
        PlaybackIsoBmffGaSpecificConfig gaSpecificConfig,
        PlaybackIsoBmffProgramConfigChannelTopology? programConfigTopology,
        int channelCount)
    {
        GaSpecificConfig = gaSpecificConfig;
        ProgramConfigTopology = programConfigTopology;
        ChannelCount = channelCount;
    }

    public PlaybackIsoBmffGaSpecificConfig GaSpecificConfig { get; }
    public PlaybackIsoBmffProgramConfigChannelTopology? ProgramConfigTopology { get; }
    public byte AudioObjectType => GaSpecificConfig.AudioObjectType;
    public uint DeclaredSamplingFrequency =>
        GaSpecificConfig.AudioSpecificConfigHeader.SamplingFrequency;
    public byte ChannelConfiguration =>
        GaSpecificConfig.AudioSpecificConfigHeader.ChannelConfiguration;
    public int ChannelCount { get; }
    public bool UsesProgramConfigElement => ProgramConfigTopology is not null;
}

public static class PlaybackIsoBmffAacCoreFormatFactsResolver
{
    public static PlaybackIsoBmffAacCoreFormatFactsResolutionResult Resolve(
        PlaybackIsoBmffGaSpecificConfig? gaSpecificConfig,
        PlaybackIsoBmffProgramConfigChannelTopology? programConfigTopology,
        out PlaybackIsoBmffAacCoreFormatFacts? facts)
    {
        facts = null;
        if (gaSpecificConfig is null)
        {
            return PlaybackIsoBmffAacCoreFormatFactsResolutionResult
                .MissingGaSpecificConfig;
        }

        var channelConfiguration =
            gaSpecificConfig.AudioSpecificConfigHeader.ChannelConfiguration;
        int channelCount;
        if (channelConfiguration == 0)
        {
            if (programConfigTopology is null)
            {
                return PlaybackIsoBmffAacCoreFormatFactsResolutionResult
                    .MissingProgramConfigTopology;
            }

            var prefix = programConfigTopology.ProgramConfigElement.Header
                .GaSpecificConfigPrefix;
            if (!ReferenceEquals(
                    prefix.AudioSpecificConfigHeader,
                    gaSpecificConfig.AudioSpecificConfigHeader) ||
                !ReferenceEquals(
                    prefix.ExplicitSbrPsExtension,
                    gaSpecificConfig.ExplicitSbrPsExtension) ||
                prefix.AudioObjectType != gaSpecificConfig.AudioObjectType)
            {
                return PlaybackIsoBmffAacCoreFormatFactsResolutionResult
                    .ProgramConfigTopologySourceMismatch;
            }

            channelCount = programConfigTopology.TotalChannelCount;
            if (channelCount < 1)
            {
                return PlaybackIsoBmffAacCoreFormatFactsResolutionResult
                    .InvalidProgramConfigChannelCount;
            }
        }
        else
        {
            if (programConfigTopology is not null)
            {
                return PlaybackIsoBmffAacCoreFormatFactsResolutionResult
                    .UnexpectedProgramConfigTopology;
            }

            channelCount = channelConfiguration switch
            {
                1 => 1,
                2 => 2,
                3 => 3,
                4 => 4,
                5 => 5,
                6 => 6,
                7 => 8,
                _ => 0,
            };
            if (channelCount == 0)
            {
                return PlaybackIsoBmffAacCoreFormatFactsResolutionResult
                    .UnsupportedChannelConfiguration;
            }
        }

        facts = new(gaSpecificConfig, programConfigTopology, channelCount);
        return PlaybackIsoBmffAacCoreFormatFactsResolutionResult.Resolved;
    }
}
