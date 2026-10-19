namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextCrossTabInteractionContractTests
{
    [Fact]
    public void ChannelsFallbackResolverUsesVisibleInteractiveRow()
    {
        var alpha = Channel("Alpha", "alpha");
        var beta = Channel("Beta", "beta");
        var viewModel = CreateViewModel(alpha, beta);
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());
        tabs.ApplyChannelViews(
            new GenesisChannelViewProjectionSnapshot([alpha, beta], [beta]));
        tabs.SearchText = "beta";
        tabs.SelectedTab = GenesisGuiNextTab.Channels;

        var row = Assert.IsType<GenesisGuiNextNativeTabRow>(
            GenesisChannelDirectContextMenuRowResolver.Resolve(
                rowIndex: 0,
                registeredRow: null,
                viewModel,
                tabs));

        Assert.Same(beta, row.DisplayChannel);
        Assert.Same(beta, row.InteractiveChannel);
        Assert.True(row.IsInteractive);
    }

    [Fact]
    public void NewFallbackResolverUsesCurrentNewInteractiveRow()
    {
        var existing = Channel("Existing", "existing");
        var added = Channel("Added", "added");
        var viewModel = CreateViewModel(existing, added);
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());
        tabs.ApplyChannelViews(
            new GenesisChannelViewProjectionSnapshot(
                [existing, added],
                [added]));
        tabs.SelectedTab = GenesisGuiNextTab.New;

        var row = Assert.IsType<GenesisGuiNextNativeTabRow>(
            GenesisChannelDirectContextMenuRowResolver.Resolve(
                rowIndex: 0,
                registeredRow: null,
                viewModel,
                tabs));

        Assert.Same(added, row.DisplayChannel);
        Assert.Same(added, row.InteractiveChannel);
        Assert.True(row.IsInteractive);
    }

    [Fact]
    public void FavoritesFallbackResolverUsesCurrentLiveInteractiveIdentity()
    {
        var saved = Channel("Saved", "shared");
        var current = new GenesisChannelItem(
            "Current",
            saved.Source,
            Description: "Current metadata",
            Type: "FLV",
            ChannelId: saved.ChannelId);
        var favorites = new GenesisFavoriteChannelCollection([
            GenesisFavoriteChannel.FromChannel(saved),
        ]);
        var viewModel = CreateViewModel(current);
        var tabs = new GenesisGuiNextTabsViewModel(favorites);
        tabs.ApplyChannelViews(
            new GenesisChannelViewProjectionSnapshot([current], []));
        tabs.SelectedTab = GenesisGuiNextTab.Favorites;

        var row = Assert.IsType<GenesisGuiNextNativeTabRow>(
            GenesisChannelDirectContextMenuRowResolver.Resolve(
                rowIndex: 0,
                registeredRow: null,
                viewModel,
                tabs));

        Assert.Same(current, row.DisplayChannel);
        Assert.Same(current, row.InteractiveChannel);
        Assert.True(row.IsInteractive);
    }

    [Fact]
    public void OfflineFavoriteFallbackRemainsContextTargetButNotPlaybackTarget()
    {
        var saved = Channel("Saved offline", "offline");
        var favorites = new GenesisFavoriteChannelCollection([
            GenesisFavoriteChannel.FromChannel(saved),
        ]);
        var viewModel = CreateViewModel();
        var tabs = new GenesisGuiNextTabsViewModel(favorites);
        tabs.ApplyChannelViews(
            GenesisChannelViewProjectionSnapshot.FromCatalog(
                GenesisChannelCatalogSnapshot.Empty));
        tabs.SelectedTab = GenesisGuiNextTab.Favorites;

        var row = Assert.IsType<GenesisGuiNextNativeTabRow>(
            GenesisChannelDirectContextMenuRowResolver.Resolve(
                rowIndex: 0,
                registeredRow: null,
                viewModel,
                tabs));
        GenesisGuiNextNativeTabRow? opened = null;

        var openedMenu = GenesisChannelDirectContextMenuDispatch.TryOpen(
            row,
            _ => true,
            candidate => opened = candidate);

        Assert.True(openedMenu);
        Assert.Same(row, opened);
        Assert.Equal(saved.Name, row.DisplayChannel.Name);
        Assert.Equal(saved.Source, row.DisplayChannel.Source);
        Assert.Null(row.InteractiveChannel);
        Assert.False(row.IsInteractive);
    }

    [Fact]
    public void PlayingFallbackUsesTruthfulNonInteractiveRuntimeRow()
    {
        var viewModel = CreateViewModel();
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());
        var snapshot = new PlaybackSessionSnapshot(
            PlaybackSessionState.Playing,
            "pcp://example/playing",
            null);
        tabs.UpdatePlayback(snapshot);
        tabs.SelectedTab = GenesisGuiNextTab.Playing;

        var row = Assert.IsType<GenesisGuiNextNativeTabRow>(
            GenesisChannelDirectContextMenuRowResolver.Resolve(
                rowIndex: 0,
                registeredRow: null,
                viewModel,
                tabs));

        Assert.Equal(snapshot.Source, row.DisplayChannel.Name);
        Assert.Contains(snapshot.Source!, row.DisplayChannel.DetailsText);
        Assert.Null(row.InteractiveChannel);
        Assert.False(row.IsInteractive);
    }

    private static GenesisMainViewModel CreateViewModel(
        params GenesisChannelItem[] channels)
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels(channels);
        return viewModel;
    }

    private static GenesisChannelItem Channel(string name, string key) =>
        new(
            name,
            $"pcp://example/{key}",
            Description: $"{name} description",
            Type: "FLV",
            ChannelId: key);
}
