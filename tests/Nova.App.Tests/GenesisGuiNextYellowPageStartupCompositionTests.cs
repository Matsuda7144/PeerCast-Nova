namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextYellowPageStartupCompositionTests
{
    [Fact]
    public void DefaultSpFactoryUsesSharedStateRouter()
    {
        var catalog = SpGenesisChannelCatalog.Create();

        Assert.IsType<GenesisGuiNextYellowPageChannelCatalogRouter>(catalog);
        Assert.IsAssignableFrom<IGenesisAggregatedChannelCatalog>(catalog);
    }

    [Fact]
    public async Task InteractiveSurfaceBindsApplicationOwnedYellowPagesBeforeRefresh()
    {
        var fallback = new RecordingAggregatedCatalog("fallback");
        var configured = new RecordingAggregatedCatalog("configured");
        GenesisYellowPageConfiguration? observedConfiguration = null;
        var router = new GenesisGuiNextYellowPageChannelCatalogRouter(
            fallback,
            configuration =>
            {
                observedConfiguration = configuration;
                return configured;
            });
        var state = await CreateStateAsync();
        var target = CreateTarget(router, state);

        var lifetime = await target.OpenAsync(
            7144,
            new GenesisMainViewModel(new FakePlaybackSessionController()),
            CancellationToken.None);

        Assert.NotNull(lifetime);
        await configured.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Same(state.YellowPages, observedConfiguration);
        Assert.Equal(0, fallback.ReadCount);
        Assert.Equal(1, configured.ReadCount);

        await lifetime.DisposeAsync();
    }

    [Fact]
    public async Task YellowPageBindFailureKeepsStartupFallbackUsable()
    {
        var fallback = new RecordingAggregatedCatalog("fallback");
        var router = new GenesisGuiNextYellowPageChannelCatalogRouter(
            fallback,
            _ => throw new InvalidOperationException("Configured catalog failed."));
        var state = await CreateStateAsync();
        var target = CreateTarget(router, state);

        var lifetime = await target.OpenAsync(
            7144,
            new GenesisMainViewModel(new FakePlaybackSessionController()),
            CancellationToken.None);

        Assert.NotNull(lifetime);
        await fallback.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, fallback.ReadCount);

        await lifetime.DisposeAsync();
    }

    private static SystemGenesisInteractiveMainWindowSurfaceTarget CreateTarget(
        IGenesisChannelCatalog catalog,
        GenesisGuiNextApplicationState state) =>
        new(
            new RecordingSurfaceTarget(),
            new RecordingBridgeFactory(),
            catalog,
            _ => NoopLifetime.Instance,
            cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return state;
            },
            static (_, _) => GenesisChannelNotificationRuntime.Empty);

    private static ValueTask<GenesisGuiNextApplicationState> CreateStateAsync() =>
        GenesisGuiNextApplicationState.LoadAsync(
            EmptyFavoriteStore.Instance,
            EmptyNotificationStore.Instance,
            SilentSoundPlayer.Instance);

    private sealed class RecordingAggregatedCatalog : IGenesisAggregatedChannelCatalog
    {
        private readonly GenesisAggregatedChannelCatalogSnapshot _snapshot;

        public RecordingAggregatedCatalog(string channelName)
        {
            var channel = new GenesisChannelItem(
                channelName,
                $"pcp://example.test/{channelName}",
                "[test]",
                1,
                0,
                128,
                TimeSpan.FromMinutes(1),
                "FLV");
            _snapshot = new GenesisAggregatedChannelCatalogSnapshot(
                [new GenesisAggregatedChannelCatalogEntry(channel, ["test-yp"])],
                [new GenesisYellowPageCatalogReadStatus(
                    GenesisYellowPageDefaults.Sp,
                    GenesisYellowPageCatalogReadState.Succeeded)]);
        }

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ReadCount { get; private set; }

        public async ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            var detailed = await ReadDetailedAsync(cancellationToken).ConfigureAwait(false);
            return detailed.Catalog;
        }

        public ValueTask<GenesisAggregatedChannelCatalogSnapshot> ReadDetailedAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            Started.TrySetResult();
            return ValueTask.FromResult(_snapshot);
        }
    }

    private sealed class RecordingSurfaceTarget : IGenesisMainWindowSurfaceTarget
    {
        public ValueTask<IAsyncDisposable?> OpenAsync(
            long windowToken,
            GenesisMainViewModel viewModel,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IAsyncDisposable?>(NoopLifetime.Instance);
        }
    }

    private sealed class RecordingBridgeFactory : IGenesisMainWindowInteractionBridgeFactory
    {
        public IAsyncDisposable? Create(long windowToken, GenesisMainViewModel viewModel) =>
            NoopLifetime.Instance;
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

    private sealed class EmptyNotificationStore :
        IGenesisChannelNotificationPreferenceStore
    {
        public static EmptyNotificationStore Instance { get; } = new();

        private EmptyNotificationStore()
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

    private sealed class NoopLifetime : IAsyncDisposable
    {
        public static NoopLifetime Instance { get; } = new();

        private NoopLifetime()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
