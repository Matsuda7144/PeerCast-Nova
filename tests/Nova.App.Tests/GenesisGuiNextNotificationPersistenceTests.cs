namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextNotificationPersistenceTests
{
    [Fact]
    public async Task PreferenceTogglePersistsBeforeShutdown()
    {
        var initial = GenesisChannelNotificationPreferences.Default;
        var notificationStore = new RecordingNotificationStore(initial);
        var state = await GenesisGuiNextApplicationState.LoadAsync(
            EmptyFavoriteStore.Instance,
            notificationStore,
            SilentSoundPlayer.Instance);

        try
        {
            state.Notifications.SetNotifyNewChannels(!initial.NotifyNewChannels);

            Assert.Equal(1, notificationStore.SaveCount);
            Assert.NotNull(notificationStore.Saved);
            Assert.Equal(!initial.NotifyNewChannels, notificationStore.Saved.NotifyNewChannels);
            Assert.Equal(initial.NotifyFavoriteChannels, notificationStore.Saved.NotifyFavoriteChannels);
            Assert.Equal(initial.PlaySound, notificationStore.Saved.PlaySound);
            Assert.Equal(notificationStore.Saved, state.Notifications.Current);
        }
        finally
        {
            await state.DisposeAsync();
        }
    }

    [Fact]
    public async Task PreferenceSaveFailureLeavesPublishedSettingsUnchanged()
    {
        var initial = GenesisChannelNotificationPreferences.Default;
        var notificationStore = new RecordingNotificationStore(
            initial,
            saveThrows: true);
        var state = await GenesisGuiNextApplicationState.LoadAsync(
            EmptyFavoriteStore.Instance,
            notificationStore,
            SilentSoundPlayer.Instance);

        try
        {
            state.Notifications.SetPlaySound(!initial.PlaySound);

            Assert.Equal(1, notificationStore.SaveCount);
            Assert.Equal(initial, state.Notifications.Current);
        }
        finally
        {
            await state.DisposeAsync();
        }
    }

    [Fact]
    public async Task UnchangedPreferenceDoesNotRewriteStore()
    {
        var initial = GenesisChannelNotificationPreferences.Default;
        var notificationStore = new RecordingNotificationStore(initial);
        var state = await GenesisGuiNextApplicationState.LoadAsync(
            EmptyFavoriteStore.Instance,
            notificationStore,
            SilentSoundPlayer.Instance);

        try
        {
            state.Notifications.SetNotifyFavoriteChannels(initial.NotifyFavoriteChannels);

            Assert.Equal(0, notificationStore.SaveCount);
            Assert.Equal(initial, state.Notifications.Current);
        }
        finally
        {
            await state.DisposeAsync();
        }
    }

    private sealed class RecordingNotificationStore :
        IGenesisChannelNotificationPreferenceStore
    {
        private readonly GenesisChannelNotificationPreferences _loaded;
        private readonly bool _saveThrows;

        public RecordingNotificationStore(
            GenesisChannelNotificationPreferences loaded,
            bool saveThrows = false)
        {
            _loaded = loaded;
            _saveThrows = saveThrows;
        }

        public GenesisChannelNotificationPreferences? Saved { get; private set; }

        public int SaveCount { get; private set; }

        public ValueTask<GenesisChannelNotificationPreferences> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_loaded);
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

    private sealed class EmptyFavoriteStore : IGenesisFavoriteChannelStore
    {
        public static EmptyFavoriteStore Instance { get; } = new();

        private EmptyFavoriteStore()
        {
        }

        public ValueTask<IReadOnlyList<GenesisFavoriteChannel>> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<GenesisFavoriteChannel>>(
                Array.Empty<GenesisFavoriteChannel>());
        }

        public ValueTask SaveAsync(
            IReadOnlyList<GenesisFavoriteChannel> favorites,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(favorites);
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
}
