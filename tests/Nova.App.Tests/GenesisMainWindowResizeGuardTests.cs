namespace PeerCast.Nova.App.Tests;

public sealed class GenesisMainWindowResizeGuardTests
{
    [Fact]
    public void ResizableStyleAddsOnlyResizeCapabilities()
    {
        const int overlappedWindowStyle = 0x00CF0000;
        const int resizeMask =
            GenesisMainWindowResizeGuard.WindowStyleThickFrame |
            GenesisMainWindowResizeGuard.WindowStyleMaximizeBox;

        var resizableStyle = GenesisMainWindowResizeGuard.ResolveResizableStyle(
            overlappedWindowStyle & ~resizeMask);

        Assert.Equal(overlappedWindowStyle, resizableStyle);
        Assert.Equal(0x00020000, resizableStyle & 0x00020000); // Keep minimize.
    }

    [Fact]
    public async Task InteractiveSurfaceRestoresResizeAfterSurfaceCleanup()
    {
        var events = new List<string>();
        var surface = new RecordingSurfaceTarget(events);
        var bridge = new RecordingBridgeFactory(events);
        var target = new SystemGenesisInteractiveMainWindowSurfaceTarget(
            surface,
            bridge,
            EmptyGenesisChannelCatalog.Instance,
            windowToken =>
            {
                Assert.Equal(7144, windowToken);
                events.Add("LockResize");
                return new RecordingLifetime(events, "UnlockResize");
            });

        var lifetime = await target.OpenAsync(
            7144,
            new GenesisMainViewModel(new FakePlaybackSessionController()),
            CancellationToken.None);

        Assert.NotNull(lifetime);
        Assert.Equal(["OpenSurface", "LockResize", "OpenBridge"], events);

        await lifetime.DisposeAsync();

        Assert.Equal(
            [
                "OpenSurface",
                "LockResize",
                "OpenBridge",
                "CloseBridge",
                "CloseSurface",
                "UnlockResize",
            ],
            events);
    }

    [Fact]
    public async Task ResizeGuardRejectionClosesSurfaceBeforeBridgeCreation()
    {
        var events = new List<string>();
        var surface = new RecordingSurfaceTarget(events);
        var bridge = new RecordingBridgeFactory(events);
        var target = new SystemGenesisInteractiveMainWindowSurfaceTarget(
            surface,
            bridge,
            EmptyGenesisChannelCatalog.Instance,
            _ =>
            {
                events.Add("LockResize");
                return null;
            });

        var lifetime = await target.OpenAsync(
            7144,
            new GenesisMainViewModel(new FakePlaybackSessionController()),
            CancellationToken.None);

        Assert.Null(lifetime);
        Assert.Equal(["OpenSurface", "LockResize", "CloseSurface"], events);
        Assert.Equal(0, bridge.CreateCount);
    }

    private sealed class RecordingSurfaceTarget : IGenesisMainWindowSurfaceTarget
    {
        private readonly List<string> _events;

        public RecordingSurfaceTarget(List<string> events)
        {
            _events = events;
        }

        public ValueTask<IAsyncDisposable?> OpenAsync(
            long windowToken,
            GenesisMainViewModel viewModel,
            CancellationToken cancellationToken)
        {
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

        public int CreateCount { get; private set; }

        public IAsyncDisposable? Create(long windowToken, GenesisMainViewModel viewModel)
        {
            CreateCount++;
            _events.Add("OpenBridge");
            return new RecordingLifetime(_events, "CloseBridge");
        }
    }

    private sealed class RecordingLifetime : IAsyncDisposable
    {
        private readonly List<string> _events;
        private readonly string _closeEvent;

        public RecordingLifetime(List<string> events, string closeEvent)
        {
            _events = events;
            _closeEvent = closeEvent;
        }

        public ValueTask DisposeAsync()
        {
            _events.Add(_closeEvent);
            return ValueTask.CompletedTask;
        }
    }
}
