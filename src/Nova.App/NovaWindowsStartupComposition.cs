using PeerCast.Nova.Foundation.Logging;
using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App;

public enum NovaWindowsStartupResult
{
    None,
    Completed,
    PlatformUnsupported,
    StartupRejected,
    Cancelled,
    Faulted,
}

public sealed record NovaWindowsStartupOptions
{
    public PlaybackWindowsPhysicalRunnerOptions Host { get; init; } = new()
    {
        ClientWidth = 1_120,
        ClientHeight = 620,
        Title = "PeerCast Nova",
    };

    public IGenesisChannelCatalog ChannelCatalog { get; init; } =
        SpGenesisChannelCatalog.Create();

    public TimeSpan ChannelAutoRefreshInterval { get; init; } =
        GenesisChannelCatalogSession.DefaultAutoRefreshInterval;

    internal IGenesisNetworkSettingsController NetworkSettingsController { get; init; } =
        GenesisUnavailableNetworkSettingsController.Instance;
    internal NovaValidationProfile? ValidationProfile { get; init; }
    internal string? PlayerExecutablePath { get; init; }
    internal string? LibMpvLibraryPath { get; init; }
}

/// <summary>
/// User-facing application startup boundary. It creates exactly one shared
/// Nova.Windows host, waits for close or cancellation, then stops and disposes
/// that host. GUI playback stays behind the application-facing session
/// controller, whose production adapter starts its PCP/HTTP-FLV path only on
/// an explicit Play command.
/// </summary>
public sealed class NovaWindowsStartupComposition
{
    private readonly NovaWindowsStartupOptions _options;
    private readonly INovaWindowsStartupHostFactory _hostFactory;
    private readonly Func<INovaApplicationNetworkOwner> _networkOwnerFactory;
    private readonly INovaObsPublishingRuntimeFactory _publishingRuntimeFactory;
    private readonly IPublishingPreferencesStore? _publishingPreferencesStore;
    private readonly Func<
        GenesisGuiNextPlaybackPreferencesSource,
        GenesisRecordingOutputDirectorySettings,
        GenesisGuiNextPublicChannelComposition?,
        IPlaybackSessionController> _genesisPlaybackControllerFactory;

    public NovaWindowsStartupComposition(NovaWindowsStartupOptions? options = null)
        : this(
            options ?? new(),
            SystemNovaWindowsStartupHostFactory.Instance,
            playbackControllerFactory: null)
    {
    }

    internal NovaWindowsStartupComposition(
        NovaWindowsStartupOptions options,
        INovaWindowsStartupHostFactory hostFactory,
        Func<IPlaybackSessionController>? playbackControllerFactory = null,
        Func<INovaApplicationNetworkOwner>? networkOwnerFactory = null,
        INovaObsPublishingRuntimeFactory? publishingRuntimeFactory = null,
        IPublishingPreferencesStore? publishingPreferencesStore = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(hostFactory);
        ArgumentNullException.ThrowIfNull(options.Host);
        ArgumentNullException.ThrowIfNull(options.ChannelCatalog);
        ArgumentNullException.ThrowIfNull(options.NetworkSettingsController);
        if (options.ChannelAutoRefreshInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.ChannelAutoRefreshInterval,
                "Channel auto-refresh interval must be positive.");
        }

        _options = options with { Host = options.Host with { } };
        _hostFactory = hostFactory;
        _publishingRuntimeFactory = publishingRuntimeFactory ?? SystemNovaObsPublishingRuntimeFactory.Instance;
        _publishingPreferencesStore = publishingPreferencesStore ??
            (publishingRuntimeFactory is null ? JsonPublishingPreferencesStore.CreateDefault() : null);
        _networkOwnerFactory = networkOwnerFactory ??
            (() => _options.ValidationProfile is { } profile ? profile.CreateNetworkOwner() :
                new NovaApplicationNetworkOwner(NovaSettingsStore.CreateDefault()));
        _genesisPlaybackControllerFactory = playbackControllerFactory is null
            ? (paths, settings, publicChannels) => _options.ValidationProfile is { } profile ? profile.CreatePlaybackController(paths) :
                CreateDefaultGenesisPlaybackController(paths, settings,
                    explicitPlayerPath: _options.PlayerExecutablePath, libMpvLibraryPath: _options.LibMpvLibraryPath,
                    publicChannels: publicChannels)
            : (_, _, _) => playbackControllerFactory();
    }

    public async ValueTask<NovaWindowsStartupResult> RunAsync(
        CancellationToken cancellationToken = default) =>
        await RunCoreAsync(
            projectionTarget: null,
            genesisSurfaceTarget: null,
            genesisPlaybackWindowSurfaceTarget: null,
            genesisViewModel: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

    public async ValueTask<NovaWindowsStartupResult> RunAsync(
        INovaAboutLegalProjectionTarget? projectionTarget,
        CancellationToken cancellationToken = default) =>
        await RunCoreAsync(
            projectionTarget,
            genesisSurfaceTarget: null,
            genesisPlaybackWindowSurfaceTarget: null,
            genesisViewModel: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

    public ValueTask<NovaWindowsStartupResult> RunGenesisAsync(
        CancellationToken cancellationToken = default)
    {
        var appearance = new GenesisAppearanceSettings(_options.ValidationProfile?.Settings ?? NovaSettingsStore.CreateDefault());
        return RunOwnedGenesisCoreAsync(
            (playbackPathSource, recordingSettings, publishingStatus, publishingCommands, rootState) =>
                new SystemGenesisInteractiveMainWindowSurfaceTarget(
                    new SystemGenesisMainWindowSurfaceTarget(),
                    SystemGenesisMainWindowInteractionBridgeFactory.Instance,
                    _options.ChannelCatalog,
                    _options.ChannelAutoRefreshInterval,
                    GenesisMainWindowResizeGuard.Create,
                    token =>
                    {
                        var state = rootState ?? throw new InvalidOperationException("Application state is unavailable.");
                        state.Tabs.BindPublishingStatus(publishingStatus, publishingCommands);
                        return state;
                    },
                    CreateSystemNotificationRuntime,
                    token => CaptureSystemNotificationSettingsPlacement(token, appearance),
                    _options.NetworkSettingsController, recordingSettings, ownsGuiNextState: false, appearanceSettings: appearance),
            loadApplicationState: true, appearance: appearance, cancellationToken: cancellationToken);
    }

    // The production root and managed composition tests use the same lifetime.
    // Surfaces receive a borrower; only this root initializes/disposes the owner.
    internal ValueTask<NovaWindowsStartupResult> RunOwnedGenesisAsync(
        Func<GenesisGuiNextPlaybackPreferencesSource, GenesisRecordingOutputDirectorySettings,
            IGenesisMainWindowSurfaceTarget> createSurface,
        GenesisRecordingOutputDirectorySettings? recordingSettings = null,
        CancellationToken cancellationToken = default) =>
        RunOwnedGenesisAsync(createSurface, recordingSettings, appearance: null, cancellationToken);

    internal ValueTask<NovaWindowsStartupResult> RunOwnedGenesisAsync(
        Func<GenesisGuiNextPlaybackPreferencesSource, GenesisRecordingOutputDirectorySettings,
            IGenesisMainWindowSurfaceTarget> createSurface,
        GenesisRecordingOutputDirectorySettings? recordingSettings,
        GenesisAppearanceSettings? appearance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(createSurface);
        return RunOwnedGenesisCoreAsync((paths, settings, _, _, _) => createSurface(paths, settings),
            recordingSettings, loadApplicationState: false, appearance: appearance, cancellationToken: cancellationToken);
    }

    private async ValueTask<NovaWindowsStartupResult> RunOwnedGenesisCoreAsync(
        Func<GenesisGuiNextPlaybackPreferencesSource, GenesisRecordingOutputDirectorySettings,
            INovaObsPublishingReadOnlyStatus, INovaGuiNextPublishingCommands, GenesisGuiNextApplicationState?, IGenesisMainWindowSurfaceTarget> createSurface,
        GenesisRecordingOutputDirectorySettings? recordingSettings = null,
        bool loadApplicationState = false,
        GenesisAppearanceSettings? appearance = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(createSurface);
        GenesisGuiNextApplicationState? applicationState = null;
        INovaApplicationNetworkOwner? networkOwner = null;
        IPlaybackSessionController? playbackController = null;
        NovaGuiNextPublishingComposition? publishing = null;
        PublishingSettingsController? publishingSettings = null;
        var result = NovaWindowsStartupResult.Faulted;
        try
        {
            _options.ValidationProfile?.CheckReady();
            networkOwner = _networkOwnerFactory() ??
                throw new InvalidOperationException("Application network owner is unavailable.");
            var paths = new GenesisGuiNextPlaybackPreferencesSource();
            recordingSettings ??= _options.ValidationProfile is { } profile ? profile.RecordingSettings :
                GenesisRecordingOutputDirectorySettings.CreateSystem();
            if (_options.ValidationProfile is not null &&
                !(await networkOwner.InitializeAsync().WaitAsync(cancellationToken).ConfigureAwait(false)).Initialized)
                return NovaWindowsStartupResult.Faulted;
            if (_publishingPreferencesStore is not null)
            {
                publishingSettings = new(_publishingPreferencesStore);
                await publishingSettings.LoadAsync(cancellationToken).ConfigureAwait(false);
            }
            if (loadApplicationState)
            {
                try
                {
                    applicationState = _options.ValidationProfile is { } validation ? validation.GetGuiState(cancellationToken) :
                        LoadSystemGuiNextState(cancellationToken, paths);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    /* Preserve auxiliary-state fallback; LAN has no registry and fails closed. */
                    FileNovaLogger.Default.Log(LogLevel.Warning, "Failed to load GUI Next application state.", exception);
                }
            }
            var publishingFactory = ReferenceEquals(_publishingRuntimeFactory, SystemNovaObsPublishingRuntimeFactory.Instance) && applicationState is not null
                ? new SystemNovaObsPublishingRuntimeFactory(applicationState, allowLan: _options.ValidationProfile is null,
                    portMappingProvider: () => (networkOwner as INovaPublishingPortMappingProvider)?.TryGetPortMappingManager())
                : _publishingRuntimeFactory;
            publishing = NovaGuiNextPublishingComposition.Create(publishingFactory, settings: publishingSettings);
            playbackController = _genesisPlaybackControllerFactory(paths, recordingSettings, applicationState?.PublicChannels);
            while (true)
            {
                var switchRequested = 0;
                using var windowCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Action<PeerCast.Nova.Foundation.Configuration.NovaGuiMode>? switchHandler = null;
                if (appearance is not null)
                {
                    switchHandler = _ =>
                    {
                        Interlocked.Exchange(ref switchRequested, 1);
                        // Win32 UI スレッド上の保存ハンドラからの同期再入デッドロックを防ぐ。
                        ThreadPool.QueueUserWorkItem(static state =>
                        {
                            var cts = (CancellationTokenSource)state!;
                            try { cts.Cancel(); }
                            catch (ObjectDisposedException) { }
                        }, windowCts);
                    };
                    appearance.ModeSwitchRequested += switchHandler;
                }

                try
                {
                    result = await RunGenesisAsync(
                        createSurface(paths, recordingSettings, publishing.Status, publishing.Commands, applicationState),
                        playbackWindowSurfaceTarget: null,
                        playbackController,
                        networkOwner,
                        windowCts.Token).ConfigureAwait(false);
                }
                finally
                {
                    if (appearance is not null && switchHandler is not null)
                    {
                        appearance.ModeSwitchRequested -= switchHandler;
                    }
                }

                if (appearance is not null &&
                    !cancellationToken.IsCancellationRequested &&
                    Volatile.Read(ref switchRequested) != 0 &&
                    result is NovaWindowsStartupResult.Cancelled or NovaWindowsStartupResult.Completed)
                {
                    appearance.ApplyPendingRuntimeMode();
                    continue;
                }

                break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result = NovaWindowsStartupResult.Cancelled;
        }
        catch (Exception exception)
        {
            FileNovaLogger.Default.Log(LogLevel.Error, "Unhandled exception during Genesis startup/run.", exception);
            result = NovaWindowsStartupResult.Faulted;
        }
        finally
        {
            // Close publishing command admission before other owners drain.
            publishing?.CloseAdmission();
            // Close network admission immediately on root shutdown, including
            // while the independent private playback/recording owner drains.
            Task? networkShutdown = null;
            if (networkOwner is not null)
            {
                try { networkShutdown = networkOwner.DisposeAsync().AsTask(); }
                catch (Exception exception)
                {
                    FileNovaLogger.Default.Log(LogLevel.Error, "Failed to initiate network owner shutdown.", exception);
                    result = NovaWindowsStartupResult.Faulted;
                }
            }
            if (playbackController is IAsyncDisposable ownedController)
            {
                try { await ownedController.DisposeAsync().ConfigureAwait(false); }
                catch (Exception exception)
                {
                    FileNovaLogger.Default.Log(LogLevel.Error, "Failed to dispose playback controller.", exception);
                    result = NovaWindowsStartupResult.Faulted;
                }
            }
            if (networkShutdown is not null)
            {
                // Join the owner's existing task; unconfirmed cleanup stays
                // a failure with its existing safe resource retention.
                try { await networkShutdown.ConfigureAwait(false); }
                catch (Exception exception)
                {
                    FileNovaLogger.Default.Log(LogLevel.Error, "Failed to complete network owner shutdown.", exception);
                    result = NovaWindowsStartupResult.Faulted;
                }
            }
            // GUI and host teardown has completed before releasing this owner.
            if (publishing is not null)
            {
                try { await publishing.DisposeAsync().ConfigureAwait(false); }
                catch (Exception exception)
                {
                    FileNovaLogger.Default.Log(LogLevel.Error, "Failed to dispose publishing composition.", exception);
                    result = NovaWindowsStartupResult.Faulted;
                }
            }
            if (publishingSettings is not null)
            {
                try { await publishingSettings.DisposeAsync().ConfigureAwait(false); }
                catch (Exception exception)
                {
                    FileNovaLogger.Default.Log(LogLevel.Error, "Failed to dispose publishing settings.", exception);
                    result = NovaWindowsStartupResult.Faulted;
                }
            }
            // Registry/policies outlive Publishing's actual child and reservation cleanup.
            if (applicationState is not null)
            {
                try { await applicationState.DisposeAsync().ConfigureAwait(false); }
                catch (Exception exception)
                {
                    FileNovaLogger.Default.Log(LogLevel.Error, "Failed to dispose GUI Next application state.", exception);
                    result = NovaWindowsStartupResult.Faulted;
                }
            }
        }
        return result;
    }

    internal static IPlaybackSessionController CreateDefaultGenesisPlaybackController(
        GenesisRecordingHistoryStore? historyStore = null, GenesisRecordingOutputDirectorySettings? recordingSettings = null) =>
        GuiNextDailyPlaybackSessionController.CreateSystem(recordingSettings: recordingSettings, historyStore: historyStore);

    internal static GuiNextDailyPlaybackSessionController CreateDefaultGenesisPlaybackController(
        GenesisGuiNextPlaybackPreferencesSource pathSource,
        GenesisRecordingOutputDirectorySettings? recordingSettings = null,
        GenesisRecordingHistoryStore? historyStore = null,
        string? explicitPlayerPath = null,
        string? libMpvLibraryPath = null,
        GenesisGuiNextPublicChannelComposition? publicChannels = null)
    {
        ArgumentNullException.ThrowIfNull(pathSource);
        var windowStateStore = LibMpvPlaybackWindowStateStore.CreateSystem();
        var audioStore = new LibMpvPlaybackAudioSessionStore(windowStateStore.Current.AudioState);
        IDailyPlaybackPlayerLauncher CreateLauncher()
        {
            var preferences = pathSource.Current;
            if (libMpvLibraryPath is not null)
            {
                return new LibMpvPlaybackPlayerLauncher(
                    libMpvLibraryPath, audioStore, lowLatency: preferences.LowLatencyInternalPlayback, windowStateStore);
            }

            if (explicitPlayerPath is not null)
            {
                return new PcrPlayerExternalPlayerLauncher(
                    explicitPlayerPath,
                    () => pathSource.Current.ExternalPlayerArguments);
            }

            if (preferences.PlayerMode == GenesisPlaybackPlayerMode.Internal)
            {
                return new LibMpvPlaybackPlayerLauncher(
                    preferences.LibMpvLibraryPath ?? LibMpvRuntimeProvisioner.TryFindAvailableLibraryPath() ?? string.Empty,
                    audioStore,
                    lowLatency: preferences.LowLatencyInternalPlayback,
                    windowStateStore);
            }

            if (TryResolveAutoFallbackLibMpvPath(
                    preferences,
                    Environment.GetEnvironmentVariable(PcrPlayerExternalPlayerLauncher.ExecutablePathEnvironmentVariable),
                    Environment.GetEnvironmentVariable("PATH"),
                    () => LibMpvRuntimeProvisioner.TryFindAvailableLibraryPath()) is { } fallbackLibraryPath)
            {
                return new LibMpvPlaybackPlayerLauncher(
                    fallbackLibraryPath,
                    audioStore,
                    lowLatency: preferences.LowLatencyInternalPlayback,
                    windowStateStore);
            }

            return new PcrPlayerExternalPlayerLauncher(
                preferences.PcrPlayerExecutablePath,
                () => pathSource.Current.ExternalPlayerArguments);
        }

        return GuiNextDailyPlaybackSessionController.CreateSystemWithPlayerLauncherFactory(
            CreateLauncher, recordingSettings, historyStore, publicChannels);
    }

    internal static string? TryResolveAutoFallbackLibMpvPath(
        GenesisGuiNextPlaybackPreferences preferences,
        string? environmentPlayerPath,
        string? searchPath,
        Func<string?> findAvailableLibMpv)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(findAvailableLibMpv);
        if (preferences.PlayerMode != GenesisPlaybackPlayerMode.External ||
            preferences.PcrPlayerExecutablePath is not null ||
            !string.IsNullOrWhiteSpace(environmentPlayerPath) ||
            PcrPlayerExternalPlayerLauncher.HasExecutableOnPath(searchPath))
        {
            return null;
        }

        var available = findAvailableLibMpv();
        return string.IsNullOrWhiteSpace(available) ? null : available;
    }

    internal async ValueTask<NovaWindowsStartupResult> RunGenesisAsync(
        IGenesisMainWindowSurfaceTarget surfaceTarget,
        IPlaybackSessionController playbackController,
        CancellationToken cancellationToken = default) =>
        await RunGenesisAsync(
            surfaceTarget,
            playbackWindowSurfaceTarget: null,
            playbackController,
            cancellationToken).ConfigureAwait(false);

    internal async ValueTask<NovaWindowsStartupResult> RunGenesisAsync(
        IGenesisMainWindowSurfaceTarget surfaceTarget,
        IGenesisPlaybackWindowSurfaceTarget? playbackWindowSurfaceTarget,
        IPlaybackSessionController playbackController,
        CancellationToken cancellationToken = default) =>
        await RunGenesisAsync(surfaceTarget, playbackWindowSurfaceTarget, playbackController,
            networkOwner: null, cancellationToken).ConfigureAwait(false);

    private async ValueTask<NovaWindowsStartupResult> RunGenesisAsync(
        IGenesisMainWindowSurfaceTarget surfaceTarget,
        IGenesisPlaybackWindowSurfaceTarget? playbackWindowSurfaceTarget,
        IPlaybackSessionController playbackController,
        INovaApplicationNetworkOwner? networkOwner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(surfaceTarget);
        ArgumentNullException.ThrowIfNull(playbackController);
        return await RunCoreAsync(
            projectionTarget: null,
            surfaceTarget,
            playbackWindowSurfaceTarget,
            new GenesisMainViewModel(
                playbackController,
                hasPlaybackWindowSurface: playbackWindowSurfaceTarget is not null,
                networkSettingsAdapter: networkOwner is null ? null : new GuiNextNetworkSettingsAdapter(networkOwner)),
            networkOwner, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<NovaWindowsStartupResult> RunCoreAsync(
        INovaAboutLegalProjectionTarget? projectionTarget,
        IGenesisMainWindowSurfaceTarget? genesisSurfaceTarget,
        IGenesisPlaybackWindowSurfaceTarget? genesisPlaybackWindowSurfaceTarget,
        GenesisMainViewModel? genesisViewModel,
        INovaApplicationNetworkOwner? networkOwner = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await RunHostedCoreAsync(
                projectionTarget,
                genesisSurfaceTarget,
                genesisPlaybackWindowSurfaceTarget,
                genesisViewModel,
                networkOwner, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return NovaWindowsStartupResult.Cancelled;
        }
        catch (Exception exception)
        {
            FileNovaLogger.Default.Log(LogLevel.Error, "Unhandled exception in NovaWindowsStartupComposition.RunCoreAsync.", exception);
            return NovaWindowsStartupResult.Faulted;
        }
    }

    private async ValueTask<NovaWindowsStartupResult> RunHostedCoreAsync(
        INovaAboutLegalProjectionTarget? projectionTarget,
        IGenesisMainWindowSurfaceTarget? genesisSurfaceTarget,
        IGenesisPlaybackWindowSurfaceTarget? genesisPlaybackWindowSurfaceTarget,
        GenesisMainViewModel? genesisViewModel,
        INovaApplicationNetworkOwner? networkOwner,
        CancellationToken cancellationToken)
    {
        var hostOptions = genesisSurfaceTarget is not null
            ? _options.Host with { DeferInitialShow = true }
            : _options.Host;
        await using var host = _hostFactory.Create(hostOptions);
        IAsyncDisposable? projectionLifetime = null;
        IAsyncDisposable? genesisLifetime = null;
        GenesisPlaybackWindowCoordinator? playbackWindowCoordinator = null;
        var hostStarted = false;
        try
        {
            var start = await host.StartAsync(cancellationToken).ConfigureAwait(false);
            if (start != PlaybackWindowsPhysicalRunnerResult.Started)
            {
                return MapStartResult(start);
            }

            hostStarted = true;
            if (networkOwner is not null)
            {
                var initialization = await networkOwner.InitializeAsync()
                    .WaitAsync(cancellationToken).ConfigureAwait(false);
                if (!initialization.Initialized) return NovaWindowsStartupResult.Faulted;
            }
            if (genesisSurfaceTarget is not null)
            {
                if (genesisViewModel is null ||
                    host is not IGenesisMainWindowDispatcher genesisDispatcher)
                {
                    return NovaWindowsStartupResult.Faulted;
                }

                var genesisProjection = await GenesisMainWindowProjection.OpenAsync(
                    genesisDispatcher,
                    genesisSurfaceTarget,
                    genesisViewModel,
                    cancellationToken).ConfigureAwait(false);
                if (genesisProjection.Result != GenesisMainWindowProjectionResult.Projected ||
                    genesisProjection.Lifetime is null)
                {
                    return genesisProjection.Result == GenesisMainWindowProjectionResult.Cancelled
                        ? NovaWindowsStartupResult.Cancelled
                        : NovaWindowsStartupResult.Faulted;
                }
                genesisLifetime = genesisProjection.Lifetime;
            }

            if (genesisPlaybackWindowSurfaceTarget is not null)
            {
                if (genesisViewModel is null ||
                    host is not IGenesisMainWindowDispatcher genesisDispatcher)
                {
                    return NovaWindowsStartupResult.Faulted;
                }

                playbackWindowCoordinator = await GenesisPlaybackWindowCoordinator.OpenAsync(
                    genesisDispatcher,
                    genesisPlaybackWindowSurfaceTarget,
                    genesisViewModel,
                    cancellationToken).ConfigureAwait(false);
            }

            if (projectionTarget is not null)
            {
                if (host is not INovaAboutLegalDispatcher dispatcher)
                {
                    return NovaWindowsStartupResult.Faulted;
                }

                var projection = await NovaAboutLegalProjection.OpenAsync(
                    dispatcher,
                    projectionTarget,
                    cancellationToken).ConfigureAwait(false);
                if (projection.Result != NovaAboutLegalProjectionResult.Projected ||
                    projection.Lifetime is null)
                {
                    return projection.Result ==
                        NovaAboutLegalProjectionResult.PlatformUnsupported
                            ? NovaWindowsStartupResult.PlatformUnsupported
                            : NovaWindowsStartupResult.Faulted;
                }

                projectionLifetime = projection.Lifetime;
            }

            var exit = await host.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return exit switch
            {
                PlaybackWindowsPhysicalRunnerResult.Stopped =>
                    NovaWindowsStartupResult.Completed,
                PlaybackWindowsPhysicalRunnerResult.Cancelled =>
                    NovaWindowsStartupResult.Cancelled,
                _ => NovaWindowsStartupResult.Faulted,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return NovaWindowsStartupResult.Cancelled;
        }
        catch (Exception exception)
        {
            FileNovaLogger.Default.Log(LogLevel.Error, "Unhandled exception in NovaWindowsStartupComposition.RunCoreAsync.", exception);
            return NovaWindowsStartupResult.Faulted;
        }
        finally
        {
            if (projectionLifetime is not null)
            {
                try
                {
                    await projectionLifetime.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    FileNovaLogger.Default.Log(LogLevel.Warning, "Failed to dispose projection lifetime.", exception);
                }
            }

            if (playbackWindowCoordinator is not null)
            {
                try
                {
                    await playbackWindowCoordinator.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    FileNovaLogger.Default.Log(LogLevel.Warning, "Failed to dispose playback window coordinator.", exception);
                }
            }

            if (genesisLifetime is not null)
            {
                try
                {
                    await genesisLifetime.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    FileNovaLogger.Default.Log(LogLevel.Warning, "Failed to dispose Genesis surface lifetime.", exception);
                }
            }

            await StopHostAsync(host, hostStarted).ConfigureAwait(false);
        }
    }

    private static GenesisGuiNextApplicationState LoadSystemGuiNextState(
        CancellationToken cancellationToken,
        GenesisGuiNextPlaybackPreferencesSource? pathSource = null) =>
        SystemGenesisGuiNextApplicationState.LoadAsync(pathSource, cancellationToken)
            .AsTask()
            .GetAwaiter()
            .GetResult();

    private static GenesisChannelNotificationRuntime CreateSystemNotificationRuntime(
        long windowToken,
        GenesisGuiNextApplicationState? guiNextState) =>
        guiNextState is null
            ? GenesisChannelNotificationRuntime.Empty
            : SystemGenesisGuiNextNotificationRuntime.Create(windowToken, guiNextState);

    private static StartupNotificationSettingsPlacement?
        CaptureSystemNotificationSettingsPlacement(long windowToken, GenesisAppearanceSettings? appearance = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var placement = GenesisChannelNotificationSettingsButtonPlacement.Capture(windowToken, appearance);
        return placement is null
            ? null
            : new StartupNotificationSettingsPlacement(placement);
    }

    private static async ValueTask StopHostAsync(
        INovaWindowsStartupHost host,
        bool hostStarted)
    {
        var stop = await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        if (hostStarted && stop == PlaybackWindowsPhysicalRunnerResult.Faulted)
        {
            throw new InvalidOperationException("Windows host shutdown faulted.");
        }
    }

    private static NovaWindowsStartupResult MapStartResult(
        PlaybackWindowsPhysicalRunnerResult result) => result switch
        {
            PlaybackWindowsPhysicalRunnerResult.PlatformUnsupported =>
                NovaWindowsStartupResult.PlatformUnsupported,
            PlaybackWindowsPhysicalRunnerResult.Cancelled =>
                NovaWindowsStartupResult.Cancelled,
            PlaybackWindowsPhysicalRunnerResult.Faulted =>
                NovaWindowsStartupResult.Faulted,
            _ => NovaWindowsStartupResult.StartupRejected,
        };

    private sealed class StartupNotificationSettingsPlacement :
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

        public StartupNotificationSettingsPlacement(
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
}

internal interface INovaWindowsStartupHostFactory
{
    INovaWindowsStartupHost Create(PlaybackWindowsPhysicalRunnerOptions options);
}

internal interface INovaWindowsStartupHost : IAsyncDisposable
{
    ValueTask<PlaybackWindowsPhysicalRunnerResult> StartAsync(
        CancellationToken cancellationToken);

    ValueTask<PlaybackWindowsPhysicalRunnerResult> WaitForExitAsync(
        CancellationToken cancellationToken);

    ValueTask<PlaybackWindowsPhysicalRunnerResult> StopAsync(
        CancellationToken cancellationToken);
}

internal sealed class SystemNovaWindowsStartupHostFactory :
    INovaWindowsStartupHostFactory
{
    public static SystemNovaWindowsStartupHostFactory Instance { get; } = new();

    private SystemNovaWindowsStartupHostFactory()
    {
    }

    public INovaWindowsStartupHost Create(PlaybackWindowsPhysicalRunnerOptions options)
    {
        // The SDK puts the same Win32 icon in the assembly and the Windows apphost.
        // Use the assembly when Nova runs through dotnet.exe.
        var assemblyPath = typeof(SystemNovaWindowsStartupHostFactory).Assembly.Location;
        return new SystemNovaWindowsStartupHost(new(options with
        {
            ApplicationIconPath = assemblyPath.Length == 0 ? Environment.ProcessPath : assemblyPath,
        }));
    }
}

internal sealed class SystemNovaWindowsStartupHost :
    INovaWindowsStartupHost,
    INovaAboutLegalDispatcher,
    IGenesisMainWindowDispatcher
{
    private readonly PlaybackWindowsPhysicalRunnerHost _host;

    public SystemNovaWindowsStartupHost(PlaybackWindowsPhysicalRunnerHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    public long WindowToken => _host.Snapshot.WindowToken;

    public ValueTask<PlaybackWindowsPhysicalRunnerResult> StartAsync(
        CancellationToken cancellationToken) => _host.StartAsync(cancellationToken);

    public ValueTask<PlaybackWindowsPhysicalRunnerResult> WaitForExitAsync(
        CancellationToken cancellationToken) => _host.WaitForExitAsync(cancellationToken);

    public ValueTask<PlaybackWindowsPhysicalRunnerResult> StopAsync(
        CancellationToken cancellationToken) => _host.StopAsync(cancellationToken);

    public async ValueTask<NovaAboutLegalDispatcherInvocation> InvokeAsync(
        Func<CancellationToken, ValueTask<IAsyncDisposable?>> operation,
        CancellationToken cancellationToken)
    {
        var invocation = await _host.InvokeApplicationAsync(
            operation,
            cancellationToken).ConfigureAwait(false);
        return invocation.Result switch
        {
            PlaybackWindowsApplicationDispatcherResult.Completed =>
                new(NovaAboutLegalDispatcherResult.Completed, invocation.Value),
            PlaybackWindowsApplicationDispatcherResult.Cancelled =>
                new(NovaAboutLegalDispatcherResult.Cancelled, null),
            PlaybackWindowsApplicationDispatcherResult.Faulted =>
                new(NovaAboutLegalDispatcherResult.Faulted, null),
            _ => new(NovaAboutLegalDispatcherResult.Rejected, null),
        };
    }

    public async ValueTask<GenesisMainWindowDispatcherInvocation> InvokeGenesisAsync(
        Func<long, CancellationToken, ValueTask<IAsyncDisposable?>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var invocation = await _host.InvokeApplicationAsync(
            operationCancellationToken => operation(
                _host.Snapshot.WindowToken,
                operationCancellationToken),
            cancellationToken).ConfigureAwait(false);
        return invocation.Result switch
        {
            PlaybackWindowsApplicationDispatcherResult.Completed =>
                new(GenesisMainWindowDispatcherResult.Completed, invocation.Value),
            PlaybackWindowsApplicationDispatcherResult.Cancelled =>
                new(GenesisMainWindowDispatcherResult.Cancelled, null),
            PlaybackWindowsApplicationDispatcherResult.Shutdown =>
                new(GenesisMainWindowDispatcherResult.Shutdown, null),
            _ => new(GenesisMainWindowDispatcherResult.Faulted, null),
        };
    }

    public ValueTask DisposeAsync() => _host.DisposeAsync();
}
