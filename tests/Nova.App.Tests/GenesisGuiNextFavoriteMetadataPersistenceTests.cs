namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextFavoriteMetadataPersistenceTests
{
    private const string FirstChannelId = "0123456789ABCDEF0123456789ABCDEF";
    private const string SecondChannelId = "FEDCBA9876543210FEDCBA9876543210";

    [Fact]
    public async Task RefreshUpdatesLastKnownMetadataWithoutImmediateStoreWrite()
    {
        var saved = Channel(
            "Saved",
            FirstChannelId,
            "pcp://old.example/first");
        var updated = Channel(
            "Updated",
            FirstChannelId,
            "pcp://new.example/first",
            "new metadata");
        var favoriteStore = new RecordingFavoriteStore(
            [GenesisFavoriteChannel.FromChannel(saved)]);
        var state = await LoadAsync(favoriteStore);

        try
        {
            ApplyPresence(state, updated, isCurrentlyObserved: true);

            Assert.Equal(0, favoriteStore.SaveCount);
            var favorite = Assert.Single(state.Favorites.Snapshot);
            Assert.Equal("Updated", favorite.Name);
            Assert.Equal(updated.Source, favorite.LastKnownSource);
            Assert.Equal("new metadata", favorite.Description);
        }
        finally
        {
            await state.DisposeAsync();
        }

        Assert.Equal(1, favoriteStore.SaveCount);
        var persisted = Assert.Single(favoriteStore.Saved);
        Assert.Equal("Updated", persisted.Name);
        Assert.Equal(updated.Source, persisted.LastKnownSource);
        Assert.Equal("new metadata", persisted.Description);
    }

    [Fact]
    public async Task TransientAbsenceDoesNotRegressLastKnownMetadata()
    {
        var saved = Channel(
            "Saved",
            FirstChannelId,
            "pcp://saved.example/first");
        var updated = Channel(
            "Updated",
            FirstChannelId,
            "pcp://updated.example/first",
            "new metadata");
        var staleAbsent = Channel(
            "Stale",
            FirstChannelId,
            "pcp://stale.example/first",
            "stale metadata");
        var favoriteStore = new RecordingFavoriteStore(
            [GenesisFavoriteChannel.FromChannel(saved)]);
        var state = await LoadAsync(favoriteStore);

        try
        {
            ApplyPresence(state, updated, isCurrentlyObserved: true);
            ApplyPresence(state, staleAbsent, isCurrentlyObserved: false);

            Assert.Equal(0, favoriteStore.SaveCount);
            var favorite = Assert.Single(state.Favorites.Snapshot);
            Assert.Equal("Updated", favorite.Name);
            Assert.Equal(updated.Source, favorite.LastKnownSource);
            Assert.Equal("new metadata", favorite.Description);
        }
        finally
        {
            await state.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExplicitMutationFlushesRefreshedMetadata()
    {
        var firstSaved = Channel(
            "First saved",
            FirstChannelId,
            "pcp://old.example/first");
        var firstUpdated = Channel(
            "First updated",
            FirstChannelId,
            "pcp://new.example/first",
            "updated metadata");
        var second = Channel(
            "Second",
            SecondChannelId,
            "pcp://example/second");
        var favoriteStore = new RecordingFavoriteStore(
            [
                GenesisFavoriteChannel.FromChannel(firstSaved),
                GenesisFavoriteChannel.FromChannel(second),
            ]);
        var state = await LoadAsync(favoriteStore);

        try
        {
            ApplyPresence(state, firstUpdated, isCurrentlyObserved: true);
            Assert.Equal(0, favoriteStore.SaveCount);

            Assert.True(state.Tabs.RemoveFavorite(second));

            Assert.Equal(1, favoriteStore.SaveCount);
            var persisted = Assert.Single(favoriteStore.Saved);
            Assert.Equal("First updated", persisted.Name);
            Assert.Equal(firstUpdated.Source, persisted.LastKnownSource);
            Assert.Equal("updated metadata", persisted.Description);
        }
        finally
        {
            await state.DisposeAsync();
        }
    }

    private static void ApplyPresence(
        GenesisGuiNextApplicationState state,
        GenesisChannelItem channel,
        bool isCurrentlyObserved)
    {
        var entry = new GenesisChannelPresenceEntry(
            GenesisChannelIdentity.GetStableKey(channel),
            channel,
            GenesisChannelPresenceState.Active,
            isCurrentlyObserved,
            false,
            isCurrentlyObserved ? 0 : 1,
            Array.Empty<string>());
        var presence = new GenesisChannelPresenceSnapshot(
            [entry],
            Array.Empty<GenesisChannelPresenceTransition>());
        state.Tabs.ApplyChannelViews(
            GenesisChannelViewProjectionSnapshot.FromPresence(presence),
            presence);
    }

    private static ValueTask<GenesisGuiNextApplicationState> LoadAsync(
        IGenesisFavoriteChannelStore favoriteStore) =>
        GenesisGuiNextApplicationState.LoadAsync(
            favoriteStore,
            StaticNotificationStore.Instance,
            SilentSoundPlayer.Instance);

    private static GenesisChannelItem Channel(
        string name,
        string channelId,
        string source,
        string? description = null) =>
        new(
            name,
            source,
            Description: description,
            Type: "FLV",
            ChannelId: channelId);

    private sealed class RecordingFavoriteStore : IGenesisFavoriteChannelStore
    {
        private readonly IReadOnlyList<GenesisFavoriteChannel> _loaded;

        public RecordingFavoriteStore(IReadOnlyList<GenesisFavoriteChannel> loaded)
        {
            _loaded = Array.AsReadOnly(loaded.ToArray());
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
