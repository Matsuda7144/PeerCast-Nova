namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextFavoriteFeedbackTests
{
    private const string ChannelId = "0123456789ABCDEF0123456789ABCDEF";

    [Fact]
    public void ResolverMapsOnlyPersistenceFailureToWarning()
    {
        var warning = GenesisGuiNextFavoriteFeedbackResolver.Resolve(
            GenesisGuiNextFavoriteToggleResult.PersistenceFailed);

        Assert.NotNull(warning);
        Assert.Equal("Favorite changes could not be saved.", warning.Message);
        Assert.Equal("Favorites", warning.Caption);
        Assert.Null(GenesisGuiNextFavoriteFeedbackResolver.Resolve(
            GenesisGuiNextFavoriteToggleResult.Unavailable));
        Assert.Null(GenesisGuiNextFavoriteFeedbackResolver.Resolve(
            GenesisGuiNextFavoriteToggleResult.Added));
        Assert.Null(GenesisGuiNextFavoriteFeedbackResolver.Resolve(
            GenesisGuiNextFavoriteToggleResult.Removed));
    }

    [Fact]
    public async Task SessionPresentsPersistenceFailureForBoundTabsOnly()
    {
        var favorites = new GenesisFavoriteChannelCollection();
        favorites.BindPersistence(static _ => false);
        var tabs = new GenesisGuiNextTabsViewModel(favorites);
        var presented = new List<GenesisGuiNextFavoriteFeedbackPresentation>();
        await using var session = GenesisGuiNextFavoriteFeedbackSession.Open(
            tabs,
            presented.Add);
        var channel = CreateChannel("Bound");

        var result = GenesisGuiNextFavoriteToggle.Apply(
            tabs,
            new GenesisGuiNextNativeTabRow(channel, channel));

        Assert.Equal(GenesisGuiNextFavoriteToggleResult.PersistenceFailed, result);
        var warning = Assert.Single(presented);
        Assert.Equal("Favorite changes could not be saved.", warning.Message);
        Assert.Equal("Favorites", warning.Caption);
        Assert.False(tabs.IsFavorite(channel));

        var otherFavorites = new GenesisFavoriteChannelCollection();
        otherFavorites.BindPersistence(static _ => false);
        var otherTabs = new GenesisGuiNextTabsViewModel(otherFavorites);
        var otherChannel = CreateChannel("Other");

        Assert.Equal(
            GenesisGuiNextFavoriteToggleResult.PersistenceFailed,
            GenesisGuiNextFavoriteToggle.Apply(
                otherTabs,
                new GenesisGuiNextNativeTabRow(otherChannel, otherChannel)));
        Assert.Single(presented);
    }

    [Fact]
    public async Task SessionDoesNotPresentSuccessfulOrUnavailableResults()
    {
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());
        var presented = new List<GenesisGuiNextFavoriteFeedbackPresentation>();
        await using var session = GenesisGuiNextFavoriteFeedbackSession.Open(
            tabs,
            presented.Add);
        var channel = CreateChannel("Live");
        var row = new GenesisGuiNextNativeTabRow(channel, channel);

        Assert.Equal(
            GenesisGuiNextFavoriteToggleResult.Added,
            GenesisGuiNextFavoriteToggle.Apply(tabs, row));
        Assert.Equal(
            GenesisGuiNextFavoriteToggleResult.Removed,
            GenesisGuiNextFavoriteToggle.Apply(tabs, row));
        tabs.SelectedTab = GenesisGuiNextTab.Playing;
        Assert.Equal(
            GenesisGuiNextFavoriteToggleResult.Unavailable,
            GenesisGuiNextFavoriteToggle.Apply(tabs, row));

        Assert.Empty(presented);
    }

    [Fact]
    public async Task FeedbackFailureCannotReplaceToggleResult()
    {
        var favorites = new GenesisFavoriteChannelCollection();
        favorites.BindPersistence(static _ => false);
        var tabs = new GenesisGuiNextTabsViewModel(favorites);
        var session = GenesisGuiNextFavoriteFeedbackSession.Open(
            tabs,
            static _ => throw new InvalidOperationException("Presentation failed."));
        var channel = CreateChannel("Failure");

        try
        {
            Assert.Equal(
                GenesisGuiNextFavoriteToggleResult.PersistenceFailed,
                GenesisGuiNextFavoriteToggle.Apply(
                    tabs,
                    new GenesisGuiNextNativeTabRow(channel, channel)));
            Assert.False(tabs.IsFavorite(channel));
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    private static GenesisChannelItem CreateChannel(string name) =>
        new(
            name,
            $"pcp://example/{name.ToLowerInvariant()}",
            ChannelId: ChannelId);
}
