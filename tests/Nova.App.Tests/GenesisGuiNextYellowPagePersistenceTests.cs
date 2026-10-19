namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextYellowPagePersistenceTests
{
    [Fact]
    public async Task SettingsMutationPersistsBeforeApplicationShutdown()
    {
        var yellowPageStore = new RecordingYellowPageStore([]);
        var state = await GenesisGuiNextApplicationState.LoadAsync(
            EmptyFavoriteStore.Instance,
            EmptyNotificationStore.Instance,
            yellowPageStore,
            SilentSoundPlayer.Instance);
        try
        {
            var model = new GenesisYellowPageSettingsModel(state.YellowPages);

            var result = model.Add(new GenesisYellowPageSettingsDraft(
                "community",
                "Community",
                "https://yp.example/index.txt",
                null,
                IsEnabled: true,
                Namespace: null));

            Assert.True(result.Succeeded);
            Assert.Equal(1, yellowPageStore.SaveCount);
            var saved = Assert.Single(yellowPageStore.Saved);
            Assert.Equal("community", saved.Id);
            Assert.Equal(new Uri("https://yp.example/index.txt"), saved.IndexUri);
        }
        finally
        {
            await state.DisposeAsync();
        }
    }

    [Fact]
    public async Task FailedImmediateSaveLeavesLoadedConfigurationUnchanged()
    {
        var original = new GenesisYellowPageDefinition(
            "original",
            "Original",
            new Uri("https://original.example/index.txt"));
        var yellowPageStore = new RecordingYellowPageStore(
            [original],
            saveThrows: true);
        var state = await GenesisGuiNextApplicationState.LoadAsync(
            EmptyFavoriteStore.Instance,
            EmptyNotificationStore.Instance,
            yellowPageStore,
            SilentSoundPlayer.Instance);
        try
        {
            var model = new GenesisYellowPageSettingsModel(state.YellowPages);

            var result = model.Remove("original");

            Assert.Equal(
                GenesisYellowPageSettingsMutationOutcome.PersistenceFailed,
                result.Outcome);
            Assert.Equal(1, yellowPageStore.SaveCount);
            Assert.Equal(original, Assert.Single(state.YellowPages.Definitions));
        }
        finally
        {
            await state.DisposeAsync();
        }
    }

    private sealed class RecordingYellowPageStore : IGenesisYellowPageConfigurationStore
    {
        private readonly IReadOnlyList<GenesisYellowPageDefinition> _loaded;
        private readonly bool _saveThrows;

        public RecordingYellowPageStore(
            IReadOnlyList<GenesisYellowPageDefinition> loaded,
            bool saveThrows = false)
        {
            _loaded = loaded;
            _saveThrows = saveThrows;
        }

        public int SaveCount { get; private set; }

        public IReadOnlyList<GenesisYellowPageDefinition> Saved { get; private set; } =
            Array.Empty<GenesisYellowPageDefinition>();

        public ValueTask<IReadOnlyList<GenesisYellowPageDefinition>> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_loaded);
        }

        public ValueTask SaveAsync(
            IReadOnlyList<GenesisYellowPageDefinition> definitions,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(definitions);
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            if (_saveThrows)
            {
                throw new IOException("Yellow Page save failed.");
            }

            Saved = Array.AsReadOnly(definitions.ToArray());
            return ValueTask.CompletedTask;
        }
    }

    private sealed class EmptyFavoriteStore : IGenesisFavoriteChannelStore
    {
        public static EmptyFavoriteStore Instance { get; } = new();

        public ValueTask<IReadOnlyList<GenesisFavoriteChannel>> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<GenesisFavoriteChannel>>([]);
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

    private sealed class EmptyNotificationStore :
        IGenesisChannelNotificationPreferenceStore
    {
        public static EmptyNotificationStore Instance { get; } = new();

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

        public ValueTask PlayAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }
}
