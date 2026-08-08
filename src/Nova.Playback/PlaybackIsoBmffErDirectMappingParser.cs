namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffErDirectMappingParseResult
{
    Parsed,
    MissingSpecificConfig,
    NotRequired,
    NeedMoreData,
}

public sealed class PlaybackIsoBmffErDirectMapping
{
    internal PlaybackIsoBmffErDirectMapping(
        PlaybackIsoBmffErErrorProtectionSpecificConfig specificConfig,
        bool enabled,
        int consumedBitLength)
    {
        SpecificConfig = specificConfig;
        Enabled = enabled;
        ConsumedBitLength = consumedBitLength;
    }

    public PlaybackIsoBmffErErrorProtectionSpecificConfig SpecificConfig
    { get; }
    public bool Enabled { get; }
    public int ConsumedBitLength { get; }
}

public static class PlaybackIsoBmffErDirectMappingParser
{
    public static PlaybackIsoBmffErDirectMappingParseResult Parse(
        PlaybackIsoBmffErErrorProtectionSpecificConfig? specificConfig,
        out PlaybackIsoBmffErDirectMapping? directMapping)
    {
        directMapping = null;
        if (specificConfig is null)
            return PlaybackIsoBmffErDirectMappingParseResult.MissingSpecificConfig;

        if (specificConfig.Prefix.Requirement.EpConfig != 3)
            return PlaybackIsoBmffErDirectMappingParseResult.NotRequired;

        var bytes = specificConfig.Prefix.Requirement.GaSpecificConfig
            .AudioSpecificConfigHeader.ConfigurationBytes.Span;
        var position = specificConfig.ConsumedBitLength;
        if (position < 0 || position >= bytes.Length * 8L)
            return PlaybackIsoBmffErDirectMappingParseResult.NeedMoreData;

        var enabled = ((bytes[position / 8] >> (7 - (position % 8))) & 1) != 0;
        directMapping = new(specificConfig, enabled, position + 1);
        return PlaybackIsoBmffErDirectMappingParseResult.Parsed;
    }
}
