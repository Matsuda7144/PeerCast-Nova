namespace PeerCast.Nova.App.Tests;

public sealed class SystemGenesisInteractiveNotificationRuntimeTests
{
    [Fact]
    public async Task NotificationRuntimeUsesWindowTokenAndSharesSurfaceLifetime()
    {
        var events = new List<string>();
        long notificationWindowToken = 0;
        var target = new SystemGenesisInteractiveMainWindowSurfaceTarget(
            new RecordingSurface(events),
            new RecordingBridgeFactory(events),
            EmptyGenesisChannelCatalog.Instance,
            _ =>
            {
                events.Add("OpenResize");
                return new RecordingLifetime(events, "CloseResize");
            },
            windowToken =>
            {
                events.Add("OpenNotification");
                notificationWindowToken = windowToken;
                return new GenesisChannelNotificationRuntime(
                    EmptyGenesisChannelPresenceNotificationPublisher.Instance,
                    new RecordingLifetime(events, "CloseNotification"));
            });

        var lifetime = await target.OpenAsync(
            7144,
            new GenesisMainViewModel(new FakePlaybackSessionController()),
            CancellationToken.None);

        Assert.NotNull(lifetime);
        Assert.Equal(7144, notificationWindowToken);

        await lifetime.DisposeAsync();

        Assert.Equal(
            [
                "OpenSurface",
                "OpenResize",
                "OpenNotification",
                "OpenBridge",
                "CloseNotification",
                "CloseBridge",
                "CloseSurface",
                "CloseResize",
            ],
            events);
    }

    [Fact]
    public async Task NotificationRuntimeFactoryFailureDoesNotRejectGuiSurface()
    {
        var events = new List<string>();
        var target = new SystemGenesisInteractiveMainWindowSurfaceTarget(
            new RecordingSurface(events),
            new RecordingBridgeFactory(events),
            EmptyGenesisChannelCatalog.Instance,
            _ => new RecordingLifetime(events, "CloseResize"),
            _ => throw new InvalidOperationException("Notification initialization failed."));

        var lifetime = await target.OpenAsync(
            7144,
            new GenesisMainViewModel(new FakePlaybackSessionController()),
            CancellationToken.None);

        Assert.NotNull(lifetime);
        await lifetime.DisposeAsync();
        Assert.Equal(
            ["OpenSurface", "OpenBridge", "CloseBridge", "CloseSurface", "CloseResize"],
            events);
    }

    [Fact]
    public async Task GuiNextNotificationRuntimeRetainsTabProjectionWhenNativeInitializationFails()
    {
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());

        var runtime = SystemGenesisGuiNextNotificationRuntime.Create(
            tabs,
            static () => throw new InvalidOperationException(
                "Native notification initialization failed."));

        var publisher = Assert.IsType<GenesisGuiNextChannelPresencePublisher>(
            runtime.Publisher);
        Assert.Same(tabs, publisher.Tabs);

        await runtime.Lifetime.DisposeAsync();
    }

    [Fact]
    public async Task GuiNextNotificationRuntimeRetainsNativeLifetimeWhenInitializationSucceeds()
    {
        var events = new List<string>();
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());
        var nativeRuntime = new GenesisChannelNotificationRuntime(
            EmptyGenesisChannelPresenceNotificationPublisher.Instance,
            new RecordingLifetime(events, "CloseNativeNotification"));

        var runtime = SystemGenesisGuiNextNotificationRuntime.Create(
            tabs,
            () => nativeRuntime);

        var publisher = Assert.IsType<GenesisGuiNextChannelPresencePublisher>(
            runtime.Publisher);
        Assert.Same(tabs, publisher.Tabs);

        await runtime.Lifetime.DisposeAsync();
        Assert.Equal(["CloseNativeNotification"], events);
    }

    private sealed class RecordingSurface : IGenesisMainWindowSurfaceTarget
    {
        private readonly List<string> _events;

        public RecordingSurface(List<string> events)
        {
            _events = events;
        }

        public ValueTask<IAsyncDisposable?> OpenAsync(
            long windowToken,
            GenesisMainViewModel viewModel,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _events.Add("OpenSurface");
            return ValueTask.FromResult<IAsyncDisposable?>(
                new RecordingLifetime(_events, "CloseSurface"));
        }
    }

    private sealed class RecordingBridgeFactory : IGenesisMainWindowInteractionBridgeFactory
    {
        private readonly List<string> _events;

        public RecordingBridgeFactory(List<string> events)
        {
            _events = events;
        }

        public IAsyncDisposable? Create(long windowToken, GenesisMainViewModel viewModel)
        {
            _events.Add("OpenBridge");
            return new RecordingLifetime(_events, "CloseBridge");
        }
    }

    private sealed class RecordingLifetime : IAsyncDisposable
    {
        private readonly List<string> _events;
        private readonly string _eventName;

        public RecordingLifetime(List<string> events, string eventName)
        {
            _events = events;
            _eventName = eventName;
        }

        public ValueTask DisposeAsync()
        {
            _events.Add(_eventName);
            return ValueTask.CompletedTask;
        }
    }
}
