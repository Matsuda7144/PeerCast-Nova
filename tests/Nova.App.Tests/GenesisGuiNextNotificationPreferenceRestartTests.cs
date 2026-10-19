namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextNotificationPreferenceRestartTests
{
    private const string ChannelId = "0123456789ABCDEF0123456789ABCDEF";

    [Fact]
    public async Task PersistedPreferencesDriveFavoriteNotificationAfterReload()
    {
        using var temporary = new TemporaryDirectory();
        var favoriteStore = await SeedFavoriteAsync(temporary.Path);
        var notificationStore = new JsonGenesisChannelNotificationPreferenceStore(
            Path.Combine(temporary.Path, "notification-preferences.json"));
        var expected = new GenesisChannelNotificationPreferences(
            NotifyNewChannels: false,
            NotifyFavoriteChannels: true,
            PlaySound: false);
        await notificationStore.SaveAsync(expected);

        await using var state = await GenesisGuiNextApplicationState.LoadAsync(
            favoriteStore,
            notificationStore,
            SilentSoundPlayer.Instance);
        Assert.Equal(expected, state.Notifications.Current);

        var current = Channel("Current", "pcp://new.example/channel");
        var presenter = new RecordingPresenter();
        var soundPlayer = new RecordingSoundPlayer();
        var publisher = new GenesisChannelNotificationPublisher(
            state.Favorites,
            () => state.Notifications.Current,
            new GenesisChannelNotificationDispatcher(presenter, soundPlayer));

        await publisher.PublishAsync(Activation(current));

        var notification = Assert.Single(presenter.Notifications);
        Assert.Same(current, notification.Channel);
        Assert.Equal(
            GenesisChannelNotificationReason.FavoriteChannel,
            notification.Reasons);
        Assert.False(notification.PlaySound);
        Assert.Equal(0, soundPlayer.PlayCount);
    }

    [Fact]
    public async Task PersistedFavoriteNotificationDisableSuppressesAfterReload()
    {
        using var temporary = new TemporaryDirectory();
        var favoriteStore = await SeedFavoriteAsync(temporary.Path);
        var notificationStore = new JsonGenesisChannelNotificationPreferenceStore(
            Path.Combine(temporary.Path, "notification-preferences.json"));
        var expected = new GenesisChannelNotificationPreferences(
            NotifyNewChannels: false,
            NotifyFavoriteChannels: false,
            PlaySound: true);
        await notificationStore.SaveAsync(expected);

        await using var state = await GenesisGuiNextApplicationState.LoadAsync(
            favoriteStore,
            notificationStore,
            SilentSoundPlayer.Instance);
        Assert.Equal(expected, state.Notifications.Current);

        var presenter = new RecordingPresenter();
        var soundPlayer = new RecordingSoundPlayer();
        var publisher = new GenesisChannelNotificationPublisher(
            state.Favorites,
            () => state.Notifications.Current,
            new GenesisChannelNotificationDispatcher(presenter, soundPlayer));

        await publisher.PublishAsync(
            Activation(Channel("Current", "pcp://new.example/channel")));

        Assert.Empty(presenter.Notifications);
        Assert.Equal(0, soundPlayer.PlayCount);
    }

    private static async ValueTask<JsonGenesisFavoriteChannelStore> SeedFavoriteAsync(
        string directory)
    {
        var store = new JsonGenesisFavoriteChannelStore(
            Path.Combine(directory, "favorites.json"));
        await store.SaveAsync([
            GenesisFavoriteChannel.FromChannel(
                Channel("Saved", "pcp://old.example/channel")),
        ]);
        return store;
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

    private static GenesisChannelItem Channel(string name, string source) =>
        new(
            name,
            source,
            Description: "metadata",
            Type: "FLV",
            ChannelId: ChannelId);

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
                $"PeerCast-Nova-Notification-Preference-Restart-{Guid.NewGuid():N}");
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
