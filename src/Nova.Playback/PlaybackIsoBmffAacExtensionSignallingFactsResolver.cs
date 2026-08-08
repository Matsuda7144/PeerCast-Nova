namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffAacExtensionSignallingKind
{
    None,
    Explicit,
    Implicit,
}

public enum PlaybackIsoBmffAacExtensionSignallingFactsResolutionResult
{
    Resolved,
    MissingCoreFormatFacts,
    ExplicitAndImplicitSignallingConflict,
    ImplicitSbrSourceMismatch,
    ImplicitPsWithoutSbr,
    ImplicitPsSourceMismatch,
}

public sealed class PlaybackIsoBmffAacExtensionSignallingFacts
{
    internal PlaybackIsoBmffAacExtensionSignallingFacts(
        PlaybackIsoBmffAacCoreFormatFacts coreFormatFacts,
        PlaybackIsoBmffAacExtensionSignallingKind signallingKind,
        PlaybackIsoBmffImplicitSbrSyncExtension? implicitSbrExtension,
        PlaybackIsoBmffImplicitPsSyncExtension? implicitPsExtension,
        bool sbrPresent,
        bool psPresent,
        uint effectiveSamplingFrequency)
    {
        CoreFormatFacts = coreFormatFacts;
        SignallingKind = signallingKind;
        ImplicitSbrExtension = implicitSbrExtension;
        ImplicitPsExtension = implicitPsExtension;
        SbrPresent = sbrPresent;
        PsPresent = psPresent;
        EffectiveSamplingFrequency = effectiveSamplingFrequency;
    }

    public PlaybackIsoBmffAacCoreFormatFacts CoreFormatFacts { get; }
    public PlaybackIsoBmffAacExtensionSignallingKind SignallingKind { get; }
    public PlaybackIsoBmffImplicitSbrSyncExtension? ImplicitSbrExtension { get; }
    public PlaybackIsoBmffImplicitPsSyncExtension? ImplicitPsExtension { get; }
    public bool SbrPresent { get; }
    public bool PsPresent { get; }
    public uint EffectiveSamplingFrequency { get; }
}

public static class PlaybackIsoBmffAacExtensionSignallingFactsResolver
{
    public static PlaybackIsoBmffAacExtensionSignallingFactsResolutionResult
        Resolve(
            PlaybackIsoBmffAacCoreFormatFacts? coreFormatFacts,
            PlaybackIsoBmffImplicitSbrSyncExtension? implicitSbrExtension,
            PlaybackIsoBmffImplicitPsSyncExtension? implicitPsExtension,
            out PlaybackIsoBmffAacExtensionSignallingFacts? facts)
    {
        facts = null;
        if (coreFormatFacts is null)
        {
            return PlaybackIsoBmffAacExtensionSignallingFactsResolutionResult
                .MissingCoreFormatFacts;
        }

        var gaConfig = coreFormatFacts.GaSpecificConfig;
        var explicitExtension = gaConfig.ExplicitSbrPsExtension;
        if (explicitExtension is not null)
        {
            if (implicitSbrExtension is not null || implicitPsExtension is not null)
            {
                return PlaybackIsoBmffAacExtensionSignallingFactsResolutionResult
                    .ExplicitAndImplicitSignallingConflict;
            }

            facts = new(
                coreFormatFacts,
                PlaybackIsoBmffAacExtensionSignallingKind.Explicit,
                null,
                null,
                explicitExtension.SbrPresent,
                explicitExtension.PsPresent,
                explicitExtension.SbrPresent
                    ? explicitExtension.ExtensionSamplingFrequency
                    : coreFormatFacts.DeclaredSamplingFrequency);
            return PlaybackIsoBmffAacExtensionSignallingFactsResolutionResult
                .Resolved;
        }

        if (implicitSbrExtension is not null &&
            !ReferenceEquals(implicitSbrExtension.GaSpecificConfig, gaConfig))
        {
            return PlaybackIsoBmffAacExtensionSignallingFactsResolutionResult
                .ImplicitSbrSourceMismatch;
        }

        if (implicitPsExtension is not null)
        {
            if (implicitSbrExtension is null)
            {
                return PlaybackIsoBmffAacExtensionSignallingFactsResolutionResult
                    .ImplicitPsWithoutSbr;
            }

            if (!ReferenceEquals(
                    implicitPsExtension.SbrSyncExtension,
                    implicitSbrExtension))
            {
                return PlaybackIsoBmffAacExtensionSignallingFactsResolutionResult
                    .ImplicitPsSourceMismatch;
            }
        }

        var sbrPresent = implicitSbrExtension?.SbrPresent == true;
        var psPresent = implicitPsExtension?.PsPresent == true;
        facts = new(
            coreFormatFacts,
            implicitSbrExtension is null
                ? PlaybackIsoBmffAacExtensionSignallingKind.None
                : PlaybackIsoBmffAacExtensionSignallingKind.Implicit,
            implicitSbrExtension,
            implicitPsExtension,
            sbrPresent,
            psPresent,
            sbrPresent
                ? implicitSbrExtension!.ExtensionSamplingFrequency!.Value
                : coreFormatFacts.DeclaredSamplingFrequency);
        return PlaybackIsoBmffAacExtensionSignallingFactsResolutionResult.Resolved;
    }
}
