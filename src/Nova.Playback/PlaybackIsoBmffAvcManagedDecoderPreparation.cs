namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffAvcManagedDecoderPreparationResult
{
    Prepared,
    InputResolutionRejected,
    ManagedParametersRejected,
    TransferPlanRejected,
    SymbolCoverageRejected,
    DeclaredCapabilityRejected,
}

public readonly record struct PlaybackIsoBmffAvcDecoderCapabilityDeclaration(
    PlaybackDecoderFfmpegCodecId CodecId,
    int MaximumWidth,
    int MaximumHeight,
    long MaximumPixelCount);

public sealed class PlaybackIsoBmffAvcFfmpegCodecParametersInputResolution
{
    internal PlaybackIsoBmffAvcFfmpegCodecParametersInputResolution(
        PlaybackIsoBmffRegularAvcDecoderPreparationInput source,
        PlaybackDecoderFfmpegCodecParametersInput input)
    {
        Source = source;
        Input = input;
    }

    public PlaybackIsoBmffRegularAvcDecoderPreparationInput Source { get; }

    public PlaybackDecoderFfmpegCodecParametersInput Input { get; }
}

public enum PlaybackIsoBmffAvcFfmpegCodecParametersInputResolutionResult
{
    Resolved,
    MissingPreparationInput,
    SourceMismatch,
    DimensionOutOfRange,
}

public static class PlaybackIsoBmffAvcFfmpegCodecParametersInputResolver
{
    public static PlaybackIsoBmffAvcFfmpegCodecParametersInputResolutionResult
        Resolve(
            PlaybackIsoBmffRegularAvcDecoderPreparationInput? source,
            out PlaybackIsoBmffAvcFfmpegCodecParametersInputResolution?
                resolution)
    {
        resolution = null;
        if (source is null)
        {
            return PlaybackIsoBmffAvcFfmpegCodecParametersInputResolutionResult
                .MissingPreparationInput;
        }

        var visualEntry = source.AvcConfiguration.VisualEntry;
        if (!ReferenceEquals(
                visualEntry,
                source.CodecConfiguration.Source.VisualEntry) ||
            source.ConfigurationRecord.IsEmpty)
        {
            return PlaybackIsoBmffAvcFfmpegCodecParametersInputResolutionResult
                .SourceMismatch;
        }

        if (visualEntry.Width is < 1 or
                > PlaybackDecoderFfmpegCodecParametersFactory
                    .MaximumVideoDimension ||
            visualEntry.Height is < 1 or
                > PlaybackDecoderFfmpegCodecParametersFactory
                    .MaximumVideoDimension ||
            (long)visualEntry.Width * visualEntry.Height >
                PlaybackDecoderFfmpegCodecParametersFactory
                    .MaximumVideoPixelCount)
        {
            return PlaybackIsoBmffAvcFfmpegCodecParametersInputResolutionResult
                .DimensionOutOfRange;
        }

        resolution = new(
            source,
            PlaybackDecoderFfmpegCodecParametersInput.ForH264(
                visualEntry.Width,
                visualEntry.Height,
                source.ConfigurationRecord));
        return PlaybackIsoBmffAvcFfmpegCodecParametersInputResolutionResult
            .Resolved;
    }
}

public sealed class PlaybackIsoBmffAvcManagedCodecParameters :
    IDisposable,
    IAsyncDisposable
{
    internal PlaybackIsoBmffAvcManagedCodecParameters(
        PlaybackIsoBmffAvcFfmpegCodecParametersInputResolution inputResolution,
        PlaybackDecoderFfmpegCodecParametersLease lease)
    {
        InputResolution = inputResolution;
        Lease = lease;
    }

    public PlaybackIsoBmffAvcFfmpegCodecParametersInputResolution
        InputResolution
    { get; }

    public PlaybackDecoderFfmpegCodecParametersLease Lease { get; }

    public void Dispose() => Lease.Dispose();

    public ValueTask DisposeAsync() => Lease.DisposeAsync();
}

public enum PlaybackIsoBmffAvcManagedCodecParametersCreationResult
{
    Created,
    MissingInputResolution,
    FactoryRejected,
}

public static class PlaybackIsoBmffAvcManagedCodecParametersFactory
{
    public static PlaybackIsoBmffAvcManagedCodecParametersCreationResult Create(
        PlaybackIsoBmffAvcFfmpegCodecParametersInputResolution? inputResolution,
        out PlaybackIsoBmffAvcManagedCodecParameters? parameters)
    {
        parameters = null;
        if (inputResolution is null)
        {
            return PlaybackIsoBmffAvcManagedCodecParametersCreationResult
                .MissingInputResolution;
        }

        var creation = PlaybackDecoderFfmpegCodecParametersFactory.Create(
            inputResolution.Input);
        if (creation.Result !=
                PlaybackDecoderFfmpegCodecParametersResult.Created ||
            creation.Lease is null)
        {
            creation.Lease?.Dispose();
            return PlaybackIsoBmffAvcManagedCodecParametersCreationResult
                .FactoryRejected;
        }

        parameters = new(inputResolution, creation.Lease);
        return PlaybackIsoBmffAvcManagedCodecParametersCreationResult.Created;
    }
}

public sealed class PlaybackIsoBmffAvcParameterTransferPlan :
    IDisposable,
    IAsyncDisposable
{
    private static readonly IReadOnlyList<string> RequiredSymbolsValue =
        Array.AsReadOnly(
        [
            "avcodec_parameters_alloc",
            "avcodec_parameters_free",
            "av_mallocz",
            "avcodec_parameters_to_context",
        ]);

    internal PlaybackIsoBmffAvcParameterTransferPlan(
        PlaybackIsoBmffAvcManagedCodecParameters managedParameters)
    {
        ManagedParameters = managedParameters;
        CodecId = managedParameters.Lease.CodecId;
        Width = managedParameters.Lease.Width;
        Height = managedParameters.Lease.Height;
        ExtradataLength = managedParameters.Lease.ExtradataLength;
    }

    public PlaybackIsoBmffAvcManagedCodecParameters ManagedParameters { get; }

    public PlaybackDecoderFfmpegCodecId CodecId { get; }

    public int Width { get; }

    public int Height { get; }

    public int ExtradataLength { get; }

    public static IReadOnlyList<string> RequiredSymbols => RequiredSymbolsValue;

    public void Dispose() => ManagedParameters.Dispose();

    public ValueTask DisposeAsync() => ManagedParameters.DisposeAsync();
}

public enum PlaybackIsoBmffAvcParameterTransferPlanningResult
{
    Planned,
    MissingManagedParameters,
    ParametersUnavailable,
    InconsistentParameters,
}

public static class PlaybackIsoBmffAvcParameterTransferPlanner
{
    public static PlaybackIsoBmffAvcParameterTransferPlanningResult Plan(
        PlaybackIsoBmffAvcManagedCodecParameters? managedParameters,
        out PlaybackIsoBmffAvcParameterTransferPlan? plan)
    {
        plan = null;
        if (managedParameters is null)
        {
            return PlaybackIsoBmffAvcParameterTransferPlanningResult
                .MissingManagedParameters;
        }

        var lease = managedParameters.Lease;
        if (lease.IsDisposed)
        {
            return PlaybackIsoBmffAvcParameterTransferPlanningResult
                .ParametersUnavailable;
        }

        var input = managedParameters.InputResolution.Input;
        if (lease.CodecId != PlaybackDecoderFfmpegCodecId.H264 ||
            input.CodecId != lease.CodecId ||
            input.Width != lease.Width ||
            input.Height != lease.Height ||
            input.Extradata.Length != lease.ExtradataLength)
        {
            return PlaybackIsoBmffAvcParameterTransferPlanningResult
                .InconsistentParameters;
        }

        plan = new(managedParameters);
        return PlaybackIsoBmffAvcParameterTransferPlanningResult.Planned;
    }
}

public sealed class PlaybackIsoBmffAvcParameterTransferSymbolCoverage :
    IDisposable,
    IAsyncDisposable
{
    internal PlaybackIsoBmffAvcParameterTransferSymbolCoverage(
        PlaybackIsoBmffAvcParameterTransferPlan plan)
    {
        Plan = plan;
        CoveredSymbols = PlaybackIsoBmffAvcParameterTransferPlan.RequiredSymbols;
    }

    public PlaybackIsoBmffAvcParameterTransferPlan Plan { get; }

    public IReadOnlyList<string> CoveredSymbols { get; }

    public void Dispose() => Plan.Dispose();

    public ValueTask DisposeAsync() => Plan.DisposeAsync();
}

public enum PlaybackIsoBmffAvcParameterTransferSymbolCoverageResult
{
    Covered,
    MissingPlan,
    PlanUnavailable,
    MissingDeclaredSymbols,
    TooManyDeclaredSymbols,
    InvalidDeclaredSymbol,
    MissingRequiredSymbol,
}

public static class PlaybackIsoBmffAvcParameterTransferSymbolCoverageVerifier
{
    public const int MaximumDeclaredSymbolCount = 256;

    public const int MaximumSymbolLength = 128;

    public static PlaybackIsoBmffAvcParameterTransferSymbolCoverageResult Verify(
        PlaybackIsoBmffAvcParameterTransferPlan? plan,
        IReadOnlyCollection<string>? declaredSymbols,
        out PlaybackIsoBmffAvcParameterTransferSymbolCoverage? coverage)
    {
        coverage = null;
        if (plan is null)
        {
            return PlaybackIsoBmffAvcParameterTransferSymbolCoverageResult
                .MissingPlan;
        }

        if (plan.ManagedParameters.Lease.IsDisposed)
        {
            return PlaybackIsoBmffAvcParameterTransferSymbolCoverageResult
                .PlanUnavailable;
        }

        if (declaredSymbols is null || declaredSymbols.Count == 0)
        {
            return PlaybackIsoBmffAvcParameterTransferSymbolCoverageResult
                .MissingDeclaredSymbols;
        }

        if (declaredSymbols.Count > MaximumDeclaredSymbolCount)
        {
            return PlaybackIsoBmffAvcParameterTransferSymbolCoverageResult
                .TooManyDeclaredSymbols;
        }

        var available = new HashSet<string>(StringComparer.Ordinal);
        foreach (var symbol in declaredSymbols)
        {
            if (!IsValidSymbol(symbol))
            {
                return PlaybackIsoBmffAvcParameterTransferSymbolCoverageResult
                    .InvalidDeclaredSymbol;
            }

            available.Add(symbol);
        }

        if (PlaybackIsoBmffAvcParameterTransferPlan.RequiredSymbols.Any(
                symbol => !available.Contains(symbol)))
        {
            return PlaybackIsoBmffAvcParameterTransferSymbolCoverageResult
                .MissingRequiredSymbol;
        }

        coverage = new(plan);
        return PlaybackIsoBmffAvcParameterTransferSymbolCoverageResult.Covered;
    }

    private static bool IsValidSymbol(string? symbol) =>
        !string.IsNullOrWhiteSpace(symbol) &&
        symbol.Length <= MaximumSymbolLength &&
        symbol.All(static character =>
            char.IsAsciiLetterOrDigit(character) || character == '_');
}

public sealed class PlaybackIsoBmffAvcDeclaredDecoderCapabilityBinding :
    IDisposable,
    IAsyncDisposable
{
    internal PlaybackIsoBmffAvcDeclaredDecoderCapabilityBinding(
        PlaybackIsoBmffAvcParameterTransferSymbolCoverage symbolCoverage,
        PlaybackIsoBmffAvcDecoderCapabilityDeclaration declaration)
    {
        SymbolCoverage = symbolCoverage;
        Declaration = declaration;
    }

    public PlaybackIsoBmffAvcParameterTransferSymbolCoverage SymbolCoverage
    { get; }

    public PlaybackIsoBmffAvcDecoderCapabilityDeclaration Declaration { get; }

    public PlaybackIsoBmffAvcParameterTransferPlan Plan => SymbolCoverage.Plan;

    public void Dispose() => SymbolCoverage.Dispose();

    public ValueTask DisposeAsync() => SymbolCoverage.DisposeAsync();
}

public enum PlaybackIsoBmffAvcDeclaredDecoderCapabilityBindingResult
{
    Bound,
    MissingSymbolCoverage,
    CoverageUnavailable,
    InvalidDeclaration,
    CodecMismatch,
    DimensionsUnsupported,
    PixelCountUnsupported,
}

public static class PlaybackIsoBmffAvcDeclaredDecoderCapabilityBinder
{
    public static PlaybackIsoBmffAvcDeclaredDecoderCapabilityBindingResult Bind(
        PlaybackIsoBmffAvcParameterTransferSymbolCoverage? symbolCoverage,
        PlaybackIsoBmffAvcDecoderCapabilityDeclaration declaration,
        out PlaybackIsoBmffAvcDeclaredDecoderCapabilityBinding? binding)
    {
        binding = null;
        if (symbolCoverage is null)
        {
            return PlaybackIsoBmffAvcDeclaredDecoderCapabilityBindingResult
                .MissingSymbolCoverage;
        }

        var plan = symbolCoverage.Plan;
        if (plan.ManagedParameters.Lease.IsDisposed)
        {
            return PlaybackIsoBmffAvcDeclaredDecoderCapabilityBindingResult
                .CoverageUnavailable;
        }

        if (!IsValidDeclaration(declaration))
        {
            return PlaybackIsoBmffAvcDeclaredDecoderCapabilityBindingResult
                .InvalidDeclaration;
        }

        if (declaration.CodecId != plan.CodecId)
        {
            return PlaybackIsoBmffAvcDeclaredDecoderCapabilityBindingResult
                .CodecMismatch;
        }

        if (plan.Width > declaration.MaximumWidth ||
            plan.Height > declaration.MaximumHeight)
        {
            return PlaybackIsoBmffAvcDeclaredDecoderCapabilityBindingResult
                .DimensionsUnsupported;
        }

        if ((long)plan.Width * plan.Height > declaration.MaximumPixelCount)
        {
            return PlaybackIsoBmffAvcDeclaredDecoderCapabilityBindingResult
                .PixelCountUnsupported;
        }

        binding = new(symbolCoverage, declaration);
        return PlaybackIsoBmffAvcDeclaredDecoderCapabilityBindingResult.Bound;
    }

    private static bool IsValidDeclaration(
        PlaybackIsoBmffAvcDecoderCapabilityDeclaration declaration) =>
        Enum.IsDefined(declaration.CodecId) &&
        declaration.MaximumWidth is >= 1 and <=
            PlaybackDecoderFfmpegCodecParametersFactory.MaximumVideoDimension &&
        declaration.MaximumHeight is >= 1 and <=
            PlaybackDecoderFfmpegCodecParametersFactory.MaximumVideoDimension &&
        declaration.MaximumPixelCount is >= 1 and <=
            PlaybackDecoderFfmpegCodecParametersFactory.MaximumVideoPixelCount;
}

public sealed class PlaybackIsoBmffAvcManagedDecoderPreparation :
    IDisposable,
    IAsyncDisposable
{
    internal PlaybackIsoBmffAvcManagedDecoderPreparation(
        PlaybackIsoBmffAvcDeclaredDecoderCapabilityBinding capabilityBinding)
    {
        CapabilityBinding = capabilityBinding;
    }

    public PlaybackIsoBmffAvcDeclaredDecoderCapabilityBinding CapabilityBinding
    { get; }

    public PlaybackIsoBmffRegularAvcDecoderPreparationInput Input =>
        CapabilityBinding.Plan.ManagedParameters.InputResolution.Source;

    public void Dispose() => CapabilityBinding.Dispose();

    public ValueTask DisposeAsync() => CapabilityBinding.DisposeAsync();
}

public static class PlaybackIsoBmffAvcManagedDecoderPreparationComposer
{
    public static PlaybackIsoBmffAvcManagedDecoderPreparationResult Compose(
        PlaybackIsoBmffRegularAvcDecoderPreparationInput? source,
        IReadOnlyCollection<string>? declaredSymbols,
        PlaybackIsoBmffAvcDecoderCapabilityDeclaration capabilityDeclaration,
        out PlaybackIsoBmffAvcManagedDecoderPreparation? preparation)
    {
        preparation = null;
        if (PlaybackIsoBmffAvcFfmpegCodecParametersInputResolver.Resolve(
                source,
                out var inputResolution) !=
            PlaybackIsoBmffAvcFfmpegCodecParametersInputResolutionResult
                .Resolved ||
            inputResolution is null)
        {
            return PlaybackIsoBmffAvcManagedDecoderPreparationResult
                .InputResolutionRejected;
        }

        if (PlaybackIsoBmffAvcManagedCodecParametersFactory.Create(
                inputResolution,
                out var managedParameters) !=
            PlaybackIsoBmffAvcManagedCodecParametersCreationResult.Created ||
            managedParameters is null)
        {
            managedParameters?.Dispose();
            return PlaybackIsoBmffAvcManagedDecoderPreparationResult
                .ManagedParametersRejected;
        }

        if (PlaybackIsoBmffAvcParameterTransferPlanner.Plan(
                managedParameters,
                out var transferPlan) !=
            PlaybackIsoBmffAvcParameterTransferPlanningResult.Planned ||
            transferPlan is null)
        {
            transferPlan?.Dispose();
            managedParameters.Dispose();
            return PlaybackIsoBmffAvcManagedDecoderPreparationResult
                .TransferPlanRejected;
        }

        if (PlaybackIsoBmffAvcParameterTransferSymbolCoverageVerifier.Verify(
                transferPlan,
                declaredSymbols,
                out var symbolCoverage) !=
            PlaybackIsoBmffAvcParameterTransferSymbolCoverageResult.Covered ||
            symbolCoverage is null)
        {
            symbolCoverage?.Dispose();
            transferPlan.Dispose();
            return PlaybackIsoBmffAvcManagedDecoderPreparationResult
                .SymbolCoverageRejected;
        }

        if (PlaybackIsoBmffAvcDeclaredDecoderCapabilityBinder.Bind(
                symbolCoverage,
                capabilityDeclaration,
                out var capabilityBinding) !=
            PlaybackIsoBmffAvcDeclaredDecoderCapabilityBindingResult.Bound ||
            capabilityBinding is null)
        {
            capabilityBinding?.Dispose();
            symbolCoverage.Dispose();
            return PlaybackIsoBmffAvcManagedDecoderPreparationResult
                .DeclaredCapabilityRejected;
        }

        preparation = new(capabilityBinding);
        return PlaybackIsoBmffAvcManagedDecoderPreparationResult.Prepared;
    }
}
