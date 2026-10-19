namespace PeerCast.Nova.App.Tests;

public sealed class GenesisYellowPageDefinitionTests
{
    [Fact]
    public void SpDefaultPreservesExistingCatalogEndpoints()
    {
        var definition = GenesisYellowPageDefaults.Sp;

        Assert.Equal("sp", definition.Id);
        Assert.Equal("SP", definition.Name);
        Assert.Equal(SpGenesisChannelCatalog.IndexUri, definition.IndexUri);
        Assert.Equal(SpGenesisChannelCatalog.TrackerUri, definition.FallbackTrackerUri);
        Assert.True(definition.IsEnabled);
        Assert.Equal("sp", definition.Namespace);
    }

    [Fact]
    public void CustomDefinitionNormalizesEditableMetadata()
    {
        var definition = new GenesisYellowPageDefinition(
            "  custom-yp  ",
            "  Custom YP  ",
            new Uri("https://example.test/index.txt"),
            new Uri("pcp://example.test:7144/"),
            isEnabled: false,
            yellowPageNamespace: "  custom  ");

        Assert.Equal("custom-yp", definition.Id);
        Assert.Equal("Custom YP", definition.Name);
        Assert.Equal(new Uri("https://example.test/index.txt"), definition.IndexUri);
        Assert.Equal(new Uri("pcp://example.test:7144/"), definition.FallbackTrackerUri);
        Assert.False(definition.IsEnabled);
        Assert.Equal("custom", definition.Namespace);
    }

    [Fact]
    public void CustomDefinitionMayOmitFallbackTrackerAndNamespace()
    {
        var definition = new GenesisYellowPageDefinition(
            "custom",
            "Custom",
            new Uri("http://example.test/index.txt"));

        Assert.Null(definition.FallbackTrackerUri);
        Assert.Null(definition.Namespace);
        Assert.True(definition.IsEnabled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DefinitionRejectsBlankId(string id)
    {
        Assert.Throws<ArgumentException>(() => new GenesisYellowPageDefinition(
            id,
            "Custom",
            new Uri("https://example.test/index.txt")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DefinitionRejectsBlankName(string name)
    {
        Assert.Throws<ArgumentException>(() => new GenesisYellowPageDefinition(
            "custom",
            name,
            new Uri("https://example.test/index.txt")));
    }

    [Fact]
    public void DefinitionRejectsNonHttpIndexUri()
    {
        Assert.Throws<ArgumentException>(() => new GenesisYellowPageDefinition(
            "custom",
            "Custom",
            new Uri("ftp://example.test/index.txt")));
    }

    [Fact]
    public void DefinitionRejectsNonPcpFallbackTracker()
    {
        Assert.Throws<ArgumentException>(() => new GenesisYellowPageDefinition(
            "custom",
            "Custom",
            new Uri("https://example.test/index.txt"),
            new Uri("https://example.test/tracker")));
    }

    [Fact]
    public void EnabledStateCanChangeWithoutChangingStableIdentity()
    {
        var definition = GenesisYellowPageDefaults.Sp;

        var disabled = definition with { IsEnabled = false };

        Assert.Equal(definition.Id, disabled.Id);
        Assert.Equal(definition.Name, disabled.Name);
        Assert.Equal(definition.IndexUri, disabled.IndexUri);
        Assert.Equal(definition.FallbackTrackerUri, disabled.FallbackTrackerUri);
        Assert.False(disabled.IsEnabled);
    }
}
