namespace PeerCast.Nova.App.Tests;

public sealed class SystemGenesisInteractiveMainWindowSurfaceTargetTests
{
    [Fact]
    public async Task SuccessfulSurfaceAndBridgeReturnOneCompositeLifetime()
    {
        var events = new List<string>();
        var surface = new RecordingSurfaceTarget(events);
        var bridgeFactory = new RecordingBridgeFactory(events);
        var target = new SystemGenesisInteractiveMainWindowSurfaceTarget(
            surface,
            bridgeFactory);
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());

        var lifetime = await target.OpenAsync(
            7144,
            viewModel,
            CancellationToken.None);

        Assert.NotNull(lifetime);
        Assert.Same(viewModel, surface.LastViewModel);
        Assert.Same(viewModel, bridgeFactory.LastViewModel);
        Assert.Equal(7144, bridgeFactory.LastWindowToken);

        await lifetime.DisposeAsync();

        Assert.Equal(["OpenSurface", "OpenBridge", "CloseBridge", "CloseSurface"], events);
    }

    [Fact]
    public async Task CatalogCleanupFailureStillClosesBridgeAndSurface()
    {
        var events = new List<string>();
        var catalogLifetime = new ThrowingRecordingLifetime(events, "CloseCatalog");
        var bridgeLifetime = new RecordingLifetime(events, "CloseBridge");
        var surfaceLifetime = new RecordingLifetime(events, "CloseSurface");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SystemGenesisInteractiveMainWindowSurfaceTarget.DisposeCompositeAsync(
                catalogLifetime,
                bridgeLifetime,
                surfaceLifetime).AsTask());

        Assert.Equal(["CloseCatalog", "CloseBridge", "CloseSurface"], events);
    }

    [Fact]
    public async Task BridgeCleanupFailureStillClosesSurface()
    {
        var events = new List<string>();
        var catalogLifetime = new RecordingLifetime(events, "CloseCatalog");
        var bridgeLifetime = new ThrowingRecordingLifetime(events, "CloseBridge");
        var surfaceLifetime = new RecordingLifetime(events, "CloseSurface");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SystemGenesisInteractiveMainWindowSurfaceTarget.DisposeCompositeAsync(
                catalogLifetime,
                bridgeLifetime,
                surfaceLifetime).AsTask());

        Assert.Equal(["CloseCatalog", "CloseBridge", "CloseSurface"], events);
    }

    [Fact]
    public async Task BridgeFailureClosesAlreadyOpenedSurface()
    {
        var events = new List<string>();
        var surface = new RecordingSurfaceTarget(events);
        var target = new SystemGenesisInteractiveMainWindowSurfaceTarget(
            surface,
            new RecordingBridgeFactory(events, reject: true));

        var lifetime = await target.OpenAsync(
            7144,
            new GenesisMainViewModel(new FakePlaybackSessionController()),
            CancellationToken.None);

        Assert.Null(lifetime);
        Assert.Equal(["OpenSurface", "OpenBridge", "CloseSurface"], events);
    }

    [Fact]
    public async Task BridgeRejectionCleanupFailureStillReturnsNull()
    {
        var events = new List<string>();
        var surface = new RecordingSurfaceTarget(events, disposeThrows: true);
        var target = new SystemGenesisInteractiveMainWindowSurfaceTarget(
            surface,
            new RecordingBridgeFactory(events, reject: true));

        var lifetime = await target.OpenAsync(
            7144,
            new GenesisMainViewModel(new FakePlaybackSessionController()),
            CancellationToken.None);

        Assert.Null(lifetime);
        Assert.Equal(["OpenSurface", "OpenBridge", "CloseSurface"], events);
    }

    [Fact]
    public async Task BridgeCreationFailureCleanupFailurePreservesOriginalException()
    {
        var events = new List<string>();
        var surface = new RecordingSurfaceTarget(events, disposeThrows: true);
        var target = new SystemGenesisInteractiveMainWindowSurfaceTarget(
            surface,
            new RecordingBridgeFactory(events, throwOnCreate: true));

        var error = await Assert.ThrowsAsync<NotSupportedException>(() =>
            target.OpenAsync(
                7144,
                new GenesisMainViewModel(new FakePlaybackSessionController()),
                CancellationToken.None).AsTask());

        Assert.Equal("Bridge creation failed.", error.Message);
        Assert.Equal(["OpenSurface", "OpenBridge", "CloseSurface"], events);
    }

    [Fact]
    public async Task SurfaceFailureDoesNotAttemptBridgeCreation()
    {
        var events = new List<string>();
        var target = new SystemGenesisInteractiveMainWindowSurfaceTarget(
            new RecordingSurfaceTarget(events, reject: true),
            new RecordingBridgeFactory(events));

        var lifetime = await target.OpenAsync(
            7144,
            new GenesisMainViewModel(new FakePlaybackSessionController()),
            CancellationToken.None);

        Assert.Null(lifetime);
        Assert.Equal(["OpenSurface"], events);
    }

    private sealed class RecordingSurfaceTarget : IGenesisMainWindowSurfaceTarget
    {
        private readonly List<string> _events;
        private readonly bool _reject;
        private readonly bool _disposeThrows;

        public RecordingSurfaceTarget(
            List<string> events,
            bool reject = false,
            bool disposeThrows = false)
        {
            _events = events;
            _reject = reject;
            _disposeThrows = disposeThrows;
        }

        public GenesisMainViewModel? LastViewModel { get; private set; }

        public ValueTask<IAsyncDisposable?> OpenAsync(
            long windowToken,
            GenesisMainViewModel viewModel,
            CancellationToken cancellationToken)
        {
            _events.Add("OpenSurface");
            LastViewModel = viewModel;
            IAsyncDisposable? lifetime = _reject
                ? null
                : _disposeThrows
                    ? new ThrowingRecordingLifetime(_events, "CloseSurface")
                    : new RecordingLifetime(_events, "CloseSurface");
            return ValueTask.FromResult(lifetime);
        }
    }

    private sealed class RecordingBridgeFactory : IGenesisMainWindowInteractionBridgeFactory
    {
        private readonly List<string> _events;
        private readonly bool _reject;
        private readonly bool _throwOnCreate;

        public RecordingBridgeFactory(
            List<string> events,
            bool reject = false,
            bool throwOnCreate = false)
        {
            _events = events;
            _reject = reject;
            _throwOnCreate = throwOnCreate;
        }

        public long LastWindowToken { get; private set; }

        public GenesisMainViewModel? LastViewModel { get; private set; }

        public IAsyncDisposable? Create(long windowToken, GenesisMainViewModel viewModel)
        {
            _events.Add("OpenBridge");
            LastWindowToken = windowToken;
            LastViewModel = viewModel;
            if (_throwOnCreate)
            {
                throw new NotSupportedException("Bridge creation failed.");
            }

            return _reject ? null : new RecordingLifetime(_events, "CloseBridge");
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

    private sealed class ThrowingRecordingLifetime : IAsyncDisposable
    {
        private readonly List<string> _events;
        private readonly string _closeEvent;

        public ThrowingRecordingLifetime(List<string> events, string closeEvent)
        {
            _events = events;
            _closeEvent = closeEvent;
        }

        public ValueTask DisposeAsync()
        {
            _events.Add(_closeEvent);
            throw new InvalidOperationException("Cleanup failed.");
        }
    }
}
