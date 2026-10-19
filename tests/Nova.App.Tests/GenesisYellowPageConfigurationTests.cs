namespace PeerCast.Nova.App.Tests;

public sealed class GenesisYellowPageConfigurationTests
{
    [Fact]
    public void DefaultConfigurationContainsEnabledSpDefinition()
    {
        var configuration = GenesisYellowPageConfiguration.CreateDefault();

        var definition = Assert.Single(configuration.Definitions);
        Assert.Equal(GenesisYellowPageDefaults.Sp, definition);
        Assert.Single(configuration.EnabledDefinitions);
    }

    [Fact]
    public void AddRejectsDuplicateStableId()
    {
        var configuration = GenesisYellowPageConfiguration.CreateDefault();

        var added = configuration.Add(new GenesisYellowPageDefinition(
            "sp",
            "Replacement SP",
            new Uri("https://example.test/index.txt")));

        Assert.False(added);
        Assert.Single(configuration.Definitions);
        Assert.Equal(GenesisYellowPageDefaults.Sp, configuration.Definitions[0]);
    }

    [Fact]
    public void AddAndReplacePreserveDefinitionOrder()
    {
        var configuration = GenesisYellowPageConfiguration.CreateDefault();
        var custom = new GenesisYellowPageDefinition(
            "custom",
            "Custom",
            new Uri("https://example.test/index.txt"));
        Assert.True(configuration.Add(custom));

        var replacement = new GenesisYellowPageDefinition(
            "custom",
            "Edited Custom",
            new Uri("https://example.test/edited.txt"),
            new Uri("pcp://example.test:7144/"));

        Assert.True(configuration.Replace(replacement));
        Assert.Equal(
            [GenesisYellowPageDefaults.Sp, replacement],
            configuration.Definitions);
    }

    [Fact]
    public void RemoveDeletesOnlyMatchingDefinition()
    {
        var custom = new GenesisYellowPageDefinition(
            "custom",
            "Custom",
            new Uri("https://example.test/index.txt"));
        var configuration = new GenesisYellowPageConfiguration(
            [GenesisYellowPageDefaults.Sp, custom]);

        Assert.True(configuration.Remove(" custom "));
        Assert.False(configuration.Remove("custom"));
        Assert.Equal([GenesisYellowPageDefaults.Sp], configuration.Definitions);
    }

    [Fact]
    public void SetEnabledUpdatesEnabledSnapshotWithoutChangingIdentity()
    {
        var configuration = GenesisYellowPageConfiguration.CreateDefault();

        Assert.True(configuration.SetEnabled("sp", false));

        var disabled = Assert.Single(configuration.Definitions);
        Assert.Equal("sp", disabled.Id);
        Assert.False(disabled.IsEnabled);
        Assert.Empty(configuration.EnabledDefinitions);

        Assert.True(configuration.SetEnabled("sp", true));
        Assert.True(Assert.Single(configuration.EnabledDefinitions).IsEnabled);
    }

    [Fact]
    public void UnknownDefinitionCannotBeReplacedRemovedOrEnabled()
    {
        var configuration = GenesisYellowPageConfiguration.CreateDefault();
        var unknown = new GenesisYellowPageDefinition(
            "unknown",
            "Unknown",
            new Uri("https://example.test/index.txt"));

        Assert.False(configuration.Replace(unknown));
        Assert.False(configuration.Remove("unknown"));
        Assert.False(configuration.SetEnabled("unknown", false));
    }

    [Fact]
    public void ConstructorRejectsDuplicateIds()
    {
        var duplicate = new GenesisYellowPageDefinition(
            "sp",
            "Duplicate",
            new Uri("https://example.test/index.txt"));

        Assert.Throws<ArgumentException>(() => new GenesisYellowPageConfiguration(
            [GenesisYellowPageDefaults.Sp, duplicate]));
    }

    [Fact]
    public void ReturnedSnapshotsAreUnaffectedByLaterConfigurationChanges()
    {
        var configuration = GenesisYellowPageConfiguration.CreateDefault();
        var snapshot = configuration.Definitions;

        Assert.True(configuration.Add(new GenesisYellowPageDefinition(
            "custom",
            "Custom",
            new Uri("https://example.test/index.txt"))));

        Assert.Single(snapshot);
        Assert.Equal(2, configuration.Definitions.Count);
    }
}
