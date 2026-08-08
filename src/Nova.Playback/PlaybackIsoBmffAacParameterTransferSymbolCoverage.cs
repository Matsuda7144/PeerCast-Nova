namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffAacParameterTransferSymbolCoverageResult
{
    Covered,
    MissingPlan,
    PlanUnavailable,
    MissingDeclaredSymbols,
    TooManyDeclaredSymbols,
    InvalidDeclaredSymbol,
    MissingRequiredSymbol,
}

public sealed class PlaybackIsoBmffAacParameterTransferSymbolCoverage :
    IDisposable,
    IAsyncDisposable
{
    internal PlaybackIsoBmffAacParameterTransferSymbolCoverage(
        PlaybackIsoBmffAacParameterTransferPlan plan)
    {
        Plan = plan;
        CoveredSymbols = PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols;
    }

    public PlaybackIsoBmffAacParameterTransferPlan Plan { get; }

    public IReadOnlyList<string> CoveredSymbols { get; }

    public void Dispose() => Plan.Dispose();

    public ValueTask DisposeAsync() => Plan.DisposeAsync();
}

public static class PlaybackIsoBmffAacParameterTransferSymbolCoverageVerifier
{
    public const int MaximumDeclaredSymbolCount = 256;
    public const int MaximumSymbolLength = 128;

    public static PlaybackIsoBmffAacParameterTransferSymbolCoverageResult Verify(
        PlaybackIsoBmffAacParameterTransferPlan? plan,
        IReadOnlyCollection<string>? declaredSymbols,
        out PlaybackIsoBmffAacParameterTransferSymbolCoverage? coverage)
    {
        coverage = null;
        if (plan is null)
        {
            return PlaybackIsoBmffAacParameterTransferSymbolCoverageResult
                .MissingPlan;
        }

        if (plan.ManagedParameters.Lease.IsDisposed)
        {
            return PlaybackIsoBmffAacParameterTransferSymbolCoverageResult
                .PlanUnavailable;
        }

        if (declaredSymbols is null || declaredSymbols.Count == 0)
        {
            return PlaybackIsoBmffAacParameterTransferSymbolCoverageResult
                .MissingDeclaredSymbols;
        }

        if (declaredSymbols.Count > MaximumDeclaredSymbolCount)
        {
            return PlaybackIsoBmffAacParameterTransferSymbolCoverageResult
                .TooManyDeclaredSymbols;
        }

        var available = new HashSet<string>(StringComparer.Ordinal);
        foreach (var symbol in declaredSymbols)
        {
            if (!IsValidSymbol(symbol))
            {
                return PlaybackIsoBmffAacParameterTransferSymbolCoverageResult
                    .InvalidDeclaredSymbol;
            }

            available.Add(symbol);
        }

        if (PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols.Any(
                symbol => !available.Contains(symbol)))
        {
            return PlaybackIsoBmffAacParameterTransferSymbolCoverageResult
                .MissingRequiredSymbol;
        }

        coverage = new(plan);
        return PlaybackIsoBmffAacParameterTransferSymbolCoverageResult.Covered;
    }

    private static bool IsValidSymbol(string? symbol) =>
        !string.IsNullOrWhiteSpace(symbol) &&
        symbol.Length <= MaximumSymbolLength &&
        symbol.All(static character =>
            char.IsAsciiLetterOrDigit(character) || character == '_');
}
