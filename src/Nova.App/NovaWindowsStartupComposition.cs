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
    private readonly Func<
        GenesisGuiNextPlaybackPathSource,
        GenesisRecordingOutputDirectorySettings,
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
        Func<INovaApplicationNetworkOwner>? networkOwnerFactory = null)
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
        _networkOwnerFactory = networkOwnerFactory ??
            (() => _options.ValidationProfile is { } profile ? profile.CreateNetworkOwner() :
                new NovaApplicationNetworkOwner(NovaSettingsStore.CreateDefault()));
        _genesisPlaybackControllerFactory = playbackControllerFactory is null
            ? (paths, settings) => _options.ValidationProfile is { } profile ? profile.CreatePlaybackController(paths) :
                CreateDefaultGenesisPlaybackController(paths, settings)
            : (_, _) => playbackControllerFactory();
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
        CancellationToken cancellationToken = default) =>
        RunOwnedGenesisAsync(
            (playbackPathSource, recordingSettings) =>
                new SystemGenesisInteractiveMainWindowSurfaceTarget(
                    new SystemGenesisMainWindowSurfaceTarget(),
                    SystemGenesisMainWindowInteractionBridgeFactory.Instance,
                    _options.ChannelCatalog,
                    _options.ChannelAutoRefreshInterval,
                    GenesisMainWindowResizeGuard.Create,
                    token => _options.ValidationProfile is { } profile ? profile.GetGuiState(token) :
                        LoadSystemGuiNextState(token, playbackPathSource),
                    CreateSystemNotificationRuntime,
                    CaptureSystemNotificationSettingsPlacement,
                    _options.NetworkSettingsController, recordingSettings),
            cancellationToken: cancellationToken);

    // The production root and managed composition tests use the same lifetime.
    // Surfaces receive a borrower; only this root initializes/disposes the owner.
    internal async ValueTask<NovaWindowsStartupResult> RunOwnedGenesisAsync(
        Func<GenesisGuiNextPlaybackPathSource, GenesisRecordingOutputDirectorySettings,
            IGenesisMainWindowSurfaceTarget> createSurface,
        GenesisRecordingOutputDirectorySettings? recordingSettings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(createSurface);
        INovaApplicationNetworkOwner? networkOwner = null;
        IPlaybackSessionController? playbackController = null;
        var result = NovaWindowsStartupResult.Faulted;
        try
        {
            _options.ValidationProfile?.CheckReady();
            networkOwner = _networkOwnerFactory() ??
                throw new InvalidOperationException("Application network owner is unavailable.");
            var paths = new GenesisGuiNextPlaybackPathSource();
            recordingSettings ??= _options.ValidationProfile is { } profile ? profile.RecordingSettings :
                GenesisRecordingOutputDirectorySettings.CreateSystem();
            if (_options.ValidationProfile is not null &&
                !(await networkOwner.InitializeAsync().WaitAsync(cancellationToken).ConfigureAwait(false)).Initialized)
                return NovaWindowsStartupResult.Faulted;
            playbackController = _genesisPlaybackControllerFactory(paths, recordingSettings);
            result = await RunGenesisAsync(
                createSurface(paths, recordingSettings),
                playbackWindowSurfaceTarget: null,
                playbackController,
                networkOwner,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result = NovaWindowsStartupResult.Cancelled;
        }
        catch
        {
            result = NovaWindowsStartupResult.Faulted;
        }
        finally
        {
            // Close network admission immediately on root shutdown, including
            // while the independent private playback/recording owner drains.
            Task? networkShutdown = null;
            if (networkOwner is not null)
            {
                try { networkShutdown = networkOwner.DisposeAsync().AsTask(); }
                catch { result = NovaWindowsStartupResult.Faulted; }
            }
            if (playbackController is IAsyncDisposable ownedController)
            {
                try { await ownedController.DisposeAsync().ConfigureAwait(false); }
                catch { result = NovaWindowsStartupResult.Faulted; }
            }
            if (networkShutdown is not null)
            {
                // Join the owner's existing task; unconfirmed cleanup stays
                // a failure with its existing safe resource retention.
                try { await networkShutdown.ConfigureAwait(false); }
                catch { result = NovaWindowsStartupResult.Faulted; }
            }
        }
        return result;
    }

    internal static IPlaybackSessionController CreateDefaultGenesisPlaybackController(
        GenesisRecordingHistoryStore? historyStore = null, GenesisRecordingOutputDirectorySettings? recordingSettings = null) =>
        GuiNextDailyPlaybackSessionController.CreateSystem(recordingSettings: recordingSettings, historyStore: historyStore);

    internal static GuiNextDailyPlaybackSessionController CreateDefaultGenesisPlaybackController(
        GenesisGuiNextPlaybackPathSource pathSource,
        GenesisRecordingOutputDirectorySettings? recordingSettings = null,
        GenesisRecordingHistoryStore? historyStore = null)
    {
        ArgumentNullException.ThrowIfNull(pathSource);
        return GuiNextDailyPlaybackSessionController.CreateSystemWithPathProvider(
            () => pathSource.Current, recordingSettings, historyStore);
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
        catch
        {
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
        await using var host = _hostFactory.Create(_options.Host);
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
        catch
        {
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
                catch
                {
                    // Host cleanup must still run after a rejected projection close.
                }
            }

            if (playbackWindowCoordinator is not null)
            {
                try
                {
                    await playbackWindowCoordinator.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Main GUI and host cleanup still own the final shutdown path.
                }
            }

            if (genesisLifetime is not null)
            {
                try
                {
                    await genesisLifetime.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Host cleanup must still run after a rejected GUI surface close.
                }
            }

            await StopHostAsync(host, hostStarted).ConfigureAwait(false);
        }
    }

    private static GenesisGuiNextApplicationState LoadSystemGuiNextState(
        CancellationToken cancellationToken,
        GenesisGuiNextPlaybackPathSource? pathSource = null) =>
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
        CaptureSystemNotificationSettingsPlacement(long windowToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var placement = GenesisChannelNotificationSettingsButtonPlacement.Capture(windowToken);
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

    public INovaWindowsStartupHost Create(PlaybackWindowsPhysicalRunnerOptions options) =>
        new SystemNovaWindowsStartupHost(new(options));
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
