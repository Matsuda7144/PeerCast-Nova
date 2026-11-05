namespace PeerCast.Nova.App.Tests;

public sealed class GenesisYellowPageSettingsPageTests
{
    private static GenesisYellowPageSettingsDraft Draft(string id = "test") => new(id, "検証YP", "https://example.invalid/index.txt", "pcp://tracker.invalid:7144/", false, "test-space");

    [Fact]
    public void OperationsPersistThroughExistingModelAndKeepStableIdentity()
    {
        var config = new GenesisYellowPageConfiguration([]); var calls = 0;
        config.BindPersistence(_ => { calls++; return true; });
        var page = new GenesisYellowPageSettingsPage(new(config)); page.Open(); page.Edit(Draft());
        Assert.True(page.Save()); Assert.Equal("test", page.SelectedId);
        Assert.Equal("test-space", Assert.Single(page.Entries).Namespace);
        page.Edit(page.Draft with { Name = "変更済み" }); Assert.True(page.Save());
        Assert.Equal("test", Assert.Single(page.Entries).Id);
        Assert.True(page.Toggle()); Assert.True(Assert.Single(page.Entries).IsEnabled);
        Assert.True(page.Remove(page.DeletionTarget!)); Assert.Empty(page.Entries); Assert.Null(page.SelectedId);
        Assert.Equal(4, calls);
    }

    [Theory]
    [InlineData("file:///C:/index.txt", "")]
    [InlineData("https://example.invalid/index.txt", "https://tracker.invalid/")]
    public void InvalidUrlsDoNotPersist(string index, string tracker)
    {
        var page = new GenesisYellowPageSettingsPage(new(new GenesisYellowPageConfiguration([])));
        page.Open(); page.Edit(Draft() with { IndexUrl = index, FallbackTrackerUrl = tracker });
        Assert.False(page.Save()); Assert.True(page.IsError); Assert.Empty(page.Entries);
    }

    [Fact]
    public void DuplicateAndChangedIdAreRejectedAndLongUrlIsPreserved()
    {
        var page = new GenesisYellowPageSettingsPage(new(new GenesisYellowPageConfiguration([]))); page.Open();
        var url = "https://example.invalid/" + new string('a', 5000);
        page.Edit(Draft() with { IndexUrl = url }); Assert.True(page.Save());
        Assert.Equal(url, page.Draft.IndexUrl);
        page.Edit(page.Draft with { Id = "changed" }); Assert.False(page.Save());
        Assert.Equal("test", Assert.Single(page.Entries).Id);
        page.Select(null); page.Edit(Draft()); Assert.False(page.Save()); Assert.Single(page.Entries);
    }

    [Fact]
    public void PersistenceFailureRollsBackAndCancelOnlyDiscardsDraft()
    {
        var config = new GenesisYellowPageConfiguration([]); var fail = false;
        config.BindPersistence(_ => !fail);
        var page = new GenesisYellowPageSettingsPage(new(config)); page.Open(); page.Edit(Draft()); Assert.True(page.Save());
        var original = Assert.Single(page.Entries);
        fail = true;
        page.Edit(page.Draft with { Name = "未保存" }); Assert.False(page.Save()); Assert.Equal(original, Assert.Single(page.Entries));
        Assert.False(page.Toggle()); Assert.False(page.Remove(page.DeletionTarget!)); Assert.Equal(original, Assert.Single(page.Entries));
        page.Cancel(); Assert.Equal(original.Name, page.Draft.Name);
        page.Close(); Assert.False(page.Save()); Assert.Equal(original, Assert.Single(page.Entries));
    }

    [Fact]
    public void LegacyEditOrDeleteBlocksStaleSaveToggleAndConfirmedDeletion()
    {
        var model = new GenesisYellowPageSettingsModel(new([]));
        Assert.True(model.Add(Draft()).Succeeded);
        var page = new GenesisYellowPageSettingsPage(model); page.Open(); page.Select("test");
        var confirmed = page.DeletionTarget!;
        page.Edit(page.Draft with { Name = "古い入力" });
        Assert.True(model.Edit("test", Draft() with { Name = "従来画面で変更" }).Succeeded);
        Assert.False(page.Save()); Assert.False(page.Toggle()); Assert.False(page.Remove(confirmed));
        Assert.True(page.Conflict); Assert.Equal("従来画面で変更", Assert.Single(page.Entries).Name);
        page.Cancel(); Assert.False(page.Conflict); Assert.Equal("従来画面で変更", page.Draft.Name);
        Assert.True(model.Remove("test").Succeeded);
        Assert.False(page.Save()); Assert.Null(page.DeletionTarget);
        page.Cancel(); Assert.Null(page.SelectedId); Assert.Empty(page.Entries);
    }
}
