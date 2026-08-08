namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffErAacManagedDecoderPreparationResult
{
    Prepared,
    MissingAdmission,
    MissingFormatIntent,
    SourceMismatch,
    DecoderPreparationRejected,
}

public sealed class PlaybackIsoBmffErAacManagedDecoderPreparation :
    IDisposable,
    IAsyncDisposable
{
    internal PlaybackIsoBmffErAacManagedDecoderPreparation(
        PlaybackIsoBmffErConfigurationAdmission admission,
        PlaybackIsoBmffAacManagedDecoderPreparation decoderPreparation)
    {
        Admission = admission;
        DecoderPreparation = decoderPreparation;
    }

    public PlaybackIsoBmffErConfigurationAdmission Admission { get; }

    public PlaybackIsoBmffAacManagedDecoderPreparation DecoderPreparation
    {
        get;
    }

    public PlaybackIsoBmffAacDecoderFormatIntent FormatIntent =>
        DecoderPreparation.FormatIntent;

    public void Dispose() => DecoderPreparation.Dispose();

    public ValueTask DisposeAsync() => DecoderPreparation.DisposeAsync();
}

public static class PlaybackIsoBmffErAacManagedDecoderPreparationComposer
{
    public static PlaybackIsoBmffErAacManagedDecoderPreparationResult Compose(
        PlaybackIsoBmffErConfigurationAdmission? admission,
        PlaybackIsoBmffAacDecoderFormatIntent? formatIntent,
        IReadOnlyCollection<string>? declaredSymbols,
        PlaybackIsoBmffAacDecoderCapabilityDeclaration capabilityDeclaration,
        out PlaybackIsoBmffErAacManagedDecoderPreparation? preparation,
        PlaybackIsoBmffAacDecoderExtradataMaterializerOptions?
            extradataOptions = null)
    {
        preparation = null;
        if (admission is null)
        {
            return PlaybackIsoBmffErAacManagedDecoderPreparationResult
                .MissingAdmission;
        }

        if (formatIntent is null)
        {
            return PlaybackIsoBmffErAacManagedDecoderPreparationResult
                .MissingFormatIntent;
        }

        if (!ReferenceEquals(
                admission.Requirement.GaSpecificConfig,
                formatIntent.ExtensionSignallingFacts.CoreFormatFacts
                    .GaSpecificConfig))
        {
            return PlaybackIsoBmffErAacManagedDecoderPreparationResult
                .SourceMismatch;
        }

        if (PlaybackIsoBmffAacManagedDecoderPreparationComposer.Compose(
                formatIntent,
                declaredSymbols,
                capabilityDeclaration,
                out var decoderPreparation,
                extradataOptions) !=
            PlaybackIsoBmffAacManagedDecoderPreparationResult.Prepared ||
            decoderPreparation is null)
        {
            decoderPreparation?.Dispose();
            return PlaybackIsoBmffErAacManagedDecoderPreparationResult
                .DecoderPreparationRejected;
        }

        preparation = new(admission, decoderPreparation);
        return PlaybackIsoBmffErAacManagedDecoderPreparationResult.Prepared;
    }
}
