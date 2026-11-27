namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextFavoriteNativeRefreshContractTests
{
    private const string ChannelId = "0123456789ABCDEF0123456789ABCDEF";

    [Fact]
    public void FavoriteMutationsPublishVisibleFavoriteProjectionChanges()
    {
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());
        var channel = new GenesisChannelItem(
            "Favorite",
            $"pcp://example/channel/{ChannelId}",
            ChannelId: ChannelId);
        var changed = new List<string?>();
        tabs.PropertyChanged += (_, eventArgs) => changed.Add(eventArgs.PropertyName);

        Assert.Equal(
            GenesisFavoriteChannelMutationResult.Added,
            tabs.AddFavorite(channel));
        Assert.Contains(
            nameof(GenesisGuiNextTabsViewModel.VisibleFavoriteChannels),
            changed);

        changed.Clear();

        Assert.True(tabs.RemoveFavorite(channel));
        Assert.Contains(
            nameof(GenesisGuiNextTabsViewModel.VisibleFavoriteChannels),
            changed);
    }
}
