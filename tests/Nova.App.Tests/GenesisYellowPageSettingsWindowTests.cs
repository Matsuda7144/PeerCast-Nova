namespace PeerCast.Nova.App.Tests;

public sealed class GenesisYellowPageSettingsWindowTests
{
    [Theory]
    [InlineData(true, "[x] Example (example)")]
    [InlineData(false, "[ ] Example (example)")]
    public void EntryLabelShowsEnabledState(bool isEnabled, string expected)
    {
        var definition = new GenesisYellowPageDefinition(
            "example",
            "Example",
            new Uri("https://example.test/index.txt"),
            new Uri("pcp://example.test:7144/"),
            isEnabled);

        var label = GenesisYellowPageSettingsWindow.BuildEntryLabel(definition);

        Assert.Equal(expected, label);
    }

    [Fact]
    public async Task MissingOwnerRejectsNativeWindowWithoutMutatingSettings()
    {
        var configuration = GenesisYellowPageConfiguration.CreateDefault();
        var model = new GenesisYellowPageSettingsModel(configuration);

        var window = GenesisYellowPageSettingsWindow.TryCreate(0, model);

        Assert.Null(window);
        Assert.Single(configuration.Definitions);
        await Task.CompletedTask;
    }
}
