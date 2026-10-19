namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextFavoriteToggleTests
{
    [Fact]
    public void LiveChannelToggleAddsThenRemovesFavorite()
    {
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());
        var channel = new GenesisChannelItem(
            "Live",
            "pcp://example/live",
            ChannelId: "0123456789ABCDEF0123456789ABCDEF");
        tabs.ApplyChannelViews(
            new GenesisChannelViewProjectionSnapshot([channel], [channel]));
        var row = new GenesisGuiNextNativeTabRow(channel, channel);

        Assert.Equal(
            GenesisGuiNextFavoriteToggleResult.Added,
            GenesisGuiNextFavoriteToggle.Apply(tabs, row));
        Assert.True(tabs.IsFavorite(channel));
        Assert.Single(tabs.FavoriteChannels);

        Assert.Equal(
            GenesisGuiNextFavoriteToggleResult.Removed,
            GenesisGuiNextFavoriteToggle.Apply(tabs, row));
        Assert.False(tabs.IsFavorite(channel));
        Assert.Empty(tabs.FavoriteChannels);
    }

    [Fact]
    public void AddPersistenceFailureIsReportedAndDoesNotPublishFavorite()
    {
        var favorites = new GenesisFavoriteChannelCollection();
        favorites.BindPersistence(_ => false);
        var tabs = new GenesisGuiNextTabsViewModel(favorites);
        var channel = new GenesisChannelItem(
            "Live",
            "pcp://example/live",
            ChannelId: "0123456789ABCDEF0123456789ABCDEF");
        tabs.ApplyChannelViews(
            new GenesisChannelViewProjectionSnapshot([channel], [channel]));
        var row = new GenesisGuiNextNativeTabRow(channel, channel);

        Assert.Equal(
            GenesisGuiNextFavoriteToggleResult.PersistenceFailed,
            GenesisGuiNextFavoriteToggle.Apply(tabs, row));
        Assert.False(tabs.IsFavorite(channel));
        Assert.Empty(tabs.FavoriteChannels);
    }

    [Fact]
    public void RemovePersistenceFailureIsReportedAndPreservesFavorite()
    {
        var savedChannel = new GenesisChannelItem(
            "Saved",
            "pcp://example/saved",
            Description: "last known metadata",
            ChannelId: "0123456789ABCDEF0123456789ABCDEF");
        var favorites = new GenesisFavoriteChannelCollection([
            GenesisFavoriteChannel.FromChannel(savedChannel),
        ]);
        favorites.BindPersistence(_ => false);
        var tabs = new GenesisGuiNextTabsViewModel(favorites);
        tabs.ApplyChannelViews(
            GenesisChannelViewProjectionSnapshot.FromCatalog(
                GenesisChannelCatalogSnapshot.Empty));
        tabs.SelectedTab = GenesisGuiNextTab.Favorites;
        var row = Assert.Single(
            GenesisGuiNextNativeTabProjection.Create(
                tabs,
                GenesisGuiNextTab.Favorites));

        Assert.Null(row.InteractiveChannel);
        Assert.Equal(
            GenesisGuiNextFavoriteToggleResult.PersistenceFailed,
            GenesisGuiNextFavoriteToggle.Apply(tabs, row));
        Assert.True(tabs.IsFavorite(savedChannel));
        Assert.Single(tabs.FavoriteChannels);
    }

    [Fact]
    public void OfflineFavoriteCanBeRemovedWithoutBecomingInteractive()
    {
        var savedChannel = new GenesisChannelItem(
            "Saved",
            "pcp://example/saved",
            Description: "last known metadata",
            ChannelId: "0123456789ABCDEF0123456789ABCDEF");
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection([
                GenesisFavoriteChannel.FromChannel(savedChannel),
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
        Assert.Equal(
            GenesisGuiNextFavoriteToggleResult.Removed,
            GenesisGuiNextFavoriteToggle.Apply(tabs, row));
        Assert.Empty(tabs.FavoriteChannels);
    }

    [Fact]
    public void PlayingTabNeverMutatesFavorites()
    {
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());
        tabs.SelectedTab = GenesisGuiNextTab.Playing;
        var channel = new GenesisChannelItem(
            "Display",
            "pcp://example/display");
        var row = new GenesisGuiNextNativeTabRow(channel, channel);

        Assert.Equal(
            GenesisGuiNextFavoriteToggleResult.Unavailable,
            GenesisGuiNextFavoriteToggle.Apply(tabs, row));
        Assert.False(tabs.IsFavorite(channel));
        Assert.Empty(tabs.FavoriteChannels);
    }
}
