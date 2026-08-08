namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffAacDecoderExtradataMaterializationResult
{
    Materialized,
    InvalidConfiguration,
    MissingFormatIntent,
    EmptyConfiguration,
    ConfigurationLimitExceeded,
}

public sealed record PlaybackIsoBmffAacDecoderExtradataMaterializerOptions
{
    public int MaximumExtradataBytes { get; init; } =
        PlaybackBoundedDemuxOptions.DefaultMaximumCodecExtradataBytes;
}

public sealed class PlaybackIsoBmffAacDecoderExtradata
{
    internal PlaybackIsoBmffAacDecoderExtradata(
        PlaybackIsoBmffAacDecoderFormatIntent formatIntent,
        byte[] bytes)
    {
        FormatIntent = formatIntent;
        Bytes = bytes;
    }

    public PlaybackIsoBmffAacDecoderFormatIntent FormatIntent { get; }
    public ReadOnlyMemory<byte> Bytes { get; }
}

public static class PlaybackIsoBmffAacDecoderExtradataMaterializer
{
    public static PlaybackIsoBmffAacDecoderExtradataMaterializationResult
        Materialize(
            PlaybackIsoBmffAacDecoderFormatIntent? formatIntent,
            out PlaybackIsoBmffAacDecoderExtradata? extradata,
            PlaybackIsoBmffAacDecoderExtradataMaterializerOptions? options = null)
    {
        extradata = null;
        options ??= new PlaybackIsoBmffAacDecoderExtradataMaterializerOptions();
        if (options.MaximumExtradataBytes is < 1 or
            > PlaybackBoundedDemuxOptions.MaximumCodecExtradataBytesHardLimit)
        {
            return PlaybackIsoBmffAacDecoderExtradataMaterializationResult
                .InvalidConfiguration;
        }

        if (formatIntent is null)
        {
            return PlaybackIsoBmffAacDecoderExtradataMaterializationResult
                .MissingFormatIntent;
        }

        var configuration = formatIntent.ExtensionSignallingFacts.CoreFormatFacts
            .GaSpecificConfig.AudioSpecificConfigHeader.ConfigurationBytes;
        if (configuration.IsEmpty)
        {
            return PlaybackIsoBmffAacDecoderExtradataMaterializationResult
                .EmptyConfiguration;
        }

        if (configuration.Length > options.MaximumExtradataBytes)
        {
            return PlaybackIsoBmffAacDecoderExtradataMaterializationResult
                .ConfigurationLimitExceeded;
        }

        extradata = new(formatIntent, configuration.ToArray());
        return PlaybackIsoBmffAacDecoderExtradataMaterializationResult
            .Materialized;
    }
}
