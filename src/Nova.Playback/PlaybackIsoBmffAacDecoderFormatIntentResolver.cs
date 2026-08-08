namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffAacDecoderFormatIntentResolutionResult
{
    Resolved,
    MissingExtensionSignallingFacts,
    ParametricStereoRequiresMonoCore,
}

public sealed class PlaybackIsoBmffAacDecoderFormatIntent
{
    internal PlaybackIsoBmffAacDecoderFormatIntent(
        PlaybackIsoBmffAacExtensionSignallingFacts extensionSignallingFacts,
        int effectiveChannelCount)
    {
        ExtensionSignallingFacts = extensionSignallingFacts;
        EffectiveChannelCount = effectiveChannelCount;
    }

    public PlaybackIsoBmffAacExtensionSignallingFacts ExtensionSignallingFacts
    {
        get;
    }

    public byte CoreAudioObjectType =>
        ExtensionSignallingFacts.CoreFormatFacts.AudioObjectType;
    public uint EffectiveSamplingFrequency =>
        ExtensionSignallingFacts.EffectiveSamplingFrequency;
    public int CoreChannelCount =>
        ExtensionSignallingFacts.CoreFormatFacts.ChannelCount;
    public int EffectiveChannelCount { get; }
    public bool SbrPresent => ExtensionSignallingFacts.SbrPresent;
    public bool PsPresent => ExtensionSignallingFacts.PsPresent;
}

public static class PlaybackIsoBmffAacDecoderFormatIntentResolver
{
    public static PlaybackIsoBmffAacDecoderFormatIntentResolutionResult Resolve(
        PlaybackIsoBmffAacExtensionSignallingFacts? extensionSignallingFacts,
        out PlaybackIsoBmffAacDecoderFormatIntent? intent)
    {
        intent = null;
        if (extensionSignallingFacts is null)
        {
            return PlaybackIsoBmffAacDecoderFormatIntentResolutionResult
                .MissingExtensionSignallingFacts;
        }

        var coreChannelCount =
            extensionSignallingFacts.CoreFormatFacts.ChannelCount;
        if (extensionSignallingFacts.PsPresent && coreChannelCount != 1)
        {
            return PlaybackIsoBmffAacDecoderFormatIntentResolutionResult
                .ParametricStereoRequiresMonoCore;
        }

        intent = new(
            extensionSignallingFacts,
            extensionSignallingFacts.PsPresent ? 2 : coreChannelCount);
        return PlaybackIsoBmffAacDecoderFormatIntentResolutionResult.Resolved;
    }
}
