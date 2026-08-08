namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffAdmittedErAacManagedDecoderPreparationResult
{
    Prepared,
    MissingComposition,
    DecoderPreparationRejected,
}

public sealed class PlaybackIsoBmffAdmittedErAacManagedDecoderPreparation :
    IDisposable,
    IAsyncDisposable
{
    internal PlaybackIsoBmffAdmittedErAacManagedDecoderPreparation(
        PlaybackIsoBmffAdmittedErAacDecoderFormatIntent composition,
        PlaybackIsoBmffErAacManagedDecoderPreparation decoderPreparation)
    {
        Composition = composition;
        DecoderPreparation = decoderPreparation;
    }

    public PlaybackIsoBmffAdmittedErAacDecoderFormatIntent Composition
    {
        get;
    }

    public PlaybackIsoBmffErConfigurationAdmission Admission =>
        Composition.Admission;

    public PlaybackIsoBmffAacDecoderFormatIntent FormatIntent =>
        Composition.FormatIntent;

    public PlaybackIsoBmffErAacManagedDecoderPreparation DecoderPreparation
    {
        get;
    }

    public void Dispose() => DecoderPreparation.Dispose();

    public ValueTask DisposeAsync() => DecoderPreparation.DisposeAsync();
}

public static class
    PlaybackIsoBmffAdmittedErAacManagedDecoderPreparationComposer
{
    public static
        PlaybackIsoBmffAdmittedErAacManagedDecoderPreparationResult Compose(
            PlaybackIsoBmffAdmittedErAacDecoderFormatIntent? composition,
            IReadOnlyCollection<string>? declaredSymbols,
            PlaybackIsoBmffAacDecoderCapabilityDeclaration capabilityDeclaration,
            out PlaybackIsoBmffAdmittedErAacManagedDecoderPreparation?
                preparation,
            PlaybackIsoBmffAacDecoderExtradataMaterializerOptions?
                extradataOptions = null)
    {
        preparation = null;
        if (composition is null)
        {
            return PlaybackIsoBmffAdmittedErAacManagedDecoderPreparationResult
                .MissingComposition;
        }

        if (PlaybackIsoBmffErAacManagedDecoderPreparationComposer.Compose(
                composition.Admission,
                composition.FormatIntent,
                declaredSymbols,
                capabilityDeclaration,
                out var decoderPreparation,
                extradataOptions) !=
            PlaybackIsoBmffErAacManagedDecoderPreparationResult.Prepared ||
            decoderPreparation is null)
        {
            decoderPreparation?.Dispose();
            return PlaybackIsoBmffAdmittedErAacManagedDecoderPreparationResult
                .DecoderPreparationRejected;
        }

        preparation = new(composition, decoderPreparation);
        return PlaybackIsoBmffAdmittedErAacManagedDecoderPreparationResult
            .Prepared;
    }
}
