namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextFavoriteButtonPresentationTests
{
    [Fact]
    public void NoSelectedRowDisablesAddFavorite()
    {
        var tabs = CreateTabs();

        var presentation = GenesisGuiNextFavoriteButtonPresentationResolver.Resolve(
            tabs,
            row: null);

        Assert.Equal("Add Favorite", presentation.Label);
        Assert.False(presentation.IsEnabled);
    }

    [Fact]
    public void LiveNonFavoriteEnablesAddFavorite()
    {
        var tabs = CreateTabs();
        var channel = CreateChannel("Live");
        var row = new GenesisGuiNextNativeTabRow(channel, channel);

        var presentation = GenesisGuiNextFavoriteButtonPresentationResolver.Resolve(
            tabs,
            row);

        Assert.Equal("Add Favorite", presentation.Label);
        Assert.True(presentation.IsEnabled);
    }

    [Fact]
    public void ExistingFavoriteEnablesRemoveFavorite()
    {
        var channel = CreateChannel("Favorite");
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection([
                GenesisFavoriteChannel.FromChannel(channel),
            ]));
        var row = new GenesisGuiNextNativeTabRow(channel, channel);

        var presentation = GenesisGuiNextFavoriteButtonPresentationResolver.Resolve(
            tabs,
            row);

        Assert.Equal("Remove Favorite", presentation.Label);
        Assert.True(presentation.IsEnabled);
    }

    [Fact]
    public void OfflineFavoriteStillEnablesRemoveFavorite()
    {
        var channel = CreateChannel("Offline");
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection([
                GenesisFavoriteChannel.FromChannel(channel),
            ]));
        tabs.ApplyChannelViews(
            GenesisChannelViewProjectionSnapshot.FromCatalog(
                GenesisChannelCatalogSnapshot.Empty));
        tabs.SelectedTab = GenesisGuiNextTab.Favorites;
        var row = Assert.Single(
            GenesisGuiNextNativeTabProjection.Create(
                tabs,
                GenesisGuiNextTab.Favorites));

        Assert.Null(row.InteractiveChannel);
        var presentation = GenesisGuiNextFavoriteButtonPresentationResolver.Resolve(
            tabs,
            row);

        Assert.Equal("Remove Favorite", presentation.Label);
        Assert.True(presentation.IsEnabled);
    }

    [Fact]
    public void DisplayOnlyNonFavoriteAndPlayingRowsCannotAddFavorite()
    {
        var tabs = CreateTabs();
        var channel = CreateChannel("Display");
        var displayOnlyRow = new GenesisGuiNextNativeTabRow(
            channel,
            InteractiveChannel: null);

        var displayOnly = GenesisGuiNextFavoriteButtonPresentationResolver.Resolve(
            tabs,
            displayOnlyRow);

        Assert.Equal("Add Favorite", displayOnly.Label);
        Assert.False(displayOnly.IsEnabled);

        tabs.SelectedTab = GenesisGuiNextTab.Playing;
        var playing = GenesisGuiNextFavoriteButtonPresentationResolver.Resolve(
            tabs,
            new GenesisGuiNextNativeTabRow(channel, channel));

        Assert.Equal("Add Favorite", playing.Label);
        Assert.False(playing.IsEnabled);
    }

    private static GenesisGuiNextTabsViewModel CreateTabs() =>
        new(new GenesisFavoriteChannelCollection());

    private static GenesisChannelItem CreateChannel(string name) =>
        new(
            name,
            $"pcp://example/{name.ToLowerInvariant()}",
            ChannelId: "0123456789ABCDEF0123456789ABCDEF");
}
