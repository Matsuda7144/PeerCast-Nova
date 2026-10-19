namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextApplicationStateTests
{
    private const string ChannelId = "0123456789ABCDEF0123456789ABCDEF";

    [Fact]
    public async Task LoadSharesPersistedFavoritesAndNotificationPreferences()
    {
        var favorite = GenesisFavoriteChannel.FromChannel(Channel("Saved"));
        var favoriteStore = new RecordingFavoriteStore([favorite]);
        var notificationStore = new RecordingNotificationStore(
            new(
                NotifyNewChannels: false,
                NotifyFavoriteChannels: true,
                PlaySound: false));
        var soundPlayer = new RecordingSoundPlayer();

        await using var state = await GenesisGuiNextApplicationState.LoadAsync(
            favoriteStore,
            notificationStore,
            soundPlayer);

        Assert.Equal(favorite, Assert.Single(state.Favorites.Snapshot));
        var projected = Assert.Single(state.Tabs.FavoriteChannels);
        Assert.Equal(favorite, projected.Favorite);
        Assert.False(projected.IsOnline);
        Assert.Equal(notificationStore.Loaded, state.Notifications.Current);
    }

    [Fact]
    public async Task DisposePersistsFavoriteAndNotificationChanges()
    {
        var favoriteStore = new RecordingFavoriteStore([]);
        var notificationStore = new RecordingNotificationStore(
            GenesisChannelNotificationPreferences.Default);
        var state = await GenesisGuiNextApplicationState.LoadAsync(
            favoriteStore,
            notificationStore,
            new RecordingSoundPlayer());
        var channel = Channel("Favorite");

        Assert.Equal(
            GenesisFavoriteChannelMutationResult.Added,
            state.Tabs.AddFavorite(channel));
        state.Notifications.SetNotifyNewChannels(false);
        state.Notifications.SetNotifyFavoriteChannels(false);
        state.Notifications.SetPlaySound(false);

        await state.DisposeAsync();

        Assert.Equal(
            GenesisFavoriteChannel.FromChannel(channel),
            Assert.Single(favoriteStore.Saved));
        Assert.Equal(
            new GenesisChannelNotificationPreferences(
                NotifyNewChannels: false,
                NotifyFavoriteChannels: false,
                PlaySound: false),
            notificationStore.Saved);
    }

    [Fact]
    public async Task TestSoundRunsEvenWhenAutomaticSoundIsDisabled()
    {
        var soundPlayer = new RecordingSoundPlayer();
        await using var state = await GenesisGuiNextApplicationState.LoadAsync(
            new RecordingFavoriteStore([]),
            new RecordingNotificationStore(
                GenesisChannelNotificationPreferences.Default with { PlaySound = false }),
            soundPlayer);

        await state.Notifications.TestSoundAsync();

        Assert.Equal(1, soundPlayer.PlayCount);
    }

    [Fact]
    public async Task PersistenceLoadFailuresFallBackToUsableDefaults()
    {
        await using var state = await GenesisGuiNextApplicationState.LoadAsync(
            new RecordingFavoriteStore([], loadThrows: true),
            new RecordingNotificationStore(
                new(false, false, false),
                loadThrows: true),
            new RecordingSoundPlayer());

        Assert.Empty(state.Favorites.Snapshot);
        Assert.Equal(
            GenesisChannelNotificationPreferences.Default,
            state.Notifications.Current);
    }

    [Fact]
    public async Task OneSaveFailureDoesNotBlockOtherPersistence()
    {
        var favoriteStore = new RecordingFavoriteStore([], saveThrows: true);
        var notificationStore = new RecordingNotificationStore(
            GenesisChannelNotificationPreferences.Default);
        var state = await GenesisGuiNextApplicationState.LoadAsync(
            favoriteStore,
            notificationStore,
            new RecordingSoundPlayer());
        state.Notifications.SetPlaySound(false);

        await state.DisposeAsync();

        Assert.Equal(1, favoriteStore.SaveCount);
        Assert.Equal(2, notificationStore.SaveCount);
        Assert.False(notificationStore.Saved!.PlaySound);
    }

    [Fact]
    public async Task SystemRuntimeProjectsTabsWhenNativeNotificationsAreUnavailable()
    {
        var channel = Channel("Live");
        await using var state = await GenesisGuiNextApplicationState.LoadAsync(
            new RecordingFavoriteStore([GenesisFavoriteChannel.FromChannel(channel)]),
            new RecordingNotificationStore(GenesisChannelNotificationPreferences.Default),
            new RecordingSoundPlayer());
        var runtime = SystemGenesisGuiNextNotificationRuntime.Create(0, state);

        try
        {
            await runtime.Publisher.PublishAsync(Activation(channel));

            Assert.Same(channel, Assert.Single(state.Tabs.Channels));
            Assert.Same(channel, Assert.Single(state.Tabs.NewChannels));
            var favorite = Assert.Single(state.Tabs.FavoriteChannels);
            Assert.True(favorite.IsOnline);
            Assert.Same(channel, favorite.CurrentChannel);
        }
        finally
        {
            await runtime.Lifetime.DisposeAsync();
        }
    }

    private static GenesisChannelItem Channel(string name) =>
        new(
            name,
            $"pcp://example/channel/{ChannelId}",
            Type: "FLV",
            ChannelId: ChannelId);

    private static GenesisChannelPresenceSnapshot Activation(GenesisChannelItem channel)
    {
        var entry = new GenesisChannelPresenceEntry(
            GenesisChannelIdentity.GetStableKey(channel),
            channel,
            GenesisChannelPresenceState.Active,
            IsCurrentlyObserved: true,
            IsNew: true,
            ConsecutiveMissingSuccessfulRefreshes: 0,
            KnownYellowPageIds: Array.Empty<string>());
        return new(
            [entry],
            [new(GenesisChannelPresenceTransitionKind.BecameActive, entry)]);
    }

    private sealed class RecordingFavoriteStore : IGenesisFavoriteChannelStore
    {
        private readonly IReadOnlyList<GenesisFavoriteChannel> _loaded;
        private readonly bool _loadThrows;
        private readonly bool _saveThrows;

        public RecordingFavoriteStore(
            IReadOnlyList<GenesisFavoriteChannel> loaded,
            bool loadThrows = false,
            bool saveThrows = false)
        {
            _loaded = loaded;
            _loadThrows = loadThrows;
            _saveThrows = saveThrows;
        }

        public IReadOnlyList<GenesisFavoriteChannel> Saved { get; private set; } =
            Array.Empty<GenesisFavoriteChannel>();

        public int SaveCount { get; private set; }

        public ValueTask<IReadOnlyList<GenesisFavoriteChannel>> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_loadThrows)
            {
                throw new InvalidDataException("Favorite load failed.");
            }

            return ValueTask.FromResult(_loaded);
        }

        public ValueTask SaveAsync(
            IReadOnlyList<GenesisFavoriteChannel> favorites,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(favorites);
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            if (_saveThrows)
            {
                throw new IOException("Favorite save failed.");
            }

            Saved = Array.AsReadOnly(favorites.ToArray());
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingNotificationStore :
        IGenesisChannelNotificationPreferenceStore
    {
        private readonly bool _loadThrows;
        private readonly bool _saveThrows;

        public RecordingNotificationStore(
            GenesisChannelNotificationPreferences loaded,
            bool loadThrows = false,
            bool saveThrows = false)
        {
            Loaded = loaded;
            _loadThrows = loadThrows;
            _saveThrows = saveThrows;
        }

        public GenesisChannelNotificationPreferences Loaded { get; }

        public GenesisChannelNotificationPreferences? Saved { get; private set; }

        public int SaveCount { get; private set; }

        public ValueTask<GenesisChannelNotificationPreferences> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_loadThrows)
            {
                throw new InvalidDataException("Notification preference load failed.");
            }

            return ValueTask.FromResult(Loaded);
        }

        public ValueTask SaveAsync(
            GenesisChannelNotificationPreferences preferences,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(preferences);
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            if (_saveThrows)
            {
                throw new IOException("Notification preference save failed.");
            }

            Saved = preferences;
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
}
