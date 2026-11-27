namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextSettingsMenuFeedbackTests
{
    [Fact]
    public void ResolverUsesSpecificSettingsMenuWarning()
    {
        var presentation = GenesisGuiNextSettingsMenuFeedback.Resolve();

        Assert.Equal("Settings menu could not be opened. Try again.", presentation.Message);
        Assert.Equal("Settings", presentation.Caption);
    }

    [Fact]
    public void OpenFailurePresentsExactlyOneWarning()
    {
        var presented = new List<GenesisGuiNextSettingsMenuFeedbackPresentation>();

        GenesisGuiNextSettingsMenuFeedback.ReportOpenFailure(presented.Add);

        var warning = Assert.Single(presented);
        Assert.Equal("Settings menu could not be opened. Try again.", warning.Message);
    }

    [Fact]
    public void FeedbackFailureCannotFaultGuiLifetime()
    {
        var exception = Record.Exception(() =>
            GenesisGuiNextSettingsMenuFeedback.ReportOpenFailure(
                static _ => throw new InvalidOperationException("Presentation failed.")));

        Assert.Null(exception);
    }
}
