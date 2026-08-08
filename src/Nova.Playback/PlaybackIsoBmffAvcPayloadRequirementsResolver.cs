namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffAvcPayloadRequirementsResolveResult
{
    Resolved,
    MissingRequirements,
}

public sealed record PlaybackIsoBmffAvcPayloadRequirements
{
    public bool RequireAnnexB { get; init; }

    public bool IncludeConfigurationParameterSets { get; init; }
}

public sealed class PlaybackIsoBmffAvcPayloadRequirementsResolution
{
    internal PlaybackIsoBmffAvcPayloadRequirementsResolution(
        PlaybackIsoBmffAvcPayloadRequirements requirements,
        PlaybackIsoBmffAvcPayloadFormat format)
    {
        Requirements = requirements;
        Format = format;
    }

    public PlaybackIsoBmffAvcPayloadRequirements Requirements { get; }

    public PlaybackIsoBmffAvcPayloadFormat Format { get; }
}

public static class PlaybackIsoBmffAvcPayloadRequirementsResolver
{
    public static PlaybackIsoBmffAvcPayloadRequirementsResolveResult Resolve(
        PlaybackIsoBmffAvcPayloadRequirements? requirements,
        out PlaybackIsoBmffAvcPayloadRequirementsResolution? resolution)
    {
        resolution = null;
        if (requirements is null)
        {
            return PlaybackIsoBmffAvcPayloadRequirementsResolveResult
                .MissingRequirements;
        }

        var format = (requirements.RequireAnnexB,
            requirements.IncludeConfigurationParameterSets) switch
        {
            (false, false) =>
                PlaybackIsoBmffAvcPayloadFormat.OriginalLengthPrefixed,
            (false, true) => PlaybackIsoBmffAvcPayloadFormat
                .ParameterSetPrefixedLengthPrefixed,
            (true, false) =>
                PlaybackIsoBmffAvcPayloadFormat.SampleOnlyAnnexB,
            (true, true) => PlaybackIsoBmffAvcPayloadFormat
                .ParameterSetPrefixedAnnexB,
        };
        resolution = new(requirements, format);
        return PlaybackIsoBmffAvcPayloadRequirementsResolveResult.Resolved;
    }
}
