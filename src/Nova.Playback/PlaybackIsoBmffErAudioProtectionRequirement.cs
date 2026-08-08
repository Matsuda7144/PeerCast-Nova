namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffErAudioProtectionRequirementResult
{
    Projected,
    MissingGaSpecificConfig,
    NotRequired,
    NeedMoreData,
}

public sealed class PlaybackIsoBmffErAudioProtectionRequirement
{
    internal PlaybackIsoBmffErAudioProtectionRequirement(
        PlaybackIsoBmffGaSpecificConfig gaSpecificConfig,
        byte epConfig,
        int consumedBitLength)
    {
        GaSpecificConfig = gaSpecificConfig;
        EpConfig = epConfig;
        ConsumedBitLength = consumedBitLength;
    }

    public PlaybackIsoBmffGaSpecificConfig GaSpecificConfig { get; }

    public byte EpConfig { get; }

    public bool RequiresErrorProtectionSpecificConfig => EpConfig >= 2;

    public int ConsumedBitLength { get; }
}

public static class PlaybackIsoBmffErAudioProtectionRequirementProjector
{
    public static PlaybackIsoBmffErAudioProtectionRequirementResult Project(
        PlaybackIsoBmffGaSpecificConfig? gaSpecificConfig,
        out PlaybackIsoBmffErAudioProtectionRequirement? requirement)
    {
        requirement = null;
        if (gaSpecificConfig is null)
        {
            return PlaybackIsoBmffErAudioProtectionRequirementResult
                .MissingGaSpecificConfig;
        }

        if (gaSpecificConfig.AudioObjectType is not (
            17 or 19 or 20 or 21 or 22 or 23))
        {
            return PlaybackIsoBmffErAudioProtectionRequirementResult.NotRequired;
        }

        var bytes = gaSpecificConfig.AudioSpecificConfigHeader
            .ConfigurationBytes.Span;
        var position = gaSpecificConfig.ConsumedBitLength;
        if (position < 0 || 2 > (bytes.Length * 8L) - position)
        {
            return PlaybackIsoBmffErAudioProtectionRequirementResult.NeedMoreData;
        }

        var epConfig = (byte)(
            ((bytes[position / 8] >> (7 - (position % 8))) & 1) << 1);
        position++;
        epConfig |= (byte)(
            (bytes[position / 8] >> (7 - (position % 8))) & 1);
        position++;

        requirement = new(gaSpecificConfig, epConfig, position);
        return PlaybackIsoBmffErAudioProtectionRequirementResult.Projected;
    }
}
