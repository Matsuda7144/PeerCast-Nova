namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelContextMenuFavoritePresentationTests
{
    [Fact]
    public void LiveNonFavoriteOffersAddFavorite()
    {
        var tabs = CreateTabs();
        var channel = Channel("Live");
        var row = new GenesisGuiNextNativeTabRow(channel, channel);

        Assert.Equal(
            GenesisChannelContextMenuFavoritePresentation.AddFavoriteText,
            GenesisChannelContextMenuFavoritePresentation.Resolve(tabs, row));
    }

    [Fact]
    public void FavoriteOffersRemoveEvenWhenOffline()
    {
        var channel = Channel("Saved");
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection([
                GenesisFavoriteChannel.FromChannel(channel),
            ]));
        tabs.SelectedTab = GenesisGuiNextTab.Favorites;
        var row = new GenesisGuiNextNativeTabRow(
            channel,
            InteractiveChannel: null);

        Assert.Equal(
            GenesisChannelContextMenuFavoritePresentation.RemoveFavoriteText,
            GenesisChannelContextMenuFavoritePresentation.Resolve(tabs, row));
    }

    [Fact]
    public void OfflineNonFavoriteOffersNoFavoriteMutation()
    {
        var tabs = CreateTabs();
        var row = new GenesisGuiNextNativeTabRow(
            Channel("Offline"),
            InteractiveChannel: null);

        Assert.Null(
            GenesisChannelContextMenuFavoritePresentation.Resolve(tabs, row));
    }

    [Fact]
    public void PlayingTabOffersNoFavoriteMutation()
    {
        var tabs = CreateTabs();
        tabs.SelectedTab = GenesisGuiNextTab.Playing;
        var channel = Channel("Playing");
        var row = new GenesisGuiNextNativeTabRow(channel, channel);

        Assert.Null(
            GenesisChannelContextMenuFavoritePresentation.Resolve(tabs, row));
    }

    private static GenesisGuiNextTabsViewModel CreateTabs() =>
        new(new GenesisFavoriteChannelCollection());

    private static GenesisChannelItem Channel(string name) =>
        new(
            name,
            $"pcp://example/{name.ToLowerInvariant()}",
            ChannelId: "0123456789ABCDEF0123456789ABCDEF");
}
