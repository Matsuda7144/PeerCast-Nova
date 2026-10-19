namespace PeerCast.Nova.App.Tests;

public sealed class GenesisYellowPageSettingsModelTests
{
    [Fact]
    public void EntriesPreserveConfiguredOrderAndMetadata()
    {
        var first = Definition(
            "one",
            "One",
            "https://one.example/index.txt",
            "pcp://one.example:7144/",
            isEnabled: true,
            yellowPageNamespace: "one-space");
        var second = Definition(
            "two",
            "Two",
            "https://two.example/index.txt",
            fallbackTrackerUrl: null,
            isEnabled: false,
            yellowPageNamespace: null);
        var model = new GenesisYellowPageSettingsModel(
            new GenesisYellowPageConfiguration([first, second]));

        Assert.Equal([first, second], model.Entries);
    }

    [Fact]
    public void AddValidYellowPageMutatesSharedConfiguration()
    {
        var configuration = GenesisYellowPageConfiguration.CreateDefault();
        var model = new GenesisYellowPageSettingsModel(configuration);

        var result = model.Add(Draft(
            "community",
            "Community",
            "https://yp.example/index.txt",
            "pcp://tracker.example:7144/",
            isEnabled: false,
            yellowPageNamespace: "community"));

        Assert.True(result.Succeeded);
        Assert.Equal(GenesisYellowPageSettingsMutationOutcome.Applied, result.Outcome);
        Assert.Null(result.ErrorMessage);
        var added = Assert.Single(
            configuration.Definitions,
            definition => definition.Id == "community");
        Assert.Equal("Community", added.Name);
        Assert.Equal(new Uri("https://yp.example/index.txt"), added.IndexUri);
        Assert.Equal(new Uri("pcp://tracker.example:7144/"), added.FallbackTrackerUri);
        Assert.False(added.IsEnabled);
        Assert.Equal("community", added.Namespace);
    }

    [Fact]
    public void AddDuplicateIdIsRejectedWithoutReplacingExistingEntry()
    {
        var original = Definition(
            "same",
            "Original",
            "https://original.example/index.txt");
        var configuration = new GenesisYellowPageConfiguration([original]);
        var model = new GenesisYellowPageSettingsModel(configuration);

        var result = model.Add(Draft(
            "same",
            "Replacement",
            "https://replacement.example/index.txt"));

        Assert.False(result.Succeeded);
        Assert.Equal(GenesisYellowPageSettingsMutationOutcome.DuplicateId, result.Outcome);
        Assert.Single(configuration.Definitions);
        Assert.Same(original, configuration.Definitions[0]);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://yp.example/index.txt")]
    public void AddRejectsInvalidIndexUrl(string indexUrl)
    {
        var configuration = new GenesisYellowPageConfiguration([]);
        var model = new GenesisYellowPageSettingsModel(configuration);

        var result = model.Add(Draft("yp", "YP", indexUrl));

        Assert.Equal(GenesisYellowPageSettingsMutationOutcome.Invalid, result.Outcome);
        Assert.Empty(configuration.Definitions);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("https://tracker.example/")]
    public void AddRejectsInvalidFallbackTrackerUrl(string trackerUrl)
    {
        var configuration = new GenesisYellowPageConfiguration([]);
        var model = new GenesisYellowPageSettingsModel(configuration);

        var result = model.Add(Draft(
            "yp",
            "YP",
            "https://yp.example/index.txt",
            trackerUrl));

        Assert.Equal(GenesisYellowPageSettingsMutationOutcome.Invalid, result.Outcome);
        Assert.Empty(configuration.Definitions);
    }

    [Fact]
    public void EditReplacesMetadataForStableId()
    {
        var original = Definition(
            "stable",
            "Before",
            "https://before.example/index.txt");
        var configuration = new GenesisYellowPageConfiguration([original]);
        var model = new GenesisYellowPageSettingsModel(configuration);

        var result = model.Edit(
            " stable ",
            Draft(
                "stable",
                "After",
                "https://after.example/index.txt",
                "pcp://after.example:7144/",
                isEnabled: false,
                yellowPageNamespace: "after"));

        Assert.True(result.Succeeded);
        var edited = Assert.Single(configuration.Definitions);
        Assert.Equal("stable", edited.Id);
        Assert.Equal("After", edited.Name);
        Assert.Equal(new Uri("https://after.example/index.txt"), edited.IndexUri);
        Assert.Equal(new Uri("pcp://after.example:7144/"), edited.FallbackTrackerUri);
        Assert.False(edited.IsEnabled);
        Assert.Equal("after", edited.Namespace);
    }

    [Fact]
    public void EditCannotChangeStableId()
    {
        var original = Definition(
            "stable",
            "Before",
            "https://before.example/index.txt");
        var configuration = new GenesisYellowPageConfiguration([original]);
        var model = new GenesisYellowPageSettingsModel(configuration);

        var result = model.Edit(
            "stable",
            Draft("different", "After", "https://after.example/index.txt"));

        Assert.Equal(GenesisYellowPageSettingsMutationOutcome.Invalid, result.Outcome);
        Assert.Single(configuration.Definitions);
        Assert.Same(original, configuration.Definitions[0]);
    }

    [Fact]
    public void RemoveAndSetEnabledMutateSharedConfiguration()
    {
        var first = Definition("one", "One", "https://one.example/index.txt");
        var second = Definition("two", "Two", "https://two.example/index.txt");
        var configuration = new GenesisYellowPageConfiguration([first, second]);
        var model = new GenesisYellowPageSettingsModel(configuration);

        var disabled = model.SetEnabled(" one ", isEnabled: false);
        var removed = model.Remove("two");

        Assert.True(disabled.Succeeded);
        Assert.True(removed.Succeeded);
        var remaining = Assert.Single(configuration.Definitions);
        Assert.Equal("one", remaining.Id);
        Assert.False(remaining.IsEnabled);
    }

    [Fact]
    public void MissingEditRemoveAndEnableAreBoundedNotFoundResults()
    {
        var model = new GenesisYellowPageSettingsModel(
            new GenesisYellowPageConfiguration([]));

        var edit = model.Edit(
            "missing",
            Draft("missing", "Missing", "https://missing.example/index.txt"));
        var remove = model.Remove("missing");
        var enable = model.SetEnabled("missing", isEnabled: true);

        Assert.Equal(GenesisYellowPageSettingsMutationOutcome.NotFound, edit.Outcome);
        Assert.Equal(GenesisYellowPageSettingsMutationOutcome.NotFound, remove.Outcome);
        Assert.Equal(GenesisYellowPageSettingsMutationOutcome.NotFound, enable.Outcome);
    }

    [Fact]
    public void BlankOptionalValuesNormalizeToNull()
    {
        var configuration = new GenesisYellowPageConfiguration([]);
        var model = new GenesisYellowPageSettingsModel(configuration);

        var result = model.Add(Draft(
            "yp",
            "YP",
            "https://yp.example/index.txt",
            "  ",
            yellowPageNamespace: "  "));

        Assert.True(result.Succeeded);
        var added = Assert.Single(configuration.Definitions);
        Assert.Null(added.FallbackTrackerUri);
        Assert.Null(added.Namespace);
    }

    [Fact]
    public void SuccessfulMutationPersistsCurrentSnapshot()
    {
        var original = Definition("one", "One", "https://one.example/index.txt");
        var configuration = new GenesisYellowPageConfiguration([original]);
        IReadOnlyList<GenesisYellowPageDefinition>? persisted = null;
        configuration.BindPersistence(definitions =>
        {
            persisted = Array.AsReadOnly(definitions.ToArray());
            return true;
        });
        var model = new GenesisYellowPageSettingsModel(configuration);

        var result = model.Add(Draft(
            "two",
            "Two",
            "https://two.example/index.txt"));

        Assert.True(result.Succeeded);
        Assert.NotNull(persisted);
        Assert.Collection(
            persisted!,
            entry => Assert.Equal("one", entry.Id),
            entry => Assert.Equal("two", entry.Id));
    }

    [Fact]
    public void PersistenceFailureRollsBackMutationAndOrder()
    {
        var first = Definition("one", "One", "https://one.example/index.txt");
        var second = Definition("two", "Two", "https://two.example/index.txt");
        var configuration = new GenesisYellowPageConfiguration([first, second]);
        configuration.BindPersistence(static _ => false);
        var model = new GenesisYellowPageSettingsModel(configuration);

        var result = model.Remove("one");

        Assert.False(result.Succeeded);
        Assert.Equal(
            GenesisYellowPageSettingsMutationOutcome.PersistenceFailed,
            result.Outcome);
        Assert.Equal([first, second], configuration.Definitions);
    }

    [Fact]
    public void PersistenceExceptionRollsBackEditedMetadata()
    {
        var original = Definition(
            "stable",
            "Before",
            "https://before.example/index.txt");
        var configuration = new GenesisYellowPageConfiguration([original]);
        configuration.BindPersistence(static _ =>
            throw new IOException("save failed"));
        var model = new GenesisYellowPageSettingsModel(configuration);

        var result = model.Edit(
            "stable",
            Draft("stable", "After", "https://after.example/index.txt"));

        Assert.Equal(
            GenesisYellowPageSettingsMutationOutcome.PersistenceFailed,
            result.Outcome);
        Assert.Equal(original, Assert.Single(configuration.Definitions));
    }

    private static GenesisYellowPageSettingsDraft Draft(
        string? id,
        string? name,
        string? indexUrl,
        string? fallbackTrackerUrl = null,
        bool isEnabled = true,
        string? yellowPageNamespace = null) =>
        new(
            id,
            name,
            indexUrl,
            fallbackTrackerUrl,
            isEnabled,
            yellowPageNamespace);

    private static GenesisYellowPageDefinition Definition(
        string id,
        string name,
        string indexUrl,
        string? fallbackTrackerUrl = null,
        bool isEnabled = true,
        string? yellowPageNamespace = null) =>
        new(
            id,
            name,
            new Uri(indexUrl),
            fallbackTrackerUrl is null ? null : new Uri(fallbackTrackerUrl),
            isEnabled,
            yellowPageNamespace);
}
