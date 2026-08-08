namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffErErrorProtectionSpecificConfigCompositionResult
{
    Composed,
    MissingHeaderProtection,
}

public sealed class PlaybackIsoBmffErErrorProtectionSpecificConfig
{
    internal PlaybackIsoBmffErErrorProtectionSpecificConfig(
        PlaybackIsoBmffErErrorProtectionHeaderProtection headerProtection)
    {
        HeaderProtection = headerProtection;
        Prefix = headerProtection.PredefinedSets.Prefix;
        PredefinedSets = headerProtection.PredefinedSets;
        ConsumedBitLength = headerProtection.ConsumedBitLength;
    }

    public PlaybackIsoBmffErErrorProtectionSpecificConfigPrefix Prefix { get; }
    public PlaybackIsoBmffErErrorProtectionPredefinedSets PredefinedSets { get; }
    public PlaybackIsoBmffErErrorProtectionHeaderProtection HeaderProtection
    { get; }
    public int ConsumedBitLength { get; }
}

public static class PlaybackIsoBmffErErrorProtectionSpecificConfigComposer
{
    public static PlaybackIsoBmffErErrorProtectionSpecificConfigCompositionResult
        Compose(
            PlaybackIsoBmffErErrorProtectionHeaderProtection? headerProtection,
            out PlaybackIsoBmffErErrorProtectionSpecificConfig? config)
    {
        config = null;
        if (headerProtection is null)
        {
            return PlaybackIsoBmffErErrorProtectionSpecificConfigCompositionResult
                .MissingHeaderProtection;
        }

        config = new(headerProtection);
        return PlaybackIsoBmffErErrorProtectionSpecificConfigCompositionResult
            .Composed;
    }
}
