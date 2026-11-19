namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextFavoritePresencePublisherIntegrationTests
{
    private const string ChannelId = "0123456789ABCDEF0123456789ABCDEF";
    private static readonly string[] YellowPageIds = ["yp"];

    [Fact]
    public async Task CoordinatorPresenceRefreshUpdatesFavoriteLastKnownMetadata()
    {
        var saved = Channel(
            "Saved",
            "pcp://old.example/channel",
            "old metadata");
        var updated = Channel(
            "Updated",
            "pcp://new.example/channel",
            "new metadata");
        var favorites = new GenesisFavoriteChannelCollection([
            GenesisFavoriteChannel.FromChannel(saved),
        ]);
        var tabs = new GenesisGuiNextTabsViewModel(favorites);
        var catalog = new SequencedAggregatedCatalog();
        catalog.Enqueue(Snapshot(updated));
        var coordinator = new GenesisChannelCatalogCoordinator(
            catalog,
            new GenesisMainViewModel(new FakePlaybackSessionController()),
            new GenesisGuiNextChannelPresencePublisher(
                tabs,
                EmptyGenesisChannelPresenceNotificationPublisher.Instance));

        var result = await coordinator.RefreshAsync();

        Assert.Equal(GenesisChannelCatalogRefreshResult.Completed, result);
        var favorite = Assert.Single(favorites.Snapshot);
        Assert.Equal("Updated", favorite.Name);
        Assert.Equal(updated.Source, favorite.LastKnownSource);
        Assert.Equal("new metadata", favorite.Description);
    }

    [Fact]
    public async Task TransientCoordinatorAbsenceDoesNotRegressFavoriteMetadata()
    {
        var saved = Channel(
            "Saved",
            "pcp://old.example/channel",
            "old metadata");
        var updated = Channel(
            "Updated",
            "pcp://new.example/channel",
            "new metadata");
        var favorites = new GenesisFavoriteChannelCollection([
            GenesisFavoriteChannel.FromChannel(saved),
        ]);
        var tabs = new GenesisGuiNextTabsViewModel(favorites);
        var catalog = new SequencedAggregatedCatalog();
        catalog.Enqueue(Snapshot(updated));
        catalog.Enqueue(Snapshot());
        var coordinator = new GenesisChannelCatalogCoordinator(
            catalog,
            new GenesisMainViewModel(new FakePlaybackSessionController()),
            new GenesisGuiNextChannelPresencePublisher(
                tabs,
                EmptyGenesisChannelPresenceNotificationPublisher.Instance));

        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await coordinator.RefreshAsync());
        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await coordinator.RefreshAsync());

        var presence = Assert.Single(coordinator.PresenceSnapshot!.Entries);
        Assert.False(presence.IsCurrentlyObserved);
        var favorite = Assert.Single(favorites.Snapshot);
        Assert.Equal("Updated", favorite.Name);
        Assert.Equal(updated.Source, favorite.LastKnownSource);
        Assert.Equal("new metadata", favorite.Description);
    }

    [Fact]
    public async Task ProvenEndThenReturnRestoresFavoriteNativeInteractivity()
    {
        var saved = Channel(
            "Saved",
            "pcp://saved.example/channel",
            "saved metadata");
        var initial = Channel(
            "Initial",
            "pcp://initial.example/channel",
            "initial metadata");
        var returned = Channel(
            "Returned",
            "pcp://returned.example/channel",
            "returned metadata");
        var favorites = new GenesisFavoriteChannelCollection([
            GenesisFavoriteChannel.FromChannel(saved),
        ]);
        var tabs = new GenesisGuiNextTabsViewModel(favorites)
        {
            SelectedTab = GenesisGuiNextTab.Favorites,
        };
        var catalog = new SequencedAggregatedCatalog();
        catalog.Enqueue(Snapshot(initial));
        catalog.Enqueue(Snapshot());
        catalog.Enqueue(Snapshot());
        catalog.Enqueue(Snapshot());
        catalog.Enqueue(Snapshot(returned));
        var coordinator = new GenesisChannelCatalogCoordinator(
            catalog,
            new GenesisMainViewModel(new FakePlaybackSessionController()),
            new GenesisGuiNextChannelPresencePublisher(
                tabs,
                EmptyGenesisChannelPresenceNotificationPublisher.Instance));

        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await coordinator.RefreshAsync());
        var initialRow = Assert.Single(
            GenesisGuiNextNativeTabProjection.Create(tabs));
        Assert.Same(initial, initialRow.DisplayChannel);
        Assert.Same(initial, initialRow.InteractiveChannel);
        Assert.True(initialRow.IsInteractive);

        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await coordinator.RefreshAsync());
        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await coordinator.RefreshAsync());
        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await coordinator.RefreshAsync());

        var endedSnapshot = coordinator.PresenceSnapshot!;
        var endedPresence = Assert.Single(endedSnapshot.Entries);
        Assert.Equal(GenesisChannelPresenceState.Ended, endedPresence.State);
        Assert.False(endedPresence.IsCurrentlyObserved);
        Assert.Equal(
            GenesisChannelPresenceTransitionKind.BecameEnded,
            Assert.Single(endedSnapshot.Transitions).Kind);
        var offlineFavorite = Assert.Single(tabs.FavoriteChannels);
        Assert.False(offlineFavorite.IsOnline);
        Assert.Null(offlineFavorite.CurrentChannel);
        Assert.Null(offlineFavorite.PlayableSource);
        var endedRow = Assert.Single(
            GenesisGuiNextNativeTabProjection.Create(tabs));
        Assert.Equal(initial.Name, endedRow.DisplayChannel.Name);
        Assert.Equal(initial.Source, endedRow.DisplayChannel.Source);
        Assert.Equal("現在の詳細は未取得", endedRow.DisplayChannel.Description);
        Assert.Null(endedRow.DisplayChannel.Type);
        Assert.True(endedRow.IsUnobservedFavorite);
        Assert.Null(endedRow.InteractiveChannel);
        Assert.False(endedRow.IsInteractive);
        var endedDetails = GenesisGuiNextChannelDetailsProjection.FromSelection([endedRow], 0);
        Assert.Empty(endedDetails.Source);
        Assert.Empty(endedDetails.ChannelId);
        Assert.Equal("現在の詳細は未取得", endedDetails.Description);

        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await coordinator.RefreshAsync());

        var returnedSnapshot = coordinator.PresenceSnapshot!;
        var returnedPresence = Assert.Single(returnedSnapshot.Entries);
        Assert.Equal(GenesisChannelPresenceState.Active, returnedPresence.State);
        Assert.True(returnedPresence.IsCurrentlyObserved);
        Assert.Equal(
            GenesisChannelPresenceTransitionKind.BecameActive,
            Assert.Single(returnedSnapshot.Transitions).Kind);
        var returnedFavorite = Assert.Single(tabs.FavoriteChannels);
        Assert.True(returnedFavorite.IsOnline);
        Assert.Same(returned, returnedFavorite.CurrentChannel);
        Assert.Equal(returned.Source, returnedFavorite.PlayableSource);
        Assert.Same(returned, Assert.Single(tabs.NewChannels));
        var returnedRow = Assert.Single(
            GenesisGuiNextNativeTabProjection.Create(tabs));
        Assert.Same(returned, returnedRow.DisplayChannel);
        Assert.Same(returned, returnedRow.InteractiveChannel);
        Assert.True(returnedRow.IsInteractive);
        Assert.False(returnedRow.IsUnobservedFavorite);
        Assert.Equal(returned.Description,
            GenesisGuiNextChannelDetailsProjection.FromSelection([returnedRow], 0).Description);
        var lastKnownFavorite = Assert.Single(favorites.Snapshot);
        Assert.Equal(returned.Name, lastKnownFavorite.Name);
        Assert.Equal(returned.Source, lastKnownFavorite.LastKnownSource);
        Assert.Equal(returned.Description, lastKnownFavorite.Description);
    }

    private static GenesisAggregatedChannelCatalogSnapshot Snapshot(
        params GenesisChannelItem[] channels) =>
        new(
            Array.AsReadOnly(
                channels
                    .Select(channel => new GenesisAggregatedChannelCatalogEntry(
                        channel,
                        Array.AsReadOnly(YellowPageIds)))
                    .ToArray()),
            [new GenesisYellowPageCatalogReadStatus(
                new GenesisYellowPageDefinition(
                    "yp",
                    "YP",
                    new Uri("https://yp.example/index.txt")),
                GenesisYellowPageCatalogReadState.Succeeded)]);

    private static GenesisChannelItem Channel(
        string name,
        string source,
        string description) =>
        new(
            name,
            source,
            Description: description,
            Type: "FLV",
            ChannelId: ChannelId);

    private sealed class SequencedAggregatedCatalog : IGenesisAggregatedChannelCatalog
    {
        private readonly Queue<GenesisAggregatedChannelCatalogSnapshot> _snapshots = [];

        public void Enqueue(GenesisAggregatedChannelCatalogSnapshot snapshot)
        {
            _snapshots.Enqueue(snapshot);
        }

        public ValueTask<GenesisAggregatedChannelCatalogSnapshot> ReadDetailedAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_snapshots.Dequeue());
        }

        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "Coordinator should use the detailed aggregate read.");
    }
}
