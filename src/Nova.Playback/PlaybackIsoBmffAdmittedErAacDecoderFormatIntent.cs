namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffAdmittedErAacDecoderFormatIntentCompositionResult
{
    Composed,
    MissingAdmission,
    CoreFormatFactsRejected,
    ImplicitSbrPrecedesAdmission,
    ExtensionSignallingFactsRejected,
    DecoderFormatIntentRejected,
}

public sealed class PlaybackIsoBmffAdmittedErAacDecoderFormatIntent
{
    internal PlaybackIsoBmffAdmittedErAacDecoderFormatIntent(
        PlaybackIsoBmffErConfigurationAdmission admission,
        PlaybackIsoBmffAacDecoderFormatIntent formatIntent)
    {
        Admission = admission;
        FormatIntent = formatIntent;
    }

    public PlaybackIsoBmffErConfigurationAdmission Admission { get; }

    public PlaybackIsoBmffAacDecoderFormatIntent FormatIntent { get; }
}

public static class PlaybackIsoBmffAdmittedErAacDecoderFormatIntentComposer
{
    public static
        PlaybackIsoBmffAdmittedErAacDecoderFormatIntentCompositionResult Compose(
            PlaybackIsoBmffErConfigurationAdmission? admission,
            PlaybackIsoBmffProgramConfigChannelTopology? programConfigTopology,
            PlaybackIsoBmffImplicitSbrSyncExtension? implicitSbrExtension,
            PlaybackIsoBmffImplicitPsSyncExtension? implicitPsExtension,
            out PlaybackIsoBmffAdmittedErAacDecoderFormatIntent? composition)
    {
        composition = null;
        if (admission is null)
        {
            return PlaybackIsoBmffAdmittedErAacDecoderFormatIntentCompositionResult
                .MissingAdmission;
        }

        if (implicitSbrExtension is not null &&
            implicitSbrExtension.ConsumedBitLength <= admission.ConsumedBitLength)
        {
            return PlaybackIsoBmffAdmittedErAacDecoderFormatIntentCompositionResult
                .ImplicitSbrPrecedesAdmission;
        }

        if (PlaybackIsoBmffAacCoreFormatFactsResolver.Resolve(
                admission.Requirement.GaSpecificConfig,
                programConfigTopology,
                out var coreFormatFacts) !=
            PlaybackIsoBmffAacCoreFormatFactsResolutionResult.Resolved ||
            coreFormatFacts is null)
        {
            return PlaybackIsoBmffAdmittedErAacDecoderFormatIntentCompositionResult
                .CoreFormatFactsRejected;
        }

        if (PlaybackIsoBmffAacExtensionSignallingFactsResolver.Resolve(
                coreFormatFacts,
                implicitSbrExtension,
                implicitPsExtension,
                out var extensionSignallingFacts) !=
            PlaybackIsoBmffAacExtensionSignallingFactsResolutionResult.Resolved ||
            extensionSignallingFacts is null)
        {
            return PlaybackIsoBmffAdmittedErAacDecoderFormatIntentCompositionResult
                .ExtensionSignallingFactsRejected;
        }

        if (PlaybackIsoBmffAacDecoderFormatIntentResolver.Resolve(
                extensionSignallingFacts,
                out var formatIntent) !=
            PlaybackIsoBmffAacDecoderFormatIntentResolutionResult.Resolved ||
            formatIntent is null)
        {
            return PlaybackIsoBmffAdmittedErAacDecoderFormatIntentCompositionResult
                .DecoderFormatIntentRejected;
        }

        composition = new(admission, formatIntent);
        return PlaybackIsoBmffAdmittedErAacDecoderFormatIntentCompositionResult
            .Composed;
    }
}
