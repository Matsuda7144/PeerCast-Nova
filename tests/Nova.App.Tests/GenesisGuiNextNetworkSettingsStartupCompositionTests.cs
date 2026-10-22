namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextNetworkSettingsStartupCompositionTests
{
    [Fact]
    public async Task InteractiveSurfaceForwardsInjectedNetworkSettingsController()
    {
        var state = await CreateStateAsync();
        var placement = new RecordingSettingsPlacement();
        var controller = new RecordingNetworkSettingsController();
        var target = new SystemGenesisInteractiveMainWindowSurfaceTarget(
            new RecordingSurfaceTarget(),
            new RecordingBridgeFactory(),
            EmptyGenesisChannelCatalog.Instance,
            GenesisChannelCatalogSession.DefaultAutoRefreshInterval,
            _ => NoopLifetime.Instance,
            cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return state;
            },
            static (_, _) => GenesisChannelNotificationRuntime.Empty,
            _ => placement,
            controller);

        var lifetime = await target.OpenAsync(
            7144,
            new GenesisMainViewModel(new FakePlaybackSessionController()),
            CancellationToken.None);

        Assert.NotNull(lifetime);
        Assert.Same(state.Notifications, placement.Settings);
        Assert.Same(state.YellowPages, placement.YellowPages);
        Assert.Same(state.Playback, placement.Playback);
        Assert.Same(state.Connection, placement.Connection);
        Assert.Same(controller, placement.NetworkSettingsController);
        Assert.False(placement.ShowButton);
        Assert.False(placement.IsDisposed);

        await lifetime.DisposeAsync();

        Assert.True(placement.IsDisposed);
    }

    private static ValueTask<GenesisGuiNextApplicationState> CreateStateAsync() =>
        GenesisGuiNextApplicationState.LoadAsync(
            EmptyFavoriteStore.Instance,
            EmptyNotificationStore.Instance,
            SilentSoundPlayer.Instance);

    private sealed class RecordingSettingsPlacement :
        IGenesisChannelNotificationSettingsPlacement
    {
        public GenesisChannelNotificationSettings? Settings { get; private set; }

        public GenesisYellowPageConfiguration? YellowPages { get; private set; }

        public GenesisGuiNextPlaybackSettings? Playback { get; private set; }
        public GenesisGuiNextConnectionSettings? Connection { get; private set; }

        public IAsyncDisposable? Open(GenesisChannelNotificationSettings settings,
            GenesisYellowPageConfiguration yellowPages, GenesisGuiNextPlaybackSettings playbackSettings,
            IGenesisNetworkSettingsController networkSettingsController,
            GenesisRecordingOutputDirectorySettings? recordingSettings, IGuiNextNetworkSettingsAdapter? networkAdapter,
            GenesisGuiNextConnectionSettings connectionSettings, bool showButton = true)
        {
            Connection = connectionSettings;
            return Open(settings, yellowPages, playbackSettings, networkSettingsController, showButton);
        }

        public IGenesisNetworkSettingsController? NetworkSettingsController
        {
            get;
            private set;
        }

        public bool ShowButton { get; private set; }

        public bool IsDisposed { get; private set; }

        public IAsyncDisposable? Open(
            GenesisChannelNotificationSettings settings,
            GenesisYellowPageConfiguration yellowPages,
            GenesisGuiNextPlaybackSettings playbackSettings,
            IGenesisNetworkSettingsController networkSettingsController,
            bool showButton = true)
        {
            Settings = settings;
            YellowPages = yellowPages;
            Playback = playbackSettings;
            NetworkSettingsController = networkSettingsController;
            ShowButton = showButton;
            return new CallbackLifetime(() => IsDisposed = true);
        }
    }

    private sealed class RecordingNetworkSettingsController :
        IGenesisNetworkSettingsController
    {
        private static readonly GenesisNetworkSettingsAppliedSnapshot AppliedSnapshot = new(
            new GenesisNetworkBasicSettings(
                port: 7144,
                ipv6Enabled: false,
                automaticPortMappingEnabled: false,
                allowLocalRelay: true,
                allowGlobalRelay: false),
            new GenesisRelayNetworkStatusSnapshot(
                GenesisRelayNetworkCapabilities.None,
                GenesisRelayListenerRuntimeState.Unavailable,
                activeRelayConnections: 0));

        public GenesisNetworkSettingsAppliedSnapshot Snapshot => AppliedSnapshot;

        public ValueTask<GenesisNetworkSettingsApplyOutcome> ApplyAsync(
            GenesisNetworkBasicSettings settings,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(settings);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(GenesisNetworkSettingsApplyOutcome.Rejected);
        }
    }

    private sealed class RecordingSurfaceTarget : IGenesisMainWindowSurfaceTarget
    {
        public ValueTask<IAsyncDisposable?> OpenAsync(
            long windowToken,
            GenesisMainViewModel viewModel,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(viewModel);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IAsyncDisposable?>(NoopLifetime.Instance);
        }
    }

    private sealed class RecordingBridgeFactory : IGenesisMainWindowInteractionBridgeFactory
    {
        public IAsyncDisposable? Create(long windowToken, GenesisMainViewModel viewModel)
        {
            ArgumentNullException.ThrowIfNull(viewModel);
            return NoopLifetime.Instance;
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

    private sealed class CallbackLifetime : IAsyncDisposable
    {
        private readonly Action _dispose;
        private int _disposed;

        public CallbackLifetime(Action dispose)
        {
            _dispose = dispose;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _dispose();
            }

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
