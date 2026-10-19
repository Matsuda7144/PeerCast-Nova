namespace PeerCast.Nova.App.Tests;

public sealed class SystemGenesisRefreshAwareInteractionBridgeFactoryTests
{
    [Fact]
    public async Task RefreshAwareCreateBindsCatalogSessionToCreatedInteraction()
    {
        var updated = new GenesisChannelItem(
            "Updated",
            "pcp://example/updated",
            "[game - updated]");
        var catalog = new SequencedCatalog(
            GenesisChannelCatalogSnapshot.Empty,
            new([updated]));
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        await using var session = GenesisChannelCatalogSession.Start(catalog, viewModel);
        var inner = new RecordingBridgeFactory();
        var factory = new SystemGenesisRefreshAwareInteractionBridgeFactory(inner);

        var lifetime = Assert.IsAssignableFrom<IAsyncDisposable>(
            factory.Create(7144, viewModel, session));
        await using var lifetimeScope = lifetime;
        var interaction = Assert.IsType<GenesisMainWindowInteraction>(inner.Interaction);

        await interaction.RefreshChannelsAsync();

        Assert.Equal(2, catalog.ReadCount);
        Assert.Single(viewModel.Channels);
        Assert.Same(updated, viewModel.Channels[0]);
    }

    [Fact]
    public async Task RefreshAwareCreateExposesGuiNextTabsDuringInnerBridgeCreation()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());
        var publisher = new GenesisGuiNextChannelPresencePublisher(
            tabs,
            EmptyGenesisChannelPresenceNotificationPublisher.Instance);
        await using var session = GenesisChannelCatalogSession.Create(
            EmptyGenesisChannelCatalog.Instance,
            viewModel,
            publisher);
        var inner = new RecordingBridgeFactory();
        var factory = new SystemGenesisRefreshAwareInteractionBridgeFactory(inner);

        var lifetime = Assert.IsAssignableFrom<IAsyncDisposable>(
            factory.Create(7144, viewModel, session));
        await using var lifetimeScope = lifetime;

        Assert.Same(tabs, inner.GuiNextTabsDuringCreate);
        Assert.Null(GenesisChannelRefreshInteractionScope.Find(viewModel));
    }

    [Fact]
    public async Task RefreshScopeIsRemovedAfterInnerBridgeCreation()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var catalog = new SequencedCatalog(GenesisChannelCatalogSnapshot.Empty);
        await using var session = GenesisChannelCatalogSession.Create(catalog, viewModel);
        var inner = new RecordingBridgeFactory();
        var factory = new SystemGenesisRefreshAwareInteractionBridgeFactory(inner);

        var lifetime = Assert.IsAssignableFrom<IAsyncDisposable>(
            factory.Create(7144, viewModel, session));
        await using var lifetimeScope = lifetime;

        Assert.Null(GenesisChannelRefreshInteractionScope.Find(viewModel));
    }

    [Fact]
    public async Task AuxiliaryCreationFailureKeepsEarlierLifetimesOwned()
    {
        var events = new List<string>();
        var innerLifetime = new RecordingDisposalLifetime(events, "CloseInner");
        var refreshLifetime = new RecordingDisposalLifetime(events, "CloseRefresh");

        var lifetime = SystemGenesisRefreshAwareInteractionBridgeFactory.CreateCompositeLifetime(
            innerLifetime,
            () => refreshLifetime,
            static () => throw new InvalidOperationException("Playback mode creation failed."),
            static () => null,
            static () => null,
            static () => null,
            static () => null);

        await lifetime.DisposeAsync();

        Assert.Equal(["CloseRefresh", "CloseInner"], events);
    }

    [Fact]
    public async Task GuiNextPresentationLifetimeClosesBeforeInnerBridge()
    {
        var events = new List<string>();
        var innerLifetime = new RecordingDisposalLifetime(events, "CloseInner");
        var guiNextPresentationLifetime = new RecordingDisposalLifetime(
            events,
            "CloseGuiNextPresentation");

        var lifetime = SystemGenesisRefreshAwareInteractionBridgeFactory.CreateCompositeLifetime(
            innerLifetime,
            static () => null,
            static () => null,
            static () => null,
            static () => null,
            static () => null,
            () => guiNextPresentationLifetime);

        await lifetime.DisposeAsync();

        Assert.Equal(["CloseGuiNextPresentation", "CloseInner"], events);
    }

    [Fact]
    public async Task PreferredOptionalRouteDoesNotOpenFallbackWhenAvailable()
    {
        var events = new List<string>();
        var preferred = new RecordingDisposalLifetime(events, "ClosePreferred");
        var fallbackOpened = false;

        var lifetime = SystemGenesisRefreshAwareInteractionBridgeFactory.OpenPreferredOptionalRoute(
            () => preferred,
            () =>
            {
                fallbackOpened = true;
                return new RecordingDisposalLifetime(events, "CloseFallback");
            });

        var route = Assert.IsAssignableFrom<IAsyncDisposable>(lifetime);
        Assert.False(fallbackOpened);

        await route.DisposeAsync();

        Assert.Equal(["ClosePreferred"], events);
    }

    [Fact]
    public async Task PreferredOptionalRouteFallsBackWhenPreferredUnavailable()
    {
        var events = new List<string>();
        var fallback = new RecordingDisposalLifetime(events, "CloseFallback");

        var lifetime = SystemGenesisRefreshAwareInteractionBridgeFactory.OpenPreferredOptionalRoute(
            static () => null,
            () => fallback);

        var route = Assert.IsAssignableFrom<IAsyncDisposable>(lifetime);
        await route.DisposeAsync();

        Assert.Equal(["CloseFallback"], events);
    }

    private sealed class RecordingBridgeFactory : IGenesisMainWindowInteractionBridgeFactory
    {
        public GenesisMainWindowInteraction? Interaction { get; private set; }

        public GenesisGuiNextTabsViewModel? GuiNextTabsDuringCreate { get; private set; }

        public IAsyncDisposable? Create(long windowToken, GenesisMainViewModel viewModel)
        {
            Interaction = new GenesisMainWindowInteraction(viewModel);
            GuiNextTabsDuringCreate =
                GenesisChannelRefreshInteractionScope.Find(viewModel)?.GuiNextTabs;
            return new RecordingLifetime();
        }
    }

    private sealed class RecordingLifetime : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingDisposalLifetime(
        List<string> events,
        string disposeEvent) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            events.Add(disposeEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SequencedCatalog : IGenesisChannelCatalog
    {
        private readonly Queue<GenesisChannelCatalogSnapshot> _snapshots;
        private int _readCount;

        public SequencedCatalog(params GenesisChannelCatalogSnapshot[] snapshots)
        {
            _snapshots = new(snapshots);
        }

        public int ReadCount => Volatile.Read(ref _readCount);

        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _readCount);
            return ValueTask.FromResult(_snapshots.Dequeue());
        }
    }
}
