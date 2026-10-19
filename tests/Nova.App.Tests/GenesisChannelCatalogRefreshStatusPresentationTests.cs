namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelCatalogRefreshStatusPresentationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 21, 5, 8, 0, TimeSpan.Zero);

    [Fact]
    public void EmptyDiagnosticsReportNotRefreshed()
    {
        Assert.Equal(
            "YP: not refreshed",
            GenesisChannelCatalogRefreshStatusPresentation.Format(
                GenesisChannelCatalogRefreshDiagnostics.Empty,
                isRefreshing: false,
                Now));
    }

    [Fact]
    public void ActiveRefreshRetainsLastSuccessfulAge()
    {
        var diagnostics = new GenesisChannelCatalogRefreshDiagnostics(
            LastAttemptedAt: Now,
            LastSuccessfulAt: Now - TimeSpan.FromMinutes(2),
            LastResult: GenesisChannelCatalogRefreshResult.Completed);

        Assert.Equal(
            "YP: updating · last updated 2m ago",
            GenesisChannelCatalogRefreshStatusPresentation.Format(
                diagnostics,
                isRefreshing: true,
                Now));
    }

    [Fact]
    public void SuccessfulRefreshReportsCompactAge()
    {
        var diagnostics = new GenesisChannelCatalogRefreshDiagnostics(
            LastAttemptedAt: Now - TimeSpan.FromSeconds(13),
            LastSuccessfulAt: Now - TimeSpan.FromSeconds(12),
            LastResult: GenesisChannelCatalogRefreshResult.Completed);

        Assert.Equal(
            "YP: updated 12s ago",
            GenesisChannelCatalogRefreshStatusPresentation.Format(
                diagnostics,
                isRefreshing: false,
                Now));
    }

    [Fact]
    public void FailedRefreshReportsAttemptAndRetainsLastSuccessfulAge()
    {
        var diagnostics = new GenesisChannelCatalogRefreshDiagnostics(
            LastAttemptedAt: Now - TimeSpan.FromSeconds(5),
            LastSuccessfulAt: Now - TimeSpan.FromHours(3),
            LastResult: GenesisChannelCatalogRefreshResult.Faulted);

        Assert.Equal(
            "YP: refresh failed 5s ago · last updated 3h ago",
            GenesisChannelCatalogRefreshStatusPresentation.Format(
                diagnostics,
                isRefreshing: false,
                Now));
    }

    [Fact]
    public void FailedRefreshWithoutPriorSuccessReportsAttemptAge()
    {
        var diagnostics = new GenesisChannelCatalogRefreshDiagnostics(
            LastAttemptedAt: Now - TimeSpan.FromSeconds(5),
            LastSuccessfulAt: null,
            LastResult: GenesisChannelCatalogRefreshResult.Faulted);

        Assert.Equal(
            "YP: refresh failed 5s ago",
            GenesisChannelCatalogRefreshStatusPresentation.Format(
                diagnostics,
                isRefreshing: false,
                Now));
    }

    [Fact]
    public void CancelledRefreshReportsAttemptAndRetainsLastSuccessfulAge()
    {
        var diagnostics = new GenesisChannelCatalogRefreshDiagnostics(
            LastAttemptedAt: Now - TimeSpan.FromMinutes(2),
            LastSuccessfulAt: Now - TimeSpan.FromHours(1),
            LastResult: GenesisChannelCatalogRefreshResult.Cancelled);

        Assert.Equal(
            "YP: refresh cancelled 2m ago · last updated 1h ago",
            GenesisChannelCatalogRefreshStatusPresentation.Format(
                diagnostics,
                isRefreshing: false,
                Now));
    }

    [Fact]
    public void CancelledRefreshWithoutAttemptTimestampDoesNotInventAge()
    {
        var diagnostics = new GenesisChannelCatalogRefreshDiagnostics(
            LastAttemptedAt: null,
            LastSuccessfulAt: null,
            LastResult: GenesisChannelCatalogRefreshResult.Cancelled);

        Assert.Equal(
            "YP: refresh cancelled",
            GenesisChannelCatalogRefreshStatusPresentation.Format(
                diagnostics,
                isRefreshing: false,
                Now));
    }

    [Theory]
    [InlineData(-5, "0s ago")]
    [InlineData(59, "59s ago")]
    [InlineData(60, "1m ago")]
    [InlineData(3_600, "1h ago")]
    [InlineData(86_400, "1d ago")]
    public void AgeFormattingUsesBoundedCompactUnits(
        int ageSeconds,
        string expected)
    {
        var observedAt = Now - TimeSpan.FromSeconds(ageSeconds);

        Assert.Equal(
            expected,
            GenesisChannelCatalogRefreshStatusPresentation.FormatAge(
                observedAt,
                Now));
    }
}
