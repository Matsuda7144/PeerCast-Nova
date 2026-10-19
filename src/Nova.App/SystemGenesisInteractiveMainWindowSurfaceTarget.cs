namespace PeerCast.Nova.App;

internal interface IGenesisMainWindowInteractionBridgeFactory
{
    IAsyncDisposable? Create(long windowToken, GenesisMainViewModel viewModel);
}

internal interface IGenesisChannelRefreshInteractionBridgeFactory :
    IGenesisMainWindowInteractionBridgeFactory
{
    IAsyncDisposable? Create(
        long windowToken,
        GenesisMainViewModel viewModel,
        GenesisChannelCatalogSession channelCatalogSession);
}

internal interface IGenesisChannelNotificationSettingsPlacement
{
    IAsyncDisposable? Open(
        GenesisChannelNotificationSettings settings,
        GenesisYellowPageConfiguration yellowPages,
        GenesisGuiNextPlaybackSettings playbackSettings,
        IGenesisNetworkSettingsController networkSettingsController,
        bool showButton = true);

    IAsyncDisposable? Open(
        GenesisChannelNotificationSettings settings,
        GenesisYellowPageConfiguration yellowPages,
        GenesisGuiNextPlaybackSettings playbackSettings,
        IGenesisNetworkSettingsController networkSettingsController,
        GenesisRecordingOutputDirectorySettings? recordingSettings,
        bool showButton = true) =>
        Open(settings, yellowPages, playbackSettings, networkSettingsController, showButton);
}

internal sealed class SystemGenesisInteractiveMainWindowSurfaceTarget :
    IGenesisMainWindowSurfaceTarget
{
    private readonly IGenesisMainWindowSurfaceTarget _surfaceTarget;
    private readonly IGenesisMainWindowInteractionBridgeFactory _bridgeFactory;
    private readonly IGenesisChannelCatalog _channelCatalog;
    private readonly TimeSpan _autoRefreshInterval;
    private readonly Func<long, IAsyncDisposable?> _resizeGuardFactory;
    private readonly Func<CancellationToken, GenesisGuiNextApplicationState?>
        _guiNextStateFactory;
    private readonly Func<
        long,
        GenesisGuiNextApplicationState?,
        GenesisChannelNotificationRuntime> _notificationRuntimeFactory;
    private readonly Func<long, IGenesisChannelNotificationSettingsPlacement?>
        _notificationSettingsPlacementFactory;
    private readonly IGenesisNetworkSettingsController _networkSettingsController;
    private readonly GenesisRecordingOutputDirectorySettings? _recordingSettings;

    internal SystemGenesisInteractiveMainWindowSurfaceTarget(
        IGenesisMainWindowSurfaceTarget surfaceTarget,
        IGenesisMainWindowInteractionBridgeFactory bridgeFactory)
        : this(
            surfaceTarget,
            bridgeFactory,
            EmptyGenesisChannelCatalog.Instance,
            CreateNoopResizeGuard,
            CreateNoopGuiNextState,
            CreateNoopNotificationRuntime)
    {
    }

    internal SystemGenesisInteractiveMainWindowSurfaceTarget(
        IGenesisMainWindowSurfaceTarget surfaceTarget,
        IGenesisMainWindowInteractionBridgeFactory bridgeFactory,
        IGenesisChannelCatalog channelCatalog)
        : this(
            surfaceTarget,
            bridgeFactory,
            channelCatalog,
            GenesisMainWindowResizeGuard.Create,
            LoadSystemGuiNextState,
            CreateSystemNotificationRuntime)
    {
    }

    internal SystemGenesisInteractiveMainWindowSurfaceTarget(
        IGenesisMainWindowSurfaceTarget surfaceTarget,
        IGenesisMainWindowInteractionBridgeFactory bridgeFactory,
        IGenesisChannelCatalog channelCatalog,
        TimeSpan autoRefreshInterval)
        : this(
            surfaceTarget,
            bridgeFactory,
            channelCatalog,
            autoRefreshInterval,
            GenesisMainWindowResizeGuard.Create,
            LoadSystemGuiNextState,
            CreateSystemNotificationRuntime)
    {
    }

    internal SystemGenesisInteractiveMainWindowSurfaceTarget(
        IGenesisMainWindowSurfaceTarget surfaceTarget,
        IGenesisMainWindowInteractionBridgeFactory bridgeFactory,
        IGenesisChannelCatalog channelCatalog,
        Func<long, IAsyncDisposable?> resizeGuardFactory)
        : this(
            surfaceTarget,
            bridgeFactory,
            channelCatalog,
            resizeGuardFactory,
            CreateNoopGuiNextState,
            CreateNoopNotificationRuntime)
    {
    }

    internal SystemGenesisInteractiveMainWindowSurfaceTarget(
        IGenesisMainWindowSurfaceTarget surfaceTarget,
        IGenesisMainWindowInteractionBridgeFactory bridgeFactory,
        IGenesisChannelCatalog channelCatalog,
        Func<long, IAsyncDisposable?> resizeGuardFactory,
        Func<long, GenesisChannelNotificationRuntime> notificationRuntimeFactory)
        : this(
            surfaceTarget,
            bridgeFactory,
            channelCatalog,
            resizeGuardFactory,
            CreateNoopGuiNextState,
            (windowToken, _) => notificationRuntimeFactory(windowToken))
    {
        ArgumentNullException.ThrowIfNull(notificationRuntimeFactory);
    }

    internal SystemGenesisInteractiveMainWindowSurfaceTarget(
        IGenesisMainWindowSurfaceTarget surfaceTarget,
        IGenesisMainWindowInteractionBridgeFactory bridgeFactory,
        IGenesisChannelCatalog channelCatalog,
        Func<long, IAsyncDisposable?> resizeGuardFactory,
        Func<CancellationToken, GenesisGuiNextApplicationState?> guiNextStateFactory,
        Func<
            long,
            GenesisGuiNextApplicationState?,
            GenesisChannelNotificationRuntime> notificationRuntimeFactory)
        : this(
            surfaceTarget,
            bridgeFactory,
            channelCatalog,
            GenesisChannelCatalogSession.DefaultAutoRefreshInterval,
            resizeGuardFactory,
            guiNextStateFactory,
            notificationRuntimeFactory)
    {
    }

    internal SystemGenesisInteractiveMainWindowSurfaceTarget(
        IGenesisMainWindowSurfaceTarget surfaceTarget,
        IGenesisMainWindowInteractionBridgeFactory bridgeFactory,
        IGenesisChannelCatalog channelCatalog,
        TimeSpan autoRefreshInterval,
        Func<long, IAsyncDisposable?> resizeGuardFactory,
        Func<CancellationToken, GenesisGuiNextApplicationState?> guiNextStateFactory,
        Func<
            long,
            GenesisGuiNextApplicationState?,
            GenesisChannelNotificationRuntime> notificationRuntimeFactory)
        : this(
            surfaceTarget,
            bridgeFactory,
            channelCatalog,
            autoRefreshInterval,
            resizeGuardFactory,
            guiNextStateFactory,
            notificationRuntimeFactory,
            CaptureSystemNotificationSettingsPlacement,
            GenesisUnavailableNetworkSettingsController.Instance)
    {
    }

    internal SystemGenesisInteractiveMainWindowSurfaceTarget(
        IGenesisMainWindowSurfaceTarget surfaceTarget,
        IGenesisMainWindowInteractionBridgeFactory bridgeFactory,
        IGenesisChannelCatalog channelCatalog,
        TimeSpan autoRefreshInterval,
        Func<long, IAsyncDisposable?> resizeGuardFactory,
        Func<CancellationToken, GenesisGuiNextApplicationState?> guiNextStateFactory,
        Func<
            long,
            GenesisGuiNextApplicationState?,
            GenesisChannelNotificationRuntime> notificationRuntimeFactory,
        Func<long, IGenesisChannelNotificationSettingsPlacement?>
            notificationSettingsPlacementFactory,
        IGenesisNetworkSettingsController networkSettingsController,
        GenesisRecordingOutputDirectorySettings? recordingSettings = null)
    {
        ArgumentNullException.ThrowIfNull(surfaceTarget);
        ArgumentNullException.ThrowIfNull(bridgeFactory);
        ArgumentNullException.ThrowIfNull(channelCatalog);
        if (autoRefreshInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(autoRefreshInterval),
                autoRefreshInterval,
                "Channel auto-refresh interval must be positive.");
        }

        ArgumentNullException.ThrowIfNull(resizeGuardFactory);
        ArgumentNullException.ThrowIfNull(guiNextStateFactory);
        ArgumentNullException.ThrowIfNull(notificationRuntimeFactory);
        ArgumentNullException.ThrowIfNull(notificationSettingsPlacementFactory);
        ArgumentNullException.ThrowIfNull(networkSettingsController);
        _surfaceTarget = surfaceTarget;
        _bridgeFactory = bridgeFactory is IGenesisChannelRefreshInteractionBridgeFactory
            ? bridgeFactory
            : new SystemGenesisRefreshAwareInteractionBridgeFactory(bridgeFactory);
        _channelCatalog = channelCatalog;
        _autoRefreshInterval = autoRefreshInterval;
        _resizeGuardFactory = resizeGuardFactory;
        _guiNextStateFactory = guiNextStateFactory;
        _notificationRuntimeFactory = notificationRuntimeFactory;
        _notificationSettingsPlacementFactory = notificationSettingsPlacementFactory;
        _networkSettingsController = networkSettingsController;
        _recordingSettings = recordingSettings;
    }

    public async ValueTask<IAsyncDisposable?> OpenAsync(
        long windowToken,
        GenesisMainViewModel viewModel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        var surfaceLifetime = await _surfaceTarget.OpenAsync(
            windowToken,
            viewModel,
            cancellationToken).ConfigureAwait(false);
        if (surfaceLifetime is null)
        {
            return null;
        }

        IAsyncDisposable? resizeGuard;
        try
        {
            resizeGuard = _resizeGuardFactory(windowToken);
        }
        catch
        {
            await DisposeRejectedSurfaceAsync(surfaceLifetime).ConfigureAwait(false);
            throw;
        }

        if (resizeGuard is null)
        {
            await DisposeRejectedSurfaceAsync(surfaceLifetime).ConfigureAwait(false);
            return null;
        }

        GenesisGuiNextApplicationState? guiNextState;
        try
        {
            guiNextState = _guiNextStateFactory(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await DisposeRejectedSurfaceAndResizeGuardAsync(
                    surfaceLifetime,
                    resizeGuard)
                .ConfigureAwait(false);
            throw;
        }
        catch
        {
            // User-state persistence is auxiliary. A load/path failure must not
            // reject an otherwise valid Genesis main-window surface.
            SystemGenesisGuiNextApplicationStateInitializationFeedback.Report(windowToken);
            guiNextState = null;
        }

        if (guiNextState is not null)
        {
            SystemGenesisGuiNextApplicationStateLoadFeedback.Report(
                windowToken,
                guiNextState.LoadFailures);
        }

        if (guiNextState is not null &&
            _channelCatalog is GenesisGuiNextYellowPageChannelCatalogRouter yellowPageRouter)
        {
            try
            {
                yellowPageRouter.Bind(guiNextState.YellowPages);
            }
            catch
            {
                // Shared Yellow Page binding is auxiliary. The router retains its
                // startup fallback so an otherwise valid GUI can still refresh.
            }
        }

        IGenesisChannelNotificationSettingsPlacement? notificationSettingsPlacement = null;
        if (guiNextState is not null)
        {
            try
            {
                notificationSettingsPlacement =
                    _notificationSettingsPlacementFactory(windowToken);
            }
            catch
            {
                // Settings presentation is auxiliary. Native capture failures must
                // never reject an otherwise valid Genesis main-window surface.
            }
        }

        GenesisChannelNotificationRuntime notificationRuntime;
        try
        {
            notificationRuntime = _notificationRuntimeFactory(
                    windowToken,
                    guiNextState) ??
                GenesisChannelNotificationRuntime.Empty;
        }
        catch
        {
            // Notifications are auxiliary. Native notification initialization must
            // never reject an otherwise valid Genesis main-window surface.
            notificationRuntime = GenesisChannelNotificationRuntime.Empty;
        }

        var catalogSession = GenesisChannelCatalogSession.Create(
            _channelCatalog,
            viewModel,
            notificationRuntime.Publisher,
            cancellationToken);
        IAsyncDisposable? bridgeLifetime;
        try
        {
            bridgeLifetime = _bridgeFactory is IGenesisChannelRefreshInteractionBridgeFactory
                refreshBridgeFactory
                    ? refreshBridgeFactory.Create(windowToken, viewModel, catalogSession)
                    : _bridgeFactory.Create(windowToken, viewModel);
        }
        catch
        {
            await DisposeRejectedOpenAsync(
                    catalogSession,
                    notificationRuntime.Lifetime,
                    guiNextState,
                    surfaceLifetime,
                    resizeGuard)
                .ConfigureAwait(false);
            throw;
        }

        if (bridgeLifetime is null)
        {
            await DisposeRejectedOpenAsync(
                    catalogSession,
                    notificationRuntime.Lifetime,
                    guiNextState,
                    surfaceLifetime,
                    resizeGuard)
                .ConfigureAwait(false);
            return null;
        }

        catalogSession.StartInitialRefresh();
        catalogSession.StartAutoRefresh(_autoRefreshInterval);

        IAsyncDisposable? notificationSettingsLifetime = null;
        if (guiNextState is not null && notificationSettingsPlacement is not null)
        {
            try
            {
                notificationSettingsLifetime =
                    notificationSettingsPlacement.Open(
                        guiNextState.Notifications,
                        guiNextState.YellowPages,
                        guiNextState.Playback,
                        _networkSettingsController,
                        _recordingSettings,
                        showButton: false);
            }
            catch
            {
                // Settings presentation is optional and cannot fault catalog refresh
                // or reject the established main GUI lifetime.
            }
        }

        return new CompositeLifetime(
            surfaceLifetime,
            bridgeLifetime,
            catalogSession,
            notificationSettingsLifetime,
            notificationRuntime.Lifetime,
            guiNextState,
            resizeGuard);
    }

    private static IAsyncDisposable? CreateNoopResizeGuard(long _) =>
        NoopResizeGuard.Instance;

    private static GenesisGuiNextApplicationState? CreateNoopGuiNextState(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return null;
    }

    private static GenesisGuiNextApplicationState LoadSystemGuiNextState(
        CancellationToken cancellationToken) =>
        SystemGenesisGuiNextApplicationState.LoadAsync(cancellationToken)
            .AsTask()
            .GetAwaiter()
            .GetResult();

    private static GenesisChannelNotificationRuntime CreateNoopNotificationRuntime(
        long _,
        GenesisGuiNextApplicationState? __) =>
        GenesisChannelNotificationRuntime.Empty;

    private static GenesisChannelNotificationRuntime CreateSystemNotificationRuntime(
        long windowToken,
        GenesisGuiNextApplicationState? guiNextState) =>
        guiNextState is null
            ? GenesisChannelNotificationRuntime.Empty
            : SystemGenesisGuiNextNotificationRuntime.Create(windowToken, guiNextState);

    private static IGenesisChannelNotificationSettingsPlacement?
        CaptureSystemNotificationSettingsPlacement(long windowToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var placement = GenesisChannelNotificationSettingsButtonPlacement.Capture(windowToken);
        return placement is null
            ? null
            : new SystemNotificationSettingsPlacement(placement);
    }

    private static async ValueTask DisposeRejectedSurfaceAsync(
        IAsyncDisposable surfaceLifetime)
    {
        try
        {
            await surfaceLifetime.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort GUI cleanup must not replace the original rejection.
        }
    }

    private static async ValueTask DisposeRejectedSurfaceAndResizeGuardAsync(
        IAsyncDisposable surfaceLifetime,
        IAsyncDisposable resizeGuard)
    {
        await DisposeRejectedSurfaceAsync(surfaceLifetime).ConfigureAwait(false);
        try
        {
            await resizeGuard.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Restoring optional window chrome is best-effort during rejection.
        }
    }

    private static async ValueTask DisposeRejectedOpenAsync(
        GenesisChannelCatalogSession catalogSession,
        IAsyncDisposable notificationLifetime,
        GenesisGuiNextApplicationState? guiNextState,
        IAsyncDisposable surfaceLifetime,
        IAsyncDisposable resizeGuard)
    {
        try
        {
            await catalogSession.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // A rejected open is already bounded by its original result. Cleanup
            // failures must not replace that result or a bridge creation failure.
        }

        try
        {
            await notificationLifetime.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Auxiliary notification cleanup cannot reject GUI shutdown.
        }

        if (guiNextState is not null)
        {
            try
            {
                await guiNextState.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // User-state persistence cannot reject GUI shutdown.
            }
        }

        await DisposeRejectedSurfaceAndResizeGuardAsync(
                surfaceLifetime,
                resizeGuard)
            .ConfigureAwait(false);
    }

    internal static async ValueTask DisposeCompositeAsync(
        IAsyncDisposable catalogLifetime,
        IAsyncDisposable bridgeLifetime,
        IAsyncDisposable surfaceLifetime)
    {
        ArgumentNullException.ThrowIfNull(catalogLifetime);
        ArgumentNullException.ThrowIfNull(bridgeLifetime);
        ArgumentNullException.ThrowIfNull(surfaceLifetime);

        try
        {
            await catalogLifetime.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await bridgeLifetime.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await surfaceLifetime.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async ValueTask DisposeCompositeAsync(
        IAsyncDisposable catalogLifetime,
        IAsyncDisposable? notificationSettingsLifetime,
        IAsyncDisposable notificationLifetime,
        GenesisGuiNextApplicationState? guiNextState,
        IAsyncDisposable bridgeLifetime,
        IAsyncDisposable surfaceLifetime)
    {
        try
        {
            await catalogLifetime.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            if (notificationSettingsLifetime is not null)
            {
                try
                {
                    await notificationSettingsLifetime.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Settings presentation is auxiliary and cannot block cleanup.
                }
            }

            try
            {
                await notificationLifetime.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Notifications are auxiliary and cannot block main GUI cleanup.
            }

            if (guiNextState is not null)
            {
                try
                {
                    await guiNextState.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // User-state persistence cannot block main GUI cleanup.
                }
            }

            try
            {
                await bridgeLifetime.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await surfaceLifetime.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private sealed class CompositeLifetime : IAsyncDisposable
    {
        private readonly IAsyncDisposable _surfaceLifetime;
        private readonly IAsyncDisposable _bridgeLifetime;
        private readonly GenesisChannelCatalogSession _catalogSession;
        private readonly IAsyncDisposable? _notificationSettingsLifetime;
        private readonly IAsyncDisposable _notificationLifetime;
        private readonly GenesisGuiNextApplicationState? _guiNextState;
        private readonly IAsyncDisposable _resizeGuard;
        private int _disposed;

        public CompositeLifetime(
            IAsyncDisposable surfaceLifetime,
            IAsyncDisposable bridgeLifetime,
            GenesisChannelCatalogSession catalogSession,
            IAsyncDisposable? notificationSettingsLifetime,
            IAsyncDisposable notificationLifetime,
            GenesisGuiNextApplicationState? guiNextState,
            IAsyncDisposable resizeGuard)
        {
            _surfaceLifetime = surfaceLifetime;
            _bridgeLifetime = bridgeLifetime;
            _catalogSession = catalogSession;
            _notificationSettingsLifetime = notificationSettingsLifetime;
            _notificationLifetime = notificationLifetime;
            _guiNextState = guiNextState;
            _resizeGuard = resizeGuard;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                await DisposeCompositeAsync(
                    _catalogSession,
                    _notificationSettingsLifetime,
                    _notificationLifetime,
                    _guiNextState,
                    _bridgeLifetime,
                    _surfaceLifetime).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    await _resizeGuard.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Window chrome restoration must not replace GUI cleanup failures.
                }
            }
        }
    }

    private sealed class SystemNotificationSettingsPlacement :
        IGenesisChannelNotificationSettingsPlacement
    {
        private readonly GenesisChannelNotificationSettingsButtonPlacement _placement;

        public SystemNotificationSettingsPlacement(
            GenesisChannelNotificationSettingsButtonPlacement placement)
        {
            _placement = placement;
        }

        public IAsyncDisposable? Open(
            GenesisChannelNotificationSettings settings,
            GenesisYellowPageConfiguration yellowPages,
            GenesisGuiNextPlaybackSettings playbackSettings,
            IGenesisNetworkSettingsController networkSettingsController,
            bool showButton = true) =>
            _placement.Open(
                settings,
                yellowPages,
                playbackSettings,
                networkSettingsController,
                showButton);

        public IAsyncDisposable? Open(
            GenesisChannelNotificationSettings settings,
            GenesisYellowPageConfiguration yellowPages,
            GenesisGuiNextPlaybackSettings playbackSettings,
            IGenesisNetworkSettingsController networkSettingsController,
            GenesisRecordingOutputDirectorySettings? recordingSettings,
            bool showButton = true) =>
            _placement.Open(settings, yellowPages, playbackSettings, networkSettingsController,
                showButton, recordingSettings);
    }

    private sealed class NoopResizeGuard : IAsyncDisposable
    {
        public static NoopResizeGuard Instance { get; } = new();

        private NoopResizeGuard()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
