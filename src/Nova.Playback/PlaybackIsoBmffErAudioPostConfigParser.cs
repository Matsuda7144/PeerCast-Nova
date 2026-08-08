namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffErAudioPostConfigParseResult
{
    Parsed,
    MissingGaSpecificConfig,
    NotRequired,
    UnsupportedErrorProtectionSpecificConfig,
    NeedMoreData,
}

public sealed class PlaybackIsoBmffErAudioPostConfig
{
    internal PlaybackIsoBmffErAudioPostConfig(
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
    public int ConsumedBitLength { get; }
}

public static class PlaybackIsoBmffErAudioPostConfigParser
{
    public static PlaybackIsoBmffErAudioPostConfigParseResult Parse(
        PlaybackIsoBmffGaSpecificConfig? gaSpecificConfig,
        out PlaybackIsoBmffErAudioPostConfig? postConfig)
    {
        postConfig = null;
        if (gaSpecificConfig is null)
        {
            return PlaybackIsoBmffErAudioPostConfigParseResult
                .MissingGaSpecificConfig;
        }

        if (gaSpecificConfig.AudioObjectType is not (
            17 or 19 or 20 or 21 or 22 or 23))
        {
            return PlaybackIsoBmffErAudioPostConfigParseResult.NotRequired;
        }

        var bytes = gaSpecificConfig.AudioSpecificConfigHeader
            .ConfigurationBytes.Span;
        var position = gaSpecificConfig.ConsumedBitLength;
        if (position < 0 || 2 > (bytes.Length * 8L) - position)
        {
            return PlaybackIsoBmffErAudioPostConfigParseResult.NeedMoreData;
        }

        var epConfig = (byte)(
            ((bytes[position / 8] >> (7 - (position % 8))) & 1) << 1);
        position++;
        epConfig |= (byte)(
            (bytes[position / 8] >> (7 - (position % 8))) & 1);
        position++;
        if (epConfig >= 2)
        {
            return PlaybackIsoBmffErAudioPostConfigParseResult
                .UnsupportedErrorProtectionSpecificConfig;
        }

        postConfig = new(gaSpecificConfig, epConfig, position);
        return PlaybackIsoBmffErAudioPostConfigParseResult.Parsed;
    }
}
