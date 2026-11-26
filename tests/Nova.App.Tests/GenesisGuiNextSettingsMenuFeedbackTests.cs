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

    [Fact]
    public void NativeMenuConstructionFailureIsDistinctFromCancellation()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "Nova.App",
            "GenesisChannelNotificationSettingsButtonBridge.cs"));

        Assert.Contains(
            "private GenesisChannelNotificationSettingsCommand? ShowMenu(nint anchorWindow)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (command is null)\n            {\n                SystemGenesisGuiNextSettingsMenuFeedback.ReportOpenFailure(_owner);\n                return false;\n            }",
            source,
            StringComparison.Ordinal);
        Assert.True(
            source.Split("return null;", StringSplitOptions.None).Length - 1 >= 6,
            "All bounded native menu-construction rejection paths should report failure.");
        Assert.Contains(
            "return (GenesisChannelNotificationSettingsCommand)selected;",
            source,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(params string[] relativeParts) =>
        RepositorySourceTestHelper.FindRepositoryFile(relativeParts);
}
