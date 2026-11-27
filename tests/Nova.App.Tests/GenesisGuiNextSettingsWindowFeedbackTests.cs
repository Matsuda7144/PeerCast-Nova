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
}
