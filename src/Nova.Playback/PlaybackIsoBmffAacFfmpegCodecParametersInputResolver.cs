namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffAacFfmpegCodecParametersInputResolutionResult
{
    Resolved,
    MissingDecoderExtradata,
    SampleRateOutOfRange,
    ChannelCountOutOfRange,
}

public sealed class PlaybackIsoBmffAacFfmpegCodecParametersInputResolution
{
    internal PlaybackIsoBmffAacFfmpegCodecParametersInputResolution(
        PlaybackIsoBmffAacDecoderExtradata decoderExtradata,
        PlaybackDecoderFfmpegCodecParametersInput input)
    {
        DecoderExtradata = decoderExtradata;
        Input = input;
    }

    public PlaybackIsoBmffAacDecoderExtradata DecoderExtradata { get; }
    public PlaybackDecoderFfmpegCodecParametersInput Input { get; }
}

public static class PlaybackIsoBmffAacFfmpegCodecParametersInputResolver
{
    public static PlaybackIsoBmffAacFfmpegCodecParametersInputResolutionResult
        Resolve(
            PlaybackIsoBmffAacDecoderExtradata? decoderExtradata,
            out PlaybackIsoBmffAacFfmpegCodecParametersInputResolution?
                resolution)
    {
        resolution = null;
        if (decoderExtradata is null)
        {
            return PlaybackIsoBmffAacFfmpegCodecParametersInputResolutionResult
                .MissingDecoderExtradata;
        }

        var intent = decoderExtradata.FormatIntent;
        if (intent.EffectiveSamplingFrequency is <
                PlaybackDecoderFfmpegCodecParametersFactory.MinimumAudioSampleRate or
            > PlaybackDecoderFfmpegCodecParametersFactory.MaximumAudioSampleRate)
        {
            return PlaybackIsoBmffAacFfmpegCodecParametersInputResolutionResult
                .SampleRateOutOfRange;
        }

        if (intent.EffectiveChannelCount is <
                PlaybackDecoderFfmpegCodecParametersFactory.MinimumAudioChannelCount or
            > PlaybackDecoderFfmpegCodecParametersFactory.MaximumAudioChannelCount)
        {
            return PlaybackIsoBmffAacFfmpegCodecParametersInputResolutionResult
                .ChannelCountOutOfRange;
        }

        var input = PlaybackDecoderFfmpegCodecParametersInput.ForAac(
            checked((int)intent.EffectiveSamplingFrequency),
            intent.EffectiveChannelCount,
            decoderExtradata.Bytes);
        resolution = new(decoderExtradata, input);
        return PlaybackIsoBmffAacFfmpegCodecParametersInputResolutionResult
            .Resolved;
    }
}
