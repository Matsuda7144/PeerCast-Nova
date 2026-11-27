using PeerCast.Nova.Foundation.Logging;

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
        GenesisRecordingOutputDirectorySettings? recordingSettings,
        IGuiNextNetworkSettingsAdapter? networkAdapter,
        GenesisGuiNextConnectionSettings connectionSettings,
        bool showButton = true) =>
        Open(settings, yellowPages, playbackSettings, networkSettingsController, recordingSettings, networkAdapter, showButton);

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

    IAsyncDisposable? Open(
        GenesisChannelNotificationSettings settings,
        GenesisYellowPageConfiguration yellowPages,
        GenesisGuiNextPlaybackSettings playbackSettings,
        IGenesisNetworkSettingsController networkSettingsController,
        GenesisRecordingOutputDirectorySettings? recordingSettings,
        IGuiNextNetworkSettingsAdapter? networkAdapter,
        bool showButton = true) =>
        Open(settings, yellowPages, playbackSettings, networkSettingsController, recordingSettings, showButton);
}

internal sealed class SystemGenesisInteractiveMainWindowSurfaceTarget :
    IGenesisMainWindowSurfaceTarget
{
    private readonly IGenesisMainWindowSurfaceTarget _surfaceTarget;
    private readonly IGenesisMainWindowInteractionBridgeFactory _bridgeFactory;
    private readonly IGenesisChannelCatalog _channelCatalog;
    private readonly TimeSpan _autoRefreshInterval;
    private readonly Func<long, IAsyncDisposable?> _resizeGuardFactory;
    private readonly bool _ownsGuiNextState;
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
    private readonly GenesisAppearanceSettings? _appearanceSettings;

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
        GenesisRecordingOutputDirectorySettings? recordingSettings = null, bool ownsGuiNextState = true,
        GenesisAppearanceSettings? appearanceSettings = null)
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
        _appearanceSettings = appearanceSettings;
        _ownsGuiNextState = ownsGuiNextState;
    }

    public async ValueTask<IAsyncDisposable?> OpenAsync(
        long windowToken,
        GenesisMainViewModel viewModel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        viewModel.AppearanceSettings = _appearanceSettings;
        var surfaceLifetime = await _surfaceTarget.OpenAsync(
            windowToken,
            viewModel,
            cancellationToken).ConfigureAwait(false);
        if (surfaceLifetime is null)
        {
            return null;
        }

        SystemGenesisGuiNextSettingsWindowFeedback.ReportAppearanceLoadFailure((nint)windowToken, _appearanceSettings?.Error);
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
        catch (Exception exception)
        {
            // User-state persistence is auxiliary. A load/path failure must not
            // reject an otherwise valid Genesis main-window surface.
            FileNovaLogger.Default.Log(LogLevel.Warning, "Failed to initialize GUI Next application state.", exception);
            SystemGenesisGuiNextApplicationStateInitializationFeedback.Report(windowToken);
            guiNextState = null;
        }

        viewModel.PublicChannels = guiNextState?.PublicChannels;
        if (guiNextState is not null && viewModel.NetworkSettingsAdapter is GuiNextNetworkSettingsAdapter networkAdapter)
            networkAdapter.ConfigurePublicRelay(guiNextState.PublicChannels, guiNextState.Connection.Active);
        viewModel.RecordingOutputSettings = _recordingSettings;
        viewModel.PlaybackSettings = guiNextState?.Playback;
        viewModel.NotificationSettings = guiNextState?.Notifications;
        viewModel.ConnectionSettings = guiNextState?.Connection;
        viewModel.YellowPageSettings = guiNextState is null ? null : new(guiNextState.YellowPages);
        viewModel.RelayActivation = guiNextState?.RelayActivation;
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
            catch (Exception exception)
            {
                // Shared Yellow Page binding is auxiliary. The router retains its
                // startup fallback so an otherwise valid GUI can still refresh.
                FileNovaLogger.Default.Log(LogLevel.Warning, "Failed to bind Yellow Page router to GUI Next state.", exception);
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
            catch (Exception exception)
            {
                // Settings presentation is auxiliary. Native capture failures must
                // never reject an otherwise valid Genesis main-window surface.
                FileNovaLogger.Default.Log(LogLevel.Warning, "Failed to capture notification settings placement.", exception);
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
        catch (Exception exception)
        {
            // Notifications are auxiliary. Native notification initialization must
            // never reject an otherwise valid Genesis main-window surface.
            FileNovaLogger.Default.Log(LogLevel.Warning, "Failed to initialize notification runtime.", exception);
            notificationRuntime = GenesisChannelNotificationRuntime.Empty;
        }

        var yellowPageDiagnosticTrace =
            GenesisYellowPageRefreshDiagnosticTrace.FromEnvironment();
        GenesisChannelCatalogSession catalogSession;
        try
        {
            catalogSession = GenesisChannelCatalogSession.Create(
                _channelCatalog,
                viewModel,
                notificationRuntime.Publisher,
                yellowPageDiagnosticTrace,
                cancellationToken);
        }
        catch
        {
            if (yellowPageDiagnosticTrace is not null)
            {
                await yellowPageDiagnosticTrace.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
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
            viewModel.PublicChannels = null;
            viewModel.RelayActivation = null;
            await DisposeRejectedOpenAsync(
                    catalogSession,
                    notificationRuntime.Lifetime,
                    _ownsGuiNextState ? guiNextState : null,
                    surfaceLifetime,
                    resizeGuard)
                .ConfigureAwait(false);
            throw;
        }

        if (bridgeLifetime is null)
        {
            viewModel.PublicChannels = null;
            viewModel.RelayActivation = null;
            await DisposeRejectedOpenAsync(
                    catalogSession,
                    notificationRuntime.Lifetime,
                    _ownsGuiNextState ? guiNextState : null,
                    surfaceLifetime,
                    resizeGuard)
                .ConfigureAwait(false);
            return null;
        }

        // Modern UI・テーマ適用・初期配置がすべて揃った段階で初めてメインウィンドウを表示する。
        GenesisMainWindowResizeGuard.RevealWindow((nint)windowToken);

        // The application network owner is initialized before opening this surface.
        // Start only after the bridge is accepted, so rejected windows never publish.
        if (guiNextState is not null && viewModel.NetworkSettingsAdapter is GuiNextNetworkSettingsAdapter)
            await guiNextState.PublicChannels.StartPublicAsync().ConfigureAwait(false);

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
                        viewModel.NetworkSettingsAdapter,
                        guiNextState.Connection,
                        showButton: false);
            }
            catch (Exception exception)
            {
                // Settings presentation is optional and cannot fault catalog refresh
                // or reject the established main GUI lifetime.
                FileNovaLogger.Default.Log(LogLevel.Warning, "Failed to open notification settings placement.", exception);
            }
        }

        return new CompositeLifetime(
            surfaceLifetime,
            bridgeLifetime,
            catalogSession,
            notificationSettingsLifetime,
            notificationRuntime.Lifetime,
            guiNextState,
            resizeGuard,
            viewModel, _ownsGuiNextState);
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
        catch (Exception exception)
        {
            // Best-effort GUI cleanup must not replace the original rejection.
            FileNovaLogger.Default.Log(LogLevel.Warning, "Failed to dispose rejected surface lifetime.", exception);
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
        catch (Exception exception)
        {
            // Restoring optional window chrome is best-effort during rejection.
            FileNovaLogger.Default.Log(LogLevel.Warning, "Failed to dispose rejected resize guard.", exception);
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
        catch (Exception exception)
        {
            // A rejected open is already bounded by its original result. Cleanup
            // failures must not replace that result or a bridge creation failure.
            FileNovaLogger.Default.Log(LogLevel.Warning, "Failed to dispose rejected catalog session.", exception);
        }

        try
        {
            await notificationLifetime.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Auxiliary notification cleanup cannot reject GUI shutdown.
            FileNovaLogger.Default.Log(LogLevel.Warning, "Failed to dispose rejected notification lifetime.", exception);
        }

        if (guiNextState is not null)
        {
            try
            {
                await guiNextState.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // User-state persistence cannot reject GUI shutdown.
                FileNovaLogger.Default.Log(LogLevel.Warning, "Failed to dispose rejected GUI Next state.", exception);
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
                catch (Exception exception)
                {
                    // Settings presentation is auxiliary and cannot block cleanup.
                    FileNovaLogger.Default.Log(LogLevel.Warning, "Failed to dispose notification settings lifetime.", exception);
                }
            }

            try
            {
                await notificationLifetime.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // Notifications are auxiliary and cannot block main GUI cleanup.
                FileNovaLogger.Default.Log(LogLevel.Warning, "Failed to dispose notification runtime lifetime.", exception);
            }

            if (guiNextState is not null)
            {
                try
                {
                    await guiNextState.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    // User-state persistence cannot block main GUI cleanup.
                    FileNovaLogger.Default.Log(LogLevel.Warning, "Failed to dispose GUI Next state.", exception);
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
        private readonly bool _ownsGuiNextState;
        private readonly IAsyncDisposable _resizeGuard;
        private readonly GenesisMainViewModel _viewModel;
        private int _disposed;

        public CompositeLifetime(
            IAsyncDisposable surfaceLifetime,
            IAsyncDisposable bridgeLifetime,
            GenesisChannelCatalogSession catalogSession,
            IAsyncDisposable? notificationSettingsLifetime,
            IAsyncDisposable notificationLifetime,
            GenesisGuiNextApplicationState? guiNextState,
            IAsyncDisposable resizeGuard,
            GenesisMainViewModel viewModel, bool ownsGuiNextState)
        {
            _surfaceLifetime = surfaceLifetime;
            _bridgeLifetime = bridgeLifetime;
            _catalogSession = catalogSession;
            _notificationSettingsLifetime = notificationSettingsLifetime;
            _notificationLifetime = notificationLifetime;
            _guiNextState = guiNextState;
            _ownsGuiNextState = ownsGuiNextState;
            _resizeGuard = resizeGuard;
            _viewModel = viewModel;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (ReferenceEquals(_viewModel.PublicChannels, _guiNextState?.PublicChannels))
            {
                if (_guiNextState is not null) await _guiNextState.RelayActivation.DisposeAsync().ConfigureAwait(false);
                _viewModel.PublicChannels = null;
                _viewModel.RelayActivation = null;
            }
            try
            {
                await DisposeCompositeAsync(
                    _catalogSession,
                    _notificationSettingsLifetime,
                    _notificationLifetime,
                    _ownsGuiNextState ? _guiNextState : null,
                    _bridgeLifetime,
                    _surfaceLifetime).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    await _resizeGuard.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    // Window chrome restoration must not replace GUI cleanup failures.
                    FileNovaLogger.Default.Log(LogLevel.Warning, "Failed to dispose main window resize guard.", exception);
                }
            }
        }
    }

    private sealed class SystemNotificationSettingsPlacement :
        IGenesisChannelNotificationSettingsPlacement
    {
        public IAsyncDisposable? Open(GenesisChannelNotificationSettings settings,
            GenesisYellowPageConfiguration yellowPages, GenesisGuiNextPlaybackSettings playbackSettings,
            IGenesisNetworkSettingsController networkSettingsController,
            GenesisRecordingOutputDirectorySettings? recordingSettings, IGuiNextNetworkSettingsAdapter? networkAdapter,
            GenesisGuiNextConnectionSettings connectionSettings, bool showButton = true) =>
            _placement.Open(settings, yellowPages, playbackSettings, networkSettingsController,
                showButton, recordingSettings, networkAdapter, connectionSettings);

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

        public IAsyncDisposable? Open(
            GenesisChannelNotificationSettings settings,
            GenesisYellowPageConfiguration yellowPages,
            GenesisGuiNextPlaybackSettings playbackSettings,
            IGenesisNetworkSettingsController networkSettingsController,
            GenesisRecordingOutputDirectorySettings? recordingSettings,
            IGuiNextNetworkSettingsAdapter? networkAdapter,
            bool showButton = true) =>
            _placement.Open(settings, yellowPages, playbackSettings, networkSettingsController,
                showButton, recordingSettings, networkAdapter);
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
