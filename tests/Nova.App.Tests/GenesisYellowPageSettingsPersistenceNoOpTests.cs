namespace PeerCast.Nova.App.Tests;

public sealed class GenesisYellowPageSettingsPersistenceNoOpTests
{
    [Fact]
    public void IdenticalEditDoesNotRewritePersistence()
    {
        var original = GenesisYellowPageDefaults.Sp;
        var configuration = new GenesisYellowPageConfiguration([original]);
        var saveCount = 0;
        configuration.BindPersistence(_ =>
        {
            saveCount++;
            return true;
        });
        var model = new GenesisYellowPageSettingsModel(configuration);

        var result = model.Edit(
            original.Id,
            new GenesisYellowPageSettingsDraft(
                original.Id,
                original.Name,
                original.IndexUri.AbsoluteUri,
                original.FallbackTrackerUri?.AbsoluteUri,
                original.IsEnabled,
                original.Namespace));

        Assert.True(result.Succeeded);
        Assert.Equal(GenesisYellowPageSettingsMutationOutcome.Applied, result.Outcome);
        Assert.Equal(0, saveCount);
        Assert.Equal(original, Assert.Single(configuration.Definitions));
    }

    [Fact]
    public void UnchangedEnabledStateDoesNotRewritePersistence()
    {
        var original = GenesisYellowPageDefaults.Sp;
        var configuration = new GenesisYellowPageConfiguration([original]);
        var saveCount = 0;
        configuration.BindPersistence(_ =>
        {
            saveCount++;
            return true;
        });
        var model = new GenesisYellowPageSettingsModel(configuration);

        var result = model.SetEnabled($" {original.Id} ", original.IsEnabled);

        Assert.True(result.Succeeded);
        Assert.Equal(GenesisYellowPageSettingsMutationOutcome.Applied, result.Outcome);
        Assert.Equal(0, saveCount);
        Assert.Equal(original, Assert.Single(configuration.Definitions));
    }

    [Fact]
    public void ChangedEnabledStateStillPersistsExactlyOnce()
    {
        var original = GenesisYellowPageDefaults.Sp;
        var configuration = new GenesisYellowPageConfiguration([original]);
        var saveCount = 0;
        IReadOnlyList<GenesisYellowPageDefinition>? persisted = null;
        configuration.BindPersistence(definitions =>
        {
            saveCount++;
            persisted = Array.AsReadOnly(definitions.ToArray());
            return true;
        });
        var model = new GenesisYellowPageSettingsModel(configuration);

        var result = model.SetEnabled(original.Id, !original.IsEnabled);

        Assert.True(result.Succeeded);
        Assert.Equal(1, saveCount);
        var current = Assert.Single(configuration.Definitions);
        Assert.Equal(!original.IsEnabled, current.IsEnabled);
        Assert.NotNull(persisted);
        Assert.Equal(current, Assert.Single(persisted!));
    }
}
