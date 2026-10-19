namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextFavoriteNotificationRestartTests
{
    private const string ChannelId = "0123456789ABCDEF0123456789ABCDEF";
    private static readonly string[] YellowPageIds = ["yp"];

    [Fact]
    public async Task PersistedFavoriteTriggersFavoriteNotificationAfterReload()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "favorites.json");
        var favoriteStore = new JsonGenesisFavoriteChannelStore(path);
        var saved = Channel("Saved", "pcp://old.example/channel");
        await favoriteStore.SaveAsync([
            GenesisFavoriteChannel.FromChannel(saved),
        ]);

        await using var state = await GenesisGuiNextApplicationState.LoadAsync(
            favoriteStore,
            StaticNotificationStore.Instance,
            SilentSoundPlayer.Instance);
        var current = Channel("Current", "pcp://new.example/channel");
        var presenter = new RecordingPresenter();
        var soundPlayer = new RecordingSoundPlayer();
        var preferences = new GenesisChannelNotificationPreferences(
            NotifyNewChannels: false,
            NotifyFavoriteChannels: true,
            PlaySound: true);
        var publisher = new GenesisChannelNotificationPublisher(
            state.Favorites,
            () => preferences,
            new GenesisChannelNotificationDispatcher(presenter, soundPlayer));

        await publisher.PublishAsync(Activation(current));

        var notification = Assert.Single(presenter.Notifications);
        Assert.Same(current, notification.Channel);
        Assert.Equal(
            GenesisChannelNotificationReason.FavoriteChannel,
            notification.Reasons);
        Assert.True(notification.PlaySound);
        Assert.Equal(1, soundPlayer.PlayCount);
    }

    [Fact]
    public async Task ReloadedFavoriteFlowsThroughCoordinatorPresenceIntoFavoriteNotification()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "favorites.json");
        var favoriteStore = new JsonGenesisFavoriteChannelStore(path);
        await favoriteStore.SaveAsync([
            GenesisFavoriteChannel.FromChannel(
                Channel("Saved", "pcp://old.example/channel")),
        ]);

        await using var state = await GenesisGuiNextApplicationState.LoadAsync(
            favoriteStore,
            StaticNotificationStore.Instance,
            SilentSoundPlayer.Instance);
        var current = Channel("Current", "pcp://new.example/channel");
        var presenter = new RecordingPresenter();
        var soundPlayer = new RecordingSoundPlayer();
        var preferences = new GenesisChannelNotificationPreferences(
            NotifyNewChannels: false,
            NotifyFavoriteChannels: true,
            PlaySound: true);
        var notificationPublisher = new GenesisChannelNotificationPublisher(
            state.Favorites,
            () => preferences,
            new GenesisChannelNotificationDispatcher(presenter, soundPlayer));
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var coordinator = new GenesisChannelCatalogCoordinator(
            new FixedAggregatedCatalog(Snapshot(current)),
            viewModel,
            new GenesisGuiNextChannelPresencePublisher(
                state.Tabs,
                notificationPublisher));

        var result = await coordinator.RefreshAsync();

        Assert.Equal(GenesisChannelCatalogRefreshResult.Completed, result);
        Assert.Same(current, Assert.Single(viewModel.Channels));
        var presence = Assert.Single(coordinator.PresenceSnapshot!.Entries);
        Assert.Same(current, presence.Channel);
        Assert.True(presence.IsCurrentlyObserved);
        Assert.True(presence.IsNew);

        var notification = Assert.Single(presenter.Notifications);
        Assert.Same(current, notification.Channel);
        Assert.Equal(
            GenesisChannelNotificationReason.FavoriteChannel,
            notification.Reasons);
        Assert.True(notification.PlaySound);
        Assert.Equal(1, soundPlayer.PlayCount);
    }

    private static GenesisChannelPresenceSnapshot Activation(
        GenesisChannelItem channel)
    {
        var stableKey = GenesisChannelIdentity.GetStableKey(channel);
        var entry = new GenesisChannelPresenceEntry(
            stableKey,
            channel,
            GenesisChannelPresenceState.Active,
            IsCurrentlyObserved: true,
            IsNew: true,
            ConsecutiveMissingSuccessfulRefreshes: 0,
            KnownYellowPageIds: Array.Empty<string>());
        return new GenesisChannelPresenceSnapshot(
            [entry],
            [new GenesisChannelPresenceTransition(
                GenesisChannelPresenceTransitionKind.BecameActive,
                entry)]);
    }

    private static GenesisAggregatedChannelCatalogSnapshot Snapshot(
        GenesisChannelItem channel) =>
        new(
            [new GenesisAggregatedChannelCatalogEntry(
                channel,
                Array.AsReadOnly(YellowPageIds))],
            [new GenesisYellowPageCatalogReadStatus(
                new GenesisYellowPageDefinition(
                    "yp",
                    "YP",
                    new Uri("https://yp.example/index.txt")),
                GenesisYellowPageCatalogReadState.Succeeded)]);

    private static GenesisChannelItem Channel(string name, string source) =>
        new(
            name,
            source,
            Description: "metadata",
            Type: "FLV",
            ChannelId: ChannelId);

    private sealed class FixedAggregatedCatalog : IGenesisAggregatedChannelCatalog
    {
        private readonly GenesisAggregatedChannelCatalogSnapshot _snapshot;

        public FixedAggregatedCatalog(GenesisAggregatedChannelCatalogSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public ValueTask<GenesisAggregatedChannelCatalogSnapshot> ReadDetailedAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_snapshot);
        }

        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "Coordinator should use the detailed aggregate read.");
    }

    private sealed class RecordingPresenter : IGenesisChannelNotificationPresenter
    {
        public List<GenesisChannelNotification> Notifications { get; } = [];

        public ValueTask PresentAsync(
            GenesisChannelNotification notification,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Notifications.Add(notification);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingSoundPlayer : IGenesisSystemNotificationSoundPlayer
    {
        public int PlayCount { get; private set; }

        public ValueTask PlayAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PlayCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StaticNotificationStore :
        IGenesisChannelNotificationPreferenceStore
    {
        public static StaticNotificationStore Instance { get; } = new();

        private StaticNotificationStore()
        {
        }

        public ValueTask<GenesisChannelNotificationPreferences> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(GenesisChannelNotificationPreferences.Default);
        }

        public ValueTask SaveAsync(
            GenesisChannelNotificationPreferences preferences,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(preferences);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SilentSoundPlayer : IGenesisSystemNotificationSoundPlayer
    {
        public static SilentSoundPlayer Instance { get; } = new();

        private SilentSoundPlayer()
        {
        }

        public ValueTask PlayAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"PeerCast-Nova-Favorite-Notification-Restart-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
            }
        }
    }
}
