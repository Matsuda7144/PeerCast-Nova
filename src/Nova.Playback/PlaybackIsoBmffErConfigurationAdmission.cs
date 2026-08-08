namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffErConfigurationAdmissionResult
{
    Admitted,
    MissingRequirement,
    MissingSpecificConfig,
    SpecificConfigSourceMismatch,
    MissingDirectMappingDecision,
    DirectMappingSourceMismatch,
}

public sealed class PlaybackIsoBmffErConfigurationAdmission
{
    internal PlaybackIsoBmffErConfigurationAdmission(
        PlaybackIsoBmffErAudioProtectionRequirement requirement,
        PlaybackIsoBmffErErrorProtectionSpecificConfig? specificConfig,
        PlaybackIsoBmffErDirectMappingDecision? directMappingDecision)
    {
        Requirement = requirement;
        SpecificConfig = specificConfig;
        DirectMappingDecision = directMappingDecision;
    }

    public PlaybackIsoBmffErAudioProtectionRequirement Requirement { get; }
    public PlaybackIsoBmffErErrorProtectionSpecificConfig? SpecificConfig { get; }
    public PlaybackIsoBmffErDirectMappingDecision? DirectMappingDecision { get; }
    public int ConsumedBitLength => DirectMappingDecision?.Mapping.ConsumedBitLength
        ?? SpecificConfig?.ConsumedBitLength ?? Requirement.ConsumedBitLength;
}

public static class PlaybackIsoBmffErConfigurationAdmissionResolver
{
    public static PlaybackIsoBmffErConfigurationAdmissionResult Resolve(
        PlaybackIsoBmffErAudioProtectionRequirement? requirement,
        PlaybackIsoBmffErErrorProtectionSpecificConfig? specificConfig,
        PlaybackIsoBmffErDirectMappingDecision? directMappingDecision,
        out PlaybackIsoBmffErConfigurationAdmission? admission)
    {
        admission = null;
        if (requirement is null)
            return PlaybackIsoBmffErConfigurationAdmissionResult.MissingRequirement;

        if (requirement.EpConfig < 2)
        {
            admission = new(requirement, null, null);
            return PlaybackIsoBmffErConfigurationAdmissionResult.Admitted;
        }

        if (specificConfig is null)
            return PlaybackIsoBmffErConfigurationAdmissionResult.MissingSpecificConfig;
        if (!ReferenceEquals(specificConfig.Prefix.Requirement, requirement))
            return PlaybackIsoBmffErConfigurationAdmissionResult
                .SpecificConfigSourceMismatch;

        if (requirement.EpConfig == 3)
        {
            if (directMappingDecision is null)
                return PlaybackIsoBmffErConfigurationAdmissionResult
                    .MissingDirectMappingDecision;
            if (!ReferenceEquals(
                    directMappingDecision.Mapping.SpecificConfig, specificConfig))
                return PlaybackIsoBmffErConfigurationAdmissionResult
                    .DirectMappingSourceMismatch;
        }

        admission = new(requirement, specificConfig,
            requirement.EpConfig == 3 ? directMappingDecision : null);
        return PlaybackIsoBmffErConfigurationAdmissionResult.Admitted;
    }
}
