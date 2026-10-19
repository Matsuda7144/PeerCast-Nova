namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextFavoritePersistenceTests
{
    private const string ChannelId = "0123456789ABCDEF0123456789ABCDEF";

    [Fact]
    public async Task AddFavoritePersistsBeforeShutdown()
    {
        var favoriteStore = new RecordingFavoriteStore([]);
        var state = await LoadAsync(favoriteStore);
        var channel = Channel("Favorite");

        try
        {
            Assert.Equal(
                GenesisFavoriteChannelMutationResult.Added,
                state.Tabs.AddFavorite(channel));

            Assert.Equal(1, favoriteStore.SaveCount);
            Assert.Equal(
                GenesisFavoriteChannel.FromChannel(channel),
                Assert.Single(favoriteStore.Saved));
            Assert.True(state.Tabs.IsFavorite(channel));
        }
        finally
        {
            await state.DisposeAsync();
        }
    }

    [Fact]
    public async Task RemoveFavoritePersistsBeforeShutdown()
    {
        var channel = Channel("Favorite");
        var favoriteStore = new RecordingFavoriteStore(
            [GenesisFavoriteChannel.FromChannel(channel)]);
        var state = await LoadAsync(favoriteStore);

        try
        {
            Assert.True(state.Tabs.RemoveFavorite(channel));

            Assert.Equal(1, favoriteStore.SaveCount);
            Assert.Empty(favoriteStore.Saved);
            Assert.False(state.Tabs.IsFavorite(channel));
        }
        finally
        {
            await state.DisposeAsync();
        }
    }

    [Fact]
    public async Task AddFavoriteSaveFailureDoesNotPublishMutation()
    {
        var favoriteStore = new RecordingFavoriteStore([], saveThrows: true);
        var state = await LoadAsync(favoriteStore);
        var channel = Channel("Favorite");

        try
        {
            Assert.Equal(
                GenesisFavoriteChannelMutationResult.Unchanged,
                state.Tabs.AddFavorite(channel));

            Assert.Equal(1, favoriteStore.SaveCount);
            Assert.Empty(state.Favorites.Snapshot);
            Assert.False(state.Tabs.IsFavorite(channel));
        }
        finally
        {
            await state.DisposeAsync();
        }
    }

    [Fact]
    public async Task RemoveFavoriteSaveFailureDoesNotPublishMutation()
    {
        var channel = Channel("Favorite");
        var favorite = GenesisFavoriteChannel.FromChannel(channel);
        var favoriteStore = new RecordingFavoriteStore([favorite], saveThrows: true);
        var state = await LoadAsync(favoriteStore);

        try
        {
            Assert.False(state.Tabs.RemoveFavorite(channel));

            Assert.Equal(1, favoriteStore.SaveCount);
            Assert.Equal(favorite, Assert.Single(state.Favorites.Snapshot));
            Assert.True(state.Tabs.IsFavorite(channel));
        }
        finally
        {
            await state.DisposeAsync();
        }
    }

    [Fact]
    public async Task UnchangedFavoriteDoesNotRewriteStore()
    {
        var channel = Channel("Favorite");
        var favoriteStore = new RecordingFavoriteStore(
            [GenesisFavoriteChannel.FromChannel(channel)]);
        var state = await LoadAsync(favoriteStore);

        try
        {
            Assert.Equal(
                GenesisFavoriteChannelMutationResult.Unchanged,
                state.Tabs.AddFavorite(channel));

            Assert.Equal(0, favoriteStore.SaveCount);
            Assert.True(state.Tabs.IsFavorite(channel));
        }
        finally
        {
            await state.DisposeAsync();
        }
    }

    private static ValueTask<GenesisGuiNextApplicationState> LoadAsync(
        IGenesisFavoriteChannelStore favoriteStore) =>
        GenesisGuiNextApplicationState.LoadAsync(
            favoriteStore,
            StaticNotificationStore.Instance,
            SilentSoundPlayer.Instance);

    private static GenesisChannelItem Channel(string name) =>
        new(
            name,
            $"pcp://example/channel/{ChannelId}",
            Type: "FLV",
            ChannelId: ChannelId);

    private sealed class RecordingFavoriteStore : IGenesisFavoriteChannelStore
    {
        private readonly IReadOnlyList<GenesisFavoriteChannel> _loaded;
        private readonly bool _saveThrows;

        public RecordingFavoriteStore(
            IReadOnlyList<GenesisFavoriteChannel> loaded,
            bool saveThrows = false)
        {
            _loaded = Array.AsReadOnly(loaded.ToArray());
            _saveThrows = saveThrows;
            Saved = _loaded;
        }

        public IReadOnlyList<GenesisFavoriteChannel> Saved { get; private set; }

        public int SaveCount { get; private set; }

        public ValueTask<IReadOnlyList<GenesisFavoriteChannel>> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
}
