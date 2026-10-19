namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelCatalogInteractiveSurfaceTests
{
    [Fact]
    public async Task CatalogRefreshStartsAfterVisibleSurfaceAndBridge()
    {
        var events = new List<string>();
        var channel = new GenesisChannelItem(
            "Example",
            "pcp://example/channel",
            "[game - example]",
            10,
            2,
            1800,
            TimeSpan.FromMinutes(42),
            "FLV");
        var target = new SystemGenesisInteractiveMainWindowSurfaceTarget(
            new RecordingSurfaceTarget(events),
            new RecordingBridgeFactory(events),
            new RecordingCatalog(events, new([channel])),
            CreateNoopResizeGuard);
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());

        var lifetime = await target.OpenAsync(
            7144,
            viewModel,
            CancellationToken.None);

        Assert.NotNull(lifetime);
        Assert.Equal(["OpenSurface", "OpenBridge", "ReadCatalog"], events);
        Assert.Single(viewModel.Channels);
        Assert.Same(channel, viewModel.Channels[0]);

        await lifetime.DisposeAsync();

        Assert.Equal(
            ["OpenSurface", "OpenBridge", "ReadCatalog", "CloseBridge", "CloseSurface"],
            events);
    }

    [Fact]
    public async Task CatalogFailureDoesNotRejectAlreadyVisibleGui()
    {
        var events = new List<string>();
        var target = new SystemGenesisInteractiveMainWindowSurfaceTarget(
            new RecordingSurfaceTarget(events),
            new RecordingBridgeFactory(events),
            new FaultingCatalog(events),
            CreateNoopResizeGuard);
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());

        var lifetime = await target.OpenAsync(
            7144,
            viewModel,
            CancellationToken.None);

        Assert.NotNull(lifetime);
        Assert.Equal(["OpenSurface", "OpenBridge", "ReadCatalog"], events);
        Assert.Empty(viewModel.Channels);
        await lifetime.DisposeAsync();
    }

    [Fact]
    public async Task DisposeCancelsPendingCatalogRefreshBeforeClosingGui()
    {
        var events = new List<string>();
        var catalog = new PendingCatalog(events);
        var target = new SystemGenesisInteractiveMainWindowSurfaceTarget(
            new RecordingSurfaceTarget(events),
            new RecordingBridgeFactory(events),
            catalog,
            CreateNoopResizeGuard);

        var lifetime = await target.OpenAsync(
            7144,
            new GenesisMainViewModel(new FakePlaybackSessionController()),
            CancellationToken.None);
        Assert.NotNull(lifetime);
        await catalog.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await lifetime.DisposeAsync();

        Assert.True(catalog.Cancelled);
        Assert.Equal(
            [
                "OpenSurface",
                "OpenBridge",
                "ReadCatalog",
                "CancelCatalog",
                "CloseBridge",
                "CloseSurface",
            ],
            events);
    }

    [Fact]
    public async Task BridgeFailureClosesAlreadyVisibleSurface()
    {
        var events = new List<string>();
        var target = new SystemGenesisInteractiveMainWindowSurfaceTarget(
            new RecordingSurfaceTarget(events),
            new FaultingBridgeFactory(events),
            new RecordingCatalog(events, GenesisChannelCatalogSnapshot.Empty),
            CreateNoopResizeGuard);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            _ = await target.OpenAsync(
                7144,
                new GenesisMainViewModel(new FakePlaybackSessionController()),
                CancellationToken.None);
        });

        Assert.Equal(["OpenSurface", "OpenBridge", "CloseSurface"], events);
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

        public IAsyncDisposable? Create(long windowToken, GenesisMainViewModel viewModel)
        {
            _events.Add("OpenBridge");
            return new RecordingLifetime(_events, "CloseBridge");
        }
    }

    private sealed class FaultingBridgeFactory : IGenesisMainWindowInteractionBridgeFactory
    {
        private readonly List<string> _events;

        public FaultingBridgeFactory(List<string> events)
        {
            _events = events;
        }

        public IAsyncDisposable? Create(long windowToken, GenesisMainViewModel viewModel)
        {
            _events.Add("OpenBridge");
            throw new InvalidOperationException("Bridge failed.");
        }
    }

    private sealed class RecordingCatalog : IGenesisChannelCatalog
    {
        private readonly List<string> _events;
        private readonly GenesisChannelCatalogSnapshot _snapshot;

        public RecordingCatalog(
            List<string> events,
            GenesisChannelCatalogSnapshot snapshot)
        {
            _events = events;
            _snapshot = snapshot;
        }

        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            _events.Add("ReadCatalog");
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_snapshot);
        }
    }

    private sealed class FaultingCatalog : IGenesisChannelCatalog
    {
        private readonly List<string> _events;

        public FaultingCatalog(List<string> events)
        {
            _events = events;
        }

        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            _events.Add("ReadCatalog");
            return ValueTask.FromException<GenesisChannelCatalogSnapshot>(
                new InvalidOperationException("Catalog failed."));
        }
    }

    private sealed class PendingCatalog : IGenesisChannelCatalog
    {
        private readonly List<string> _events;

        public PendingCatalog(List<string> events)
        {
            _events = events;
        }

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Cancelled { get; private set; }

        public async ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            _events.Add("ReadCatalog");
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                    .ConfigureAwait(false);
                return GenesisChannelCatalogSnapshot.Empty;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Cancelled = true;
                _events.Add("CancelCatalog");
                throw;
            }
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

    private static NoopLifetime? CreateNoopResizeGuard(long _) =>
        NoopLifetime.Instance;

    private sealed class NoopLifetime : IAsyncDisposable
    {
        public static NoopLifetime Instance { get; } = new();

        private NoopLifetime()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
