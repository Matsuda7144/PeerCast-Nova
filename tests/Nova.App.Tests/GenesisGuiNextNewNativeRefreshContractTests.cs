namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextNewNativeRefreshContractTests
{
    [Fact]
    public void NewChannelMutationPublishesVisibleNewProjectionChange()
    {
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection())
        {
            SelectedTab = GenesisGuiNextTab.New,
        };
        var existing = new GenesisChannelItem(
            "Existing",
            "pcp://example/existing",
            ChannelId: "existing");
        var added = new GenesisChannelItem(
            "Added",
            "pcp://example/added",
            ChannelId: "added");
        var changed = new List<string?>();
        tabs.PropertyChanged += (_, eventArgs) => changed.Add(eventArgs.PropertyName);

        tabs.ApplyChannelViews(
            new GenesisChannelViewProjectionSnapshot(
                [existing, added],
                [added]));

        Assert.Contains(
            nameof(GenesisGuiNextTabsViewModel.VisibleNewChannels),
            changed);
        var row = Assert.Single(GenesisGuiNextNativeTabProjection.Create(tabs));
        Assert.Same(added, row.DisplayChannel);
        Assert.Same(added, row.InteractiveChannel);
        Assert.True(row.IsInteractive);
    }
}
