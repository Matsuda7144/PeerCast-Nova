namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffProgramConfigAacExplicitLayoutPreparationResult
{
    Prepared,
    DecoderPreparationRejected,
    LayoutRequirementRejected,
    SymbolCoverageRejected,
}

public sealed class PlaybackIsoBmffProgramConfigAacExplicitLayoutPreparation :
    IDisposable,
    IAsyncDisposable
{
    private static readonly IReadOnlyList<string> RequiredSymbolsValue =
        Array.AsReadOnly(
        [
            .. PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols,
            PlaybackIsoBmffProgramConfigAacChannelLayoutRequirement
                .InitializationSymbol,
        ]);

    internal PlaybackIsoBmffProgramConfigAacExplicitLayoutPreparation(
        PlaybackIsoBmffProgramConfigAacManagedDecoderPreparation
            decoderPreparation,
        PlaybackIsoBmffProgramConfigAacChannelLayoutSymbolCoverage
            layoutSymbolCoverage)
    {
        DecoderPreparation = decoderPreparation;
        LayoutSymbolCoverage = layoutSymbolCoverage;
    }

    public PlaybackIsoBmffProgramConfigAacManagedDecoderPreparation
        DecoderPreparation
    {
        get;
    }

    public PlaybackIsoBmffProgramConfigAacChannelLayoutRequirement
        LayoutRequirement => LayoutSymbolCoverage.Requirement;

    public PlaybackIsoBmffProgramConfigAacChannelLayoutSymbolCoverage
        LayoutSymbolCoverage
    {
        get;
    }

    public static IReadOnlyList<string> RequiredSymbols => RequiredSymbolsValue;

    public void Dispose() => DecoderPreparation.Dispose();

    public ValueTask DisposeAsync() => DecoderPreparation.DisposeAsync();
}

public static class PlaybackIsoBmffProgramConfigAacExplicitLayoutPreparationComposer
{
    public static PlaybackIsoBmffProgramConfigAacExplicitLayoutPreparationResult
        Compose(
            PlaybackIsoBmffProgramConfigChannelReorderPlan? reorderPlan,
            PlaybackIsoBmffAacDecoderFormatIntent? formatIntent,
            IReadOnlyCollection<string>? declaredSymbols,
            PlaybackIsoBmffAacDecoderCapabilityDeclaration capabilityDeclaration,
            out PlaybackIsoBmffProgramConfigAacExplicitLayoutPreparation?
                preparation,
            PlaybackIsoBmffAacDecoderExtradataMaterializerOptions?
                extradataOptions = null)
    {
        preparation = null;
        if (PlaybackIsoBmffProgramConfigAacManagedDecoderPreparationComposer
                .Compose(
                    reorderPlan,
                    formatIntent,
                    declaredSymbols,
                    capabilityDeclaration,
                    out var decoderPreparation,
                    extradataOptions) !=
            PlaybackIsoBmffProgramConfigAacManagedDecoderPreparationResult
                .Prepared ||
            decoderPreparation is null)
        {
            decoderPreparation?.Dispose();
            return PlaybackIsoBmffProgramConfigAacExplicitLayoutPreparationResult
                .DecoderPreparationRejected;
        }

        if (PlaybackIsoBmffProgramConfigAacChannelLayoutRequirementProjector
                .Project(decoderPreparation, out var layoutRequirement) !=
            PlaybackIsoBmffProgramConfigAacChannelLayoutRequirementResult
                .Projected ||
            layoutRequirement is null)
        {
            decoderPreparation.Dispose();
            return PlaybackIsoBmffProgramConfigAacExplicitLayoutPreparationResult
                .LayoutRequirementRejected;
        }

        if (PlaybackIsoBmffProgramConfigAacChannelLayoutSymbolCoverageVerifier
                .Verify(
                    layoutRequirement,
                    declaredSymbols,
                    out var layoutSymbolCoverage) !=
            PlaybackIsoBmffProgramConfigAacChannelLayoutSymbolCoverageResult
                .Covered ||
            layoutSymbolCoverage is null)
        {
            decoderPreparation.Dispose();
            return PlaybackIsoBmffProgramConfigAacExplicitLayoutPreparationResult
                .SymbolCoverageRejected;
        }

        preparation = new(decoderPreparation, layoutSymbolCoverage);
        return PlaybackIsoBmffProgramConfigAacExplicitLayoutPreparationResult
            .Prepared;
    }
}
