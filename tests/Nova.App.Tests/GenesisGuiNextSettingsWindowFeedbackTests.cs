namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextSettingsWindowFeedbackTests
{
    [Fact]
    public void ResolverMapsSettingsWindowKindsToSpecificWarnings()
    {
        var yellowPages = GenesisGuiNextSettingsWindowFeedback.Resolve(
            GenesisGuiNextSettingsWindowKind.YellowPages);
        var networkRelay = GenesisGuiNextSettingsWindowFeedback.Resolve(
            GenesisGuiNextSettingsWindowKind.NetworkRelay);
        var playback = GenesisGuiNextSettingsWindowFeedback.Resolve(
            GenesisGuiNextSettingsWindowKind.Playback);

        Assert.Equal("Yellow Page settings could not be opened.", yellowPages.Message);
        Assert.Equal("Yellow Pages", yellowPages.Caption);
        Assert.Equal(
            "Network / Relay settings could not be opened.",
            networkRelay.Message);
        Assert.Equal("Network / Relay", networkRelay.Caption);
        Assert.Equal("Playback settings could not be opened.", playback.Message);
        Assert.Equal("Playback", playback.Caption);
    }

    [Fact]
    public void SuccessfulOpenDoesNotPresentWarning()
    {
        var presented = new List<GenesisGuiNextSettingsWindowFeedbackPresentation>();

        GenesisGuiNextSettingsWindowFeedback.ReportIfFailed(
            opened: true,
            GenesisGuiNextSettingsWindowKind.YellowPages,
            presented.Add);

        Assert.Empty(presented);
    }

    [Fact]
    public void FailedOpenPresentsExactlyOneWarning()
    {
        var presented = new List<GenesisGuiNextSettingsWindowFeedbackPresentation>();

        GenesisGuiNextSettingsWindowFeedback.ReportIfFailed(
            opened: false,
            GenesisGuiNextSettingsWindowKind.NetworkRelay,
            presented.Add);

        var warning = Assert.Single(presented);
        Assert.Equal("Network / Relay settings could not be opened.", warning.Message);
        Assert.Equal("Network / Relay", warning.Caption);
    }

    [Fact]
    public void PresentationFailureDoesNotEscapeFailedOpenReporting()
    {
        var exception = Record.Exception(() =>
            GenesisGuiNextSettingsWindowFeedback.ReportIfFailed(
                opened: false,
                GenesisGuiNextSettingsWindowKind.YellowPages,
                static _ => throw new InvalidOperationException("Presentation failed.")));

        Assert.Null(exception);
    }

    [Fact]
    public void NativeHostsReportWindowCreationFailureThroughSharedFeedback()
    {
        var yellowPageHost = File.ReadAllText(FindRepositoryFile(
            "src",
            "Nova.App",
            "GenesisYellowPageSettingsWindowHost.cs"));
        var networkHost = File.ReadAllText(FindRepositoryFile(
            "src",
            "Nova.App",
            "GenesisNetworkSettingsWindowHost.cs"));
        var playbackHost = File.ReadAllText(FindRepositoryFile(
            "src",
            "Nova.App",
            "GenesisGuiNextPlaybackSettingsWindowHost.cs"));

        Assert.Contains(
            "SystemGenesisGuiNextSettingsWindowFeedback.ReportIfFailed(",
            yellowPageHost,
            StringComparison.Ordinal);
        Assert.Contains(
            "_window is not null,",
            yellowPageHost,
            StringComparison.Ordinal);
        Assert.Contains(
            "GenesisGuiNextSettingsWindowKind.YellowPages",
            yellowPageHost,
            StringComparison.Ordinal);

        Assert.Contains(
            "SystemGenesisGuiNextSettingsWindowFeedback.ReportIfFailed(",
            networkHost,
            StringComparison.Ordinal);
        Assert.Contains(
            "_window is not null,",
            networkHost,
            StringComparison.Ordinal);
        Assert.Contains(
            "GenesisGuiNextSettingsWindowKind.NetworkRelay",
            networkHost,
            StringComparison.Ordinal);

        Assert.Contains(
            "SystemGenesisGuiNextSettingsWindowFeedback.ReportIfFailed(",
            playbackHost,
            StringComparison.Ordinal);
        Assert.Contains(
            "GenesisGuiNextSettingsWindowKind.Playback",
            playbackHost,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(path))
            {
                return path;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Repository file was not found: {Path.Combine(relativeParts)}");
    }
}
