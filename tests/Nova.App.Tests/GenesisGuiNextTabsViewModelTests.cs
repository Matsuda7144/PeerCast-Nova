namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextTabsViewModelTests
{
    private const string ChannelId = "0123456789ABCDEF0123456789ABCDEF";
    private static readonly string[] YellowPageIds = ["yp"];

    [Fact]
    public void StartsOnChannelsAndPublishesTabChanges()
    {
        var viewModel = CreateViewModel();
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, eventArgs) => changed.Add(eventArgs.PropertyName);

        Assert.Equal(GenesisGuiNextTab.Channels, viewModel.SelectedTab);

        viewModel.SelectedTab = GenesisGuiNextTab.Favorites;

        Assert.Equal(GenesisGuiNextTab.Favorites, viewModel.SelectedTab);
        Assert.Contains(nameof(GenesisGuiNextTabsViewModel.SelectedTab), changed);
    }

    [Fact]
    public void ChannelAndNewViewsShareCurrentChannelInstances()
    {
        var viewModel = CreateViewModel();
        var channel = Channel("Shared");
        var presence = Presence(channel);
        var projection = GenesisChannelViewProjectionSnapshot.FromPresence(presence);

        viewModel.ApplyChannelViews(projection, presence);

        Assert.Same(channel, Assert.Single(viewModel.Channels));
        Assert.Same(channel, Assert.Single(viewModel.NewChannels));
        Assert.Equal(1, viewModel.ChannelCount);
        Assert.Equal(1, viewModel.NewChannelCount);
    }

    [Fact]
    public void SearchProjectsChannelsAndNewWithoutMutatingCanonicalViews()
    {
        var viewModel = CreateViewModel();
        var alpha = Channel("Alpha");
        var beta = new GenesisChannelItem(
            "Beta",
            "pcp://example/beta",
            Description: "Evening music",
            Type: "FLV");
        viewModel.ApplyChannelViews(
            new GenesisChannelViewProjectionSnapshot(
                [alpha, beta],
                [beta]));

        viewModel.SearchText = "beta evening";

        Assert.Equal(2, viewModel.ChannelCount);
        Assert.Same(beta, Assert.Single(viewModel.VisibleChannels));
        Assert.Same(beta, Assert.Single(viewModel.NewChannels));
        Assert.Same(beta, Assert.Single(viewModel.VisibleNewChannels));
        Assert.Equal(1, viewModel.VisibleChannelCount);
        Assert.Equal(1, viewModel.VisibleNewChannelCount);
    }

    [Fact]
    public void SearchMatchesOfflineFavoriteLastKnownMetadata()
    {
        var savedChannel = new GenesisChannelItem(
            "Saved",
            "pcp://example/saved",
            Description: "Late night jazz",
            Type: "FLV");
        var favorites = new GenesisFavoriteChannelCollection([
            GenesisFavoriteChannel.FromChannel(savedChannel),
        ]);
        var viewModel = new GenesisGuiNextTabsViewModel(favorites);
        viewModel.ApplyChannelViews(
            GenesisChannelViewProjectionSnapshot.FromCatalog(
                GenesisChannelCatalogSnapshot.Empty));

        viewModel.SearchText = "night jazz";

        var canonical = Assert.Single(viewModel.FavoriteChannels);
        var visible = Assert.Single(viewModel.VisibleFavoriteChannels);
        Assert.Same(canonical, visible);
        Assert.False(visible.IsOnline);
        Assert.Equal(1, viewModel.FavoriteChannelCount);
        Assert.Equal(1, viewModel.VisibleFavoriteChannelCount);

        viewModel.SearchText = "missing";

        Assert.Single(viewModel.FavoriteChannels);
        Assert.Empty(viewModel.VisibleFavoriteChannels);
    }

    [Fact]
    public void ExistingSearchReprojectsNewCatalogInstances()
    {
        var viewModel = CreateViewModel();
        viewModel.SearchText = "alpha";
        var initial = Channel("Alpha");
        viewModel.ApplyChannelViews(
            new GenesisChannelViewProjectionSnapshot([initial], [initial]));
        Assert.Same(initial, Assert.Single(viewModel.VisibleChannels));

        var refreshed = Channel("Alpha refreshed");
        viewModel.ApplyChannelViews(
            new GenesisChannelViewProjectionSnapshot([refreshed], []));

        Assert.Same(refreshed, Assert.Single(viewModel.VisibleChannels));
        Assert.Empty(viewModel.VisibleNewChannels);
    }

    [Fact]
    public void FavoriteProjectionUsesCurrentChannelWhileOnline()
    {
        var saved = GenesisFavoriteChannel.FromChannel(Channel("Saved"));
        var favorites = new GenesisFavoriteChannelCollection([saved]);
        var viewModel = new GenesisGuiNextTabsViewModel(favorites);
        var current = Channel("Current");
        var presence = Presence(current);

        viewModel.ApplyChannelViews(
            GenesisChannelViewProjectionSnapshot.FromPresence(presence),
            presence);

        var favorite = Assert.Single(viewModel.FavoriteChannels);
        Assert.True(favorite.IsOnline);
        Assert.Same(current, favorite.CurrentChannel);
        Assert.Equal(current.Source, favorite.PlayableSource);
    }

    [Fact]
    public void FavoriteRemainsVisibleButNotPlayableWhenOffline()
    {
        var savedChannel = Channel("Saved");
        var favorites = new GenesisFavoriteChannelCollection([
            GenesisFavoriteChannel.FromChannel(savedChannel),
        ]);
        var viewModel = new GenesisGuiNextTabsViewModel(favorites);

        viewModel.ApplyChannelViews(
            GenesisChannelViewProjectionSnapshot.FromCatalog(
                GenesisChannelCatalogSnapshot.Empty));

        var favorite = Assert.Single(viewModel.FavoriteChannels);
        Assert.False(favorite.IsOnline);
        Assert.Null(favorite.CurrentChannel);
        Assert.Null(favorite.PlayableSource);
        Assert.Equal(savedChannel.Name, favorite.DisplayName);
    }

    [Fact]
    public void AddAndRemoveFavoriteRefreshesFavoriteTabProjection()
    {
        var viewModel = CreateViewModel();
        var channel = Channel("Favorite");
        var presence = Presence(channel);
        viewModel.ApplyChannelViews(
            GenesisChannelViewProjectionSnapshot.FromPresence(presence),
            presence);

        Assert.Equal(
            GenesisFavoriteChannelMutationResult.Added,
            viewModel.AddFavorite(channel));
        Assert.True(viewModel.IsFavorite(channel));
        Assert.Single(viewModel.FavoriteChannels);

        Assert.True(viewModel.RemoveFavorite(channel));
        Assert.False(viewModel.IsFavorite(channel));
        Assert.Empty(viewModel.FavoriteChannels);
    }

    [Fact]
    public void StoppedPlaybackProducesNoPlayingRows()
    {
        var viewModel = CreateViewModel();

        viewModel.UpdatePlayback(PlaybackSessionSnapshot.Stopped);

        Assert.Empty(viewModel.Playing);
        Assert.Equal(0, viewModel.PlayingCount);
    }

    [Theory]
    [InlineData(PlaybackSessionState.Starting)]
    [InlineData(PlaybackSessionState.Playing)]
    [InlineData(PlaybackSessionState.Stopping)]
    public void OwnedPlaybackProjectsCurrentState(
        PlaybackSessionState state)
    {
        var viewModel = CreateViewModel();
        var snapshot = new PlaybackSessionSnapshot(
            state,
            "pcp://example/channel",
            state == PlaybackSessionState.Faulted
                ? "failed"
                : null);

        viewModel.UpdatePlayback(snapshot);

        var playing = Assert.Single(viewModel.Playing);
        Assert.Equal(state, playing.State);
        Assert.Equal(snapshot.Source, playing.Source);
        Assert.Equal(snapshot.ErrorMessage, playing.ErrorMessage);
    }

    [Theory]
    [InlineData(PlaybackSessionState.Stopped)]
    [InlineData(PlaybackSessionState.Faulted)]
    public void PlaybackWithoutCurrentOwnershipProducesNoPlayingRows(
        PlaybackSessionState state)
    {
        var viewModel = CreateViewModel();
        var snapshot = new PlaybackSessionSnapshot(
            state,
            "pcp://example/channel",
            state == PlaybackSessionState.Faulted ? "failed" : null);

        viewModel.UpdatePlayback(snapshot);

        Assert.Empty(viewModel.Playing);
        Assert.Equal(0, viewModel.PlayingCount);
    }

    [Fact]
    public void SearchDoesNotFilterTruthfulPlayingProjection()
    {
        var viewModel = CreateViewModel();
        var snapshot = new PlaybackSessionSnapshot(
            PlaybackSessionState.Playing,
            "pcp://example/channel",
            null);
        viewModel.UpdatePlayback(snapshot);

        viewModel.SearchText = "no-channel-match";

        var playing = Assert.Single(viewModel.Playing);
        Assert.Equal(snapshot.Source, playing.Source);
    }

    [Fact]
    public void IdenticalPlaybackSnapshotDoesNotRepublishPlayingState()
    {
        var viewModel = CreateViewModel();
        var snapshot = new PlaybackSessionSnapshot(
            PlaybackSessionState.Playing,
            "pcp://example/channel",
            null);
        var playingChangeCount = 0;
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(GenesisGuiNextTabsViewModel.Playing))
            {
                playingChangeCount++;
            }
        };

        viewModel.UpdatePlayback(snapshot);
        viewModel.UpdatePlayback(snapshot);

        Assert.Equal(1, playingChangeCount);
    }

    private static GenesisGuiNextTabsViewModel CreateViewModel() =>
        new(
            new GenesisFavoriteChannelCollection());

    private static GenesisChannelItem Channel(string name) =>
        new(
            name,
            $"pcp://example/channel/{ChannelId}",
            ChannelId: ChannelId);

    private static GenesisChannelPresenceSnapshot Presence(
        GenesisChannelItem channel)
    {
        var tracker = new GenesisChannelPresenceTracker();
        return tracker.ApplySuccessfulRefresh(
            new GenesisAggregatedChannelCatalogSnapshot(
                [
                    new GenesisAggregatedChannelCatalogEntry(
                        channel,
                        Array.AsReadOnly(YellowPageIds)),
                ],
                [
                    new GenesisYellowPageCatalogReadStatus(
                        new GenesisYellowPageDefinition(
                            "yp",
                            "YP",
                            new Uri("https://yp.example/index.txt")),
                        GenesisYellowPageCatalogReadState.Succeeded),
                ]));
    }
}
