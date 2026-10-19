namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextYellowPageApplicationStateTests
{
    [Fact]
    public async Task LoadSharesPersistedYellowPageConfiguration()
    {
        var custom = CustomDefinition();
        var yellowPageStore = new RecordingYellowPageStore([custom]);

        await using var state = await GenesisGuiNextApplicationState.LoadAsync(
            NoopFavoriteStore.Instance,
            NoopNotificationStore.Instance,
            yellowPageStore,
            NoopSoundPlayer.Instance);

        Assert.Equal([custom], state.YellowPages.Definitions);
    }

    [Fact]
    public async Task YellowPageLoadFailureFallsBackToDefaultConfiguration()
    {
        var yellowPageStore = new RecordingYellowPageStore([], loadThrows: true);

        await using var state = await GenesisGuiNextApplicationState.LoadAsync(
            NoopFavoriteStore.Instance,
            NoopNotificationStore.Instance,
            yellowPageStore,
            NoopSoundPlayer.Instance);

        Assert.Equal(
            [GenesisYellowPageDefaults.Sp],
            state.YellowPages.Definitions);
    }

    [Fact]
    public async Task DisposePersistsYellowPageMutations()
    {
        var yellowPageStore = new RecordingYellowPageStore(
            [GenesisYellowPageDefaults.Sp]);
        var state = await GenesisGuiNextApplicationState.LoadAsync(
            NoopFavoriteStore.Instance,
            NoopNotificationStore.Instance,
            yellowPageStore,
            NoopSoundPlayer.Instance);
        var custom = CustomDefinition();

        Assert.True(state.YellowPages.SetEnabled("sp", false));
        Assert.True(state.YellowPages.Add(custom));

        await state.DisposeAsync();

        Assert.Equal(
            [
                GenesisYellowPageDefaults.Sp with { IsEnabled = false },
                custom,
            ],
            yellowPageStore.Saved);
    }

    [Fact]
    public async Task YellowPageSaveFailureDoesNotEscapeShutdown()
    {
        var yellowPageStore = new RecordingYellowPageStore(
            [GenesisYellowPageDefaults.Sp],
            saveThrows: true);
        var state = await GenesisGuiNextApplicationState.LoadAsync(
            NoopFavoriteStore.Instance,
            NoopNotificationStore.Instance,
            yellowPageStore,
            NoopSoundPlayer.Instance);

        await state.DisposeAsync();

        Assert.Equal(1, yellowPageStore.SaveCount);
    }

    private static GenesisYellowPageDefinition CustomDefinition() =>
        new(
            "custom",
            "Custom",
            new Uri("https://example.test/index.txt"),
            new Uri("pcp://example.test:7144/"),
            isEnabled: false,
            yellowPageNamespace: "custom");

    private sealed class RecordingYellowPageStore :
        IGenesisYellowPageConfigurationStore
    {
        private readonly IReadOnlyList<GenesisYellowPageDefinition> _loaded;
        private readonly bool _loadThrows;
        private readonly bool _saveThrows;

        public RecordingYellowPageStore(
            IReadOnlyList<GenesisYellowPageDefinition> loaded,
            bool loadThrows = false,
            bool saveThrows = false)
        {
            _loaded = loaded;
            _loadThrows = loadThrows;
            _saveThrows = saveThrows;
        }

        public IReadOnlyList<GenesisYellowPageDefinition> Saved { get; private set; } =
            Array.Empty<GenesisYellowPageDefinition>();

        public int SaveCount { get; private set; }

        public ValueTask<IReadOnlyList<GenesisYellowPageDefinition>> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_loadThrows)
            {
                throw new InvalidDataException("Yellow Page load failed.");
            }

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

    private sealed class NoopFavoriteStore : IGenesisFavoriteChannelStore
    {
        public static NoopFavoriteStore Instance { get; } = new();

        private NoopFavoriteStore()
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

    private sealed class NoopNotificationStore :
        IGenesisChannelNotificationPreferenceStore
    {
        public static NoopNotificationStore Instance { get; } = new();

        private NoopNotificationStore()
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

    private sealed class NoopSoundPlayer : IGenesisSystemNotificationSoundPlayer
    {
        public static NoopSoundPlayer Instance { get; } = new();

        private NoopSoundPlayer()
        {
        }

        public ValueTask PlayAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }
}
