namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffAacManagedDecoderPreparationResult
{
    Prepared,
    ExtradataRejected,
    CodecParametersInputRejected,
    ManagedParametersRejected,
    TransferPlanRejected,
    SymbolCoverageRejected,
    DeclaredCapabilityRejected,
}

public sealed class PlaybackIsoBmffAacManagedDecoderPreparation :
    IDisposable,
    IAsyncDisposable
{
    internal PlaybackIsoBmffAacManagedDecoderPreparation(
        PlaybackIsoBmffAacDeclaredDecoderCapabilityBinding capabilityBinding)
    {
        CapabilityBinding = capabilityBinding;
    }

    public PlaybackIsoBmffAacDeclaredDecoderCapabilityBinding CapabilityBinding
    {
        get;
    }

    public PlaybackIsoBmffAacDecoderFormatIntent FormatIntent =>
        CapabilityBinding.Plan.ManagedParameters.InputResolution.DecoderExtradata
            .FormatIntent;

    public void Dispose() => CapabilityBinding.Dispose();

    public ValueTask DisposeAsync() => CapabilityBinding.DisposeAsync();
}

public static class PlaybackIsoBmffAacManagedDecoderPreparationComposer
{
    public static PlaybackIsoBmffAacManagedDecoderPreparationResult Compose(
        PlaybackIsoBmffAacDecoderFormatIntent? formatIntent,
        IReadOnlyCollection<string>? declaredSymbols,
        PlaybackIsoBmffAacDecoderCapabilityDeclaration capabilityDeclaration,
        out PlaybackIsoBmffAacManagedDecoderPreparation? preparation,
        PlaybackIsoBmffAacDecoderExtradataMaterializerOptions? extradataOptions = null)
    {
        preparation = null;
        if (PlaybackIsoBmffAacDecoderExtradataMaterializer.Materialize(
                formatIntent,
                out var extradata,
                extradataOptions) !=
            PlaybackIsoBmffAacDecoderExtradataMaterializationResult.Materialized ||
            extradata is null)
        {
            return PlaybackIsoBmffAacManagedDecoderPreparationResult
                .ExtradataRejected;
        }

        if (PlaybackIsoBmffAacFfmpegCodecParametersInputResolver.Resolve(
                extradata, out var inputResolution) !=
            PlaybackIsoBmffAacFfmpegCodecParametersInputResolutionResult.Resolved ||
            inputResolution is null)
        {
            return PlaybackIsoBmffAacManagedDecoderPreparationResult
                .CodecParametersInputRejected;
        }

        if (PlaybackIsoBmffAacManagedCodecParametersFactory.Create(
                inputResolution, out var managedParameters) !=
            PlaybackIsoBmffAacManagedCodecParametersCreationResult.Created ||
            managedParameters is null)
        {
            managedParameters?.Dispose();
            return PlaybackIsoBmffAacManagedDecoderPreparationResult
                .ManagedParametersRejected;
        }

        if (PlaybackIsoBmffAacParameterTransferPlanner.Plan(
                managedParameters, out var transferPlan) !=
            PlaybackIsoBmffAacParameterTransferPlanningResult.Planned ||
            transferPlan is null)
        {
            transferPlan?.Dispose();
            managedParameters.Dispose();
            return PlaybackIsoBmffAacManagedDecoderPreparationResult
                .TransferPlanRejected;
        }

        if (PlaybackIsoBmffAacParameterTransferSymbolCoverageVerifier.Verify(
                transferPlan, declaredSymbols, out var symbolCoverage) !=
            PlaybackIsoBmffAacParameterTransferSymbolCoverageResult.Covered ||
            symbolCoverage is null)
        {
            symbolCoverage?.Dispose();
            transferPlan.Dispose();
            return PlaybackIsoBmffAacManagedDecoderPreparationResult
                .SymbolCoverageRejected;
        }

        if (PlaybackIsoBmffAacDeclaredDecoderCapabilityBinder.Bind(
                symbolCoverage,
                capabilityDeclaration,
                out var capabilityBinding) !=
            PlaybackIsoBmffAacDeclaredDecoderCapabilityBindingResult.Bound ||
            capabilityBinding is null)
        {
            capabilityBinding?.Dispose();
            symbolCoverage.Dispose();
            return PlaybackIsoBmffAacManagedDecoderPreparationResult
                .DeclaredCapabilityRejected;
        }

        preparation = new(capabilityBinding);
        return PlaybackIsoBmffAacManagedDecoderPreparationResult.Prepared;
    }
}
