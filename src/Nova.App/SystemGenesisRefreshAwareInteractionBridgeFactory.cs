using System.Collections.Concurrent;

namespace PeerCast.Nova.App;

internal sealed class SystemGenesisRefreshAwareInteractionBridgeFactory :
    IGenesisChannelRefreshInteractionBridgeFactory
{
    private readonly IGenesisMainWindowInteractionBridgeFactory _inner;

    public static SystemGenesisRefreshAwareInteractionBridgeFactory Instance { get; } =
        new(SystemGenesisMainWindowInteractionBridgeFactory.Instance);

    internal SystemGenesisRefreshAwareInteractionBridgeFactory(
        IGenesisMainWindowInteractionBridgeFactory inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public IAsyncDisposable? Create(long windowToken, GenesisMainViewModel viewModel) =>
        _inner.Create(windowToken, viewModel);

    public IAsyncDisposable? Create(
        long windowToken,
        GenesisMainViewModel viewModel,
        GenesisChannelCatalogSession channelCatalogSession)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(channelCatalogSession);
        var refreshPlacement = OperatingSystem.IsWindows()
            ? GenesisChannelRefreshButtonPlacement.Capture(windowToken)
            : null;
        var playbackModePlacement = OperatingSystem.IsWindows()
            ? GenesisPlaybackModeButtonPlacement.Capture(windowToken)
            : null;
        var contextMenuPlacement = OperatingSystem.IsWindows()
            ? GenesisChannelContextMenuPlacement.Capture(windowToken)
            : null;
        var directContextMenuPlacement = OperatingSystem.IsWindows()
            ? GenesisChannelDirectContextMenuPlacement.Capture(windowToken)
            : null;
        var aboutPlacement = OperatingSystem.IsWindows()
            ? GenesisAboutButtonPlacement.Capture(windowToken)
            : null;
        var sourceEnterPlacement = OperatingSystem.IsWindows()
            ? GenesisSourceEnterPlacement.Capture(windowToken)
            : null;
        var guiNextTabs = channelCatalogSession.GuiNextTabs;
        var appShellPlacement = OperatingSystem.IsWindows() && guiNextTabs is not null
            ? GenesisGuiNextAppShellPlacement.Capture(windowToken)
            : null;
        IAsyncDisposable? innerLifetime;
        using (GenesisChannelRefreshInteractionScope.Enter(viewModel, channelCatalogSession))
        {
            innerLifetime = _inner.Create(windowToken, viewModel);
        }

        if (innerLifetime is null)
        {
            return null;
        }

        return CreateCompositeLifetime(
            innerLifetime,
            () => refreshPlacement?.Open(
                viewModel,
                channelCatalogSession,
                showButton: guiNextTabs is null),
            () => playbackModePlacement?.Open(viewModel),
            () => OpenContextMenuRoute(
                contextMenuPlacement,
                directContextMenuPlacement,
                viewModel,
                channelCatalogSession),
            () => aboutPlacement?.Open(showButton: guiNextTabs is null),
            () => sourceEnterPlacement?.Open(viewModel, channelCatalogSession),
            () => guiNextTabs is null
                ? null
                : OpenGuiNextPresentation(
                    windowToken,
                    guiNextTabs,
                    appShellPlacement,
                    viewModel,
                    channelCatalogSession));
    }

    internal static IAsyncDisposable CreateCompositeLifetime(
        IAsyncDisposable innerLifetime,
        Func<IAsyncDisposable?> openRefresh,
        Func<IAsyncDisposable?> openPlaybackMode,
        Func<IAsyncDisposable?> openContextMenu,
        Func<IAsyncDisposable?> openAbout,
        Func<IAsyncDisposable?> openSourceEnter,
        Func<IAsyncDisposable?> openGuiNextPresentation)
    {
        ArgumentNullException.ThrowIfNull(innerLifetime);
        ArgumentNullException.ThrowIfNull(openRefresh);
        ArgumentNullException.ThrowIfNull(openPlaybackMode);
        ArgumentNullException.ThrowIfNull(openContextMenu);
        ArgumentNullException.ThrowIfNull(openAbout);
        ArgumentNullException.ThrowIfNull(openSourceEnter);
        ArgumentNullException.ThrowIfNull(openGuiNextPresentation);

        var refreshLifetime = TryOpenOptional(openRefresh);
        var playbackModeLifetime = TryOpenOptional(openPlaybackMode);
        var contextMenuLifetime = TryOpenOptional(openContextMenu);
        var aboutLifetime = TryOpenOptional(openAbout);
        var sourceEnterLifetime = TryOpenOptional(openSourceEnter);
        var guiNextPresentationLifetime = TryOpenOptional(openGuiNextPresentation);
        return refreshLifetime is null &&
            playbackModeLifetime is null &&
            contextMenuLifetime is null &&
            aboutLifetime is null &&
            sourceEnterLifetime is null &&
            guiNextPresentationLifetime is null
                ? innerLifetime
                : new CompositeLifetime(
                    innerLifetime,
                    refreshLifetime,
                    playbackModeLifetime,
                    contextMenuLifetime,
                    aboutLifetime,
                    sourceEnterLifetime,
                    guiNextPresentationLifetime);
    }

    internal static IAsyncDisposable? OpenPreferredOptionalRoute(
        Func<IAsyncDisposable?> openPreferred,
        Func<IAsyncDisposable?> openFallback)
    {
        ArgumentNullException.ThrowIfNull(openPreferred);
        ArgumentNullException.ThrowIfNull(openFallback);
        var preferredLifetime = TryOpenOptional(openPreferred);
        return preferredLifetime ?? TryOpenOptional(openFallback);
    }

    private static IAsyncDisposable? OpenContextMenuRoute(
        GenesisChannelContextMenuPlacement? notificationPlacement,
        GenesisChannelDirectContextMenuPlacement? directPlacement,
        GenesisMainViewModel viewModel,
        GenesisChannelCatalogSession channelCatalogSession) =>
        OpenPreferredOptionalRoute(
            () => directPlacement?.Open(viewModel, channelCatalogSession),
            () => notificationPlacement?.Open(viewModel, channelCatalogSession));

    private static IAsyncDisposable? OpenGuiNextPresentation(
        long windowToken,
        GenesisGuiNextTabsViewModel tabs,
        GenesisGuiNextAppShellPlacement? appShellPlacement,
        GenesisMainViewModel viewModel,
        GenesisChannelCatalogSession channelCatalogSession)
    {
        var feedbackLifetime = TryOpenOptional(
            () => SystemGenesisGuiNextFavoriteFeedback.Open(windowToken, tabs));
        var interaction = new GenesisMainWindowInteraction(viewModel, channelCatalogSession);
        var listChromeLifetime = TryOpenOptional(
            () => appShellPlacement?.Open(tabs, viewModel, interaction));
        if (feedbackLifetime is null)
        {
            return listChromeLifetime;
        }

        if (listChromeLifetime is null)
        {
            return feedbackLifetime;
        }

        return new GuiNextPresentationLifetime(
            feedbackLifetime,
            listChromeLifetime);
    }

    private static IAsyncDisposable? TryOpenOptional(Func<IAsyncDisposable?> open)
    {
        try
        {
            return open();
        }
        catch
        {
            // Auxiliary Genesis controls are optional. A failed control must not
            // abandon lifetimes that were already created by the main bridge.
            return null;
        }
    }

    private sealed class GuiNextPresentationLifetime : IAsyncDisposable
    {
        private readonly IAsyncDisposable _feedbackLifetime;
        private readonly IAsyncDisposable _listChromeLifetime;
        private int _disposed;

        public GuiNextPresentationLifetime(
            IAsyncDisposable feedbackLifetime,
            IAsyncDisposable listChromeLifetime)
        {
            _feedbackLifetime = feedbackLifetime;
            _listChromeLifetime = listChromeLifetime;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                await _feedbackLifetime.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await _listChromeLifetime.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private sealed class CompositeLifetime : IAsyncDisposable
    {
        private readonly IAsyncDisposable _innerLifetime;
        private readonly IAsyncDisposable? _refreshLifetime;
        private readonly IAsyncDisposable? _playbackModeLifetime;
        private readonly IAsyncDisposable? _contextMenuLifetime;
        private readonly IAsyncDisposable? _aboutLifetime;
        private readonly IAsyncDisposable? _sourceEnterLifetime;
        private readonly IAsyncDisposable? _guiNextPresentationLifetime;
        private int _disposed;

        public CompositeLifetime(
            IAsyncDisposable innerLifetime,
            IAsyncDisposable? refreshLifetime,
            IAsyncDisposable? playbackModeLifetime,
            IAsyncDisposable? contextMenuLifetime,
            IAsyncDisposable? aboutLifetime,
            IAsyncDisposable? sourceEnterLifetime,
            IAsyncDisposable? guiNextPresentationLifetime)
        {
            _innerLifetime = innerLifetime;
            _refreshLifetime = refreshLifetime;
            _playbackModeLifetime = playbackModeLifetime;
            _contextMenuLifetime = contextMenuLifetime;
            _aboutLifetime = aboutLifetime;
            _sourceEnterLifetime = sourceEnterLifetime;
            _guiNextPresentationLifetime = guiNextPresentationLifetime;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                if (_guiNextPresentationLifetime is not null)
                {
                    await _guiNextPresentationLifetime.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                try
                {
                    if (_aboutLifetime is not null)
                    {
                        await _aboutLifetime.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    try
                    {
                        if (_contextMenuLifetime is not null)
                        {
                            await _contextMenuLifetime.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        try
                        {
                            if (_playbackModeLifetime is not null)
                            {
                                await _playbackModeLifetime.DisposeAsync().ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            try
                            {
                                if (_refreshLifetime is not null)
                                {
                                    await _refreshLifetime.DisposeAsync().ConfigureAwait(false);
                                }
                            }
                            finally
                            {
                                try
                                {
                                    if (_sourceEnterLifetime is not null)
                                    {
                                        await _sourceEnterLifetime.DisposeAsync().ConfigureAwait(false);
                                    }
                                }
                                finally
                                {
                                    await _innerLifetime.DisposeAsync().ConfigureAwait(false);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}

internal static class GenesisChannelRefreshInteractionScope
{
    private static readonly ConcurrentDictionary<GenesisMainViewModel, GenesisChannelCatalogSession>
        SessionsByViewModel = new();

    public static IDisposable Enter(
        GenesisMainViewModel viewModel,
        GenesisChannelCatalogSession session)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(session);
        if (!SessionsByViewModel.TryAdd(viewModel, session))
        {
            throw new InvalidOperationException(
                "A Genesis channel-refresh interaction scope is already active.");
        }

        return new Scope(viewModel, session);
    }

    public static GenesisChannelCatalogSession? Find(GenesisMainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        return SessionsByViewModel.TryGetValue(viewModel, out var session)
            ? session
            : null;
    }

    private sealed class Scope : IDisposable
    {
        private readonly GenesisMainViewModel _viewModel;
        private readonly GenesisChannelCatalogSession _session;
        private int _disposed;

        public Scope(
            GenesisMainViewModel viewModel,
            GenesisChannelCatalogSession session)
        {
            _viewModel = viewModel;
            _session = session;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _ = SessionsByViewModel.TryRemove(
                    new KeyValuePair<GenesisMainViewModel, GenesisChannelCatalogSession>(
                        _viewModel,
                        _session));
            }
        }
    }
}
