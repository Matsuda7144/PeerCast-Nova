namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextFavoriteYellowPageRestartTests
{
    private const string ChannelId = "0123456789ABCDEF0123456789ABCDEF";
    private static readonly string[] KnownYellowPageIds = ["sp", "other"];

    [Fact]
    public async Task RefreshedYellowPageSurvivesRestartAsOfflineFavorite()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "favorites.json");
        var favoriteStore = new JsonGenesisFavoriteChannelStore(path);
        var saved = Channel(
            "Saved",
            "pcp://old.example/channel") with
        {
            YellowPage = "Legacy",
        };
        await favoriteStore.SaveAsync([
            GenesisFavoriteChannel.FromChannel(saved),
        ]);
        var updated = Channel(
            "Updated",
            "pcp://new.example/channel") with
        {
            YellowPage = "SP, Other",
        };

        var firstState = await LoadAsync(favoriteStore);
        try
        {
            ApplyPresence(firstState, updated);

            var inMemory = Assert.Single(firstState.Favorites.Snapshot);
            Assert.Equal("SP, Other", inMemory.LastKnownYellowPage);
        }
        finally
        {
            await firstState.DisposeAsync();
        }

        var secondState = await LoadAsync(favoriteStore);
        try
        {
            var reloaded = Assert.Single(secondState.Favorites.Snapshot);
            Assert.Equal("Updated", reloaded.Name);
            Assert.Equal(updated.Source, reloaded.LastKnownSource);
            Assert.Equal("SP, Other", reloaded.LastKnownYellowPage);

            secondState.Tabs.SelectedTab = GenesisGuiNextTab.Favorites;
            var row = Assert.Single(
                GenesisGuiNextNativeTabProjection.Create(secondState.Tabs));
            Assert.Equal("Updated", row.DisplayChannel.Name);
            Assert.Equal("SP, Other", row.DisplayChannel.YellowPageText);
            Assert.False(row.IsInteractive);

            secondState.Tabs.SearchText = "other";
            Assert.Single(
                GenesisGuiNextNativeTabProjection.Create(secondState.Tabs));
        }
        finally
        {
            await secondState.DisposeAsync();
        }
    }

    private static void ApplyPresence(
        GenesisGuiNextApplicationState state,
        GenesisChannelItem channel)
    {
        var entry = new GenesisChannelPresenceEntry(
            GenesisChannelIdentity.GetStableKey(channel),
            channel,
            GenesisChannelPresenceState.Active,
            true,
            false,
            0,
            KnownYellowPageIds);
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

    private static GenesisChannelItem Channel(string name, string source) =>
        new(
            name,
            source,
            Description: "metadata",
            Type: "FLV",
            ChannelId: ChannelId);

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
                $"PeerCast-Nova-Favorite-Yp-Restart-{Guid.NewGuid():N}");
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
