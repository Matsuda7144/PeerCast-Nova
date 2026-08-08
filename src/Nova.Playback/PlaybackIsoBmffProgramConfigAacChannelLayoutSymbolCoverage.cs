namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffProgramConfigAacChannelLayoutSymbolCoverageResult
{
    Covered,
    MissingRequirement,
    PreparationUnavailable,
    MissingDeclaredSymbols,
    TooManyDeclaredSymbols,
    InvalidDeclaredSymbol,
    MissingRequiredSymbol,
}

public sealed class PlaybackIsoBmffProgramConfigAacChannelLayoutSymbolCoverage
{
    internal PlaybackIsoBmffProgramConfigAacChannelLayoutSymbolCoverage(
        PlaybackIsoBmffProgramConfigAacChannelLayoutRequirement requirement)
    {
        Requirement = requirement;
        CoveredSymbol =
            PlaybackIsoBmffProgramConfigAacChannelLayoutRequirement
                .InitializationSymbol;
    }

    public PlaybackIsoBmffProgramConfigAacChannelLayoutRequirement Requirement
    {
        get;
    }

    public string CoveredSymbol { get; }
}

public static class
    PlaybackIsoBmffProgramConfigAacChannelLayoutSymbolCoverageVerifier
{
    public static
        PlaybackIsoBmffProgramConfigAacChannelLayoutSymbolCoverageResult Verify(
            PlaybackIsoBmffProgramConfigAacChannelLayoutRequirement? requirement,
            IReadOnlyCollection<string>? declaredSymbols,
            out PlaybackIsoBmffProgramConfigAacChannelLayoutSymbolCoverage?
                coverage)
    {
        coverage = null;
        if (requirement is null)
        {
            return PlaybackIsoBmffProgramConfigAacChannelLayoutSymbolCoverageResult
                .MissingRequirement;
        }

        var lease = requirement.Preparation.DecoderPreparation.CapabilityBinding
            .Plan.ManagedParameters.Lease;
        if (lease.IsDisposed)
        {
            return PlaybackIsoBmffProgramConfigAacChannelLayoutSymbolCoverageResult
                .PreparationUnavailable;
        }

        if (declaredSymbols is null || declaredSymbols.Count == 0)
        {
            return PlaybackIsoBmffProgramConfigAacChannelLayoutSymbolCoverageResult
                .MissingDeclaredSymbols;
        }

        if (declaredSymbols.Count >
            PlaybackIsoBmffAacParameterTransferSymbolCoverageVerifier
                .MaximumDeclaredSymbolCount)
        {
            return PlaybackIsoBmffProgramConfigAacChannelLayoutSymbolCoverageResult
                .TooManyDeclaredSymbols;
        }

        var available = new HashSet<string>(StringComparer.Ordinal);
        foreach (var symbol in declaredSymbols)
        {
            if (!IsValidSymbol(symbol))
            {
                return PlaybackIsoBmffProgramConfigAacChannelLayoutSymbolCoverageResult
                    .InvalidDeclaredSymbol;
            }

            available.Add(symbol);
        }

        if (!available.Contains(
                PlaybackIsoBmffProgramConfigAacChannelLayoutRequirement
                    .InitializationSymbol))
        {
            return PlaybackIsoBmffProgramConfigAacChannelLayoutSymbolCoverageResult
                .MissingRequiredSymbol;
        }

        coverage = new(requirement);
        return PlaybackIsoBmffProgramConfigAacChannelLayoutSymbolCoverageResult
            .Covered;
    }

    private static bool IsValidSymbol(string? symbol) =>
        !string.IsNullOrWhiteSpace(symbol) &&
        symbol.Length <=
            PlaybackIsoBmffAacParameterTransferSymbolCoverageVerifier
                .MaximumSymbolLength &&
        symbol.All(static character =>
            char.IsAsciiLetterOrDigit(character) || character == '_');
}
