namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelCatalogCoordinatorTests
{
    [Fact]
    public async Task RefreshProjectsCatalogChannelsIntoViewModel()
    {
        var channels = new[]
        {
            new GenesisChannelItem(
                "Example A",
                "pcp://example/a",
                "[game - example]",
                12,
                3,
                1800,
                TimeSpan.FromMinutes(95),
                "FLV"),
            new GenesisChannelItem(
                "Example B",
                "pcp://example/b",
                "[talk - example]",
                7,
                2,
                1200,
                TimeSpan.FromMinutes(22),
                "FLV"),
        };
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var coordinator = new GenesisChannelCatalogCoordinator(
            new RecordingCatalog(new(channels)),
            viewModel);

        var result = await coordinator.RefreshAsync();

        Assert.Equal(GenesisChannelCatalogRefreshResult.Completed, result);
        Assert.Equal(2, viewModel.Channels.Count);
        Assert.Equal("Example A", viewModel.Channels[0].Name);
        Assert.Equal("pcp://example/b", viewModel.Channels[1].Source);
        Assert.False(viewModel.IsChannelCatalogRefreshing);
        Assert.Null(viewModel.ChannelCatalogErrorMessage);
        Assert.Equal("Ready", viewModel.StatusText);
    }

    [Fact]
    public async Task PendingRefreshPublishesLoadingStateUntilCatalogCompletes()
    {
        var catalog = new PendingCatalog();
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var coordinator = new GenesisChannelCatalogCoordinator(catalog, viewModel);

        var refresh = coordinator.RefreshAsync().AsTask();
        await catalog.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(viewModel.IsChannelCatalogRefreshing);
        Assert.Null(viewModel.ChannelCatalogErrorMessage);
        Assert.Equal("Loading channels...", viewModel.StatusText);

        catalog.Complete(GenesisChannelCatalogSnapshot.Empty);
        var result = await refresh;

        Assert.Equal(GenesisChannelCatalogRefreshResult.Completed, result);
        Assert.False(viewModel.IsChannelCatalogRefreshing);
        Assert.Null(viewModel.ChannelCatalogErrorMessage);
        Assert.Equal("Ready", viewModel.StatusText);
    }

    [Fact]
    public async Task CancelledRefreshPreservesExistingChannels()
    {
        var existing = new GenesisChannelItem(
            "Existing",
            "pcp://example/existing");
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([existing]);
        var coordinator = new GenesisChannelCatalogCoordinator(
            new CancellingCatalog(),
            viewModel);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        var result = await coordinator.RefreshAsync(cancellationSource.Token);

        Assert.Equal(GenesisChannelCatalogRefreshResult.Cancelled, result);
        Assert.Single(viewModel.Channels);
        Assert.Same(existing, viewModel.Channels[0]);
        Assert.False(viewModel.IsChannelCatalogRefreshing);
        Assert.Null(viewModel.ChannelCatalogErrorMessage);
        Assert.Equal("Ready", viewModel.StatusText);
    }

    [Fact]
    public async Task CancellationAfterCatalogReadPreservesExistingChannels()
    {
        var existing = new GenesisChannelItem(
            "Existing",
            "pcp://example/existing");
        var replacement = new GenesisChannelItem(
            "Replacement",
            "pcp://example/replacement");
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([existing]);
        using var cancellationSource = new CancellationTokenSource();
        var coordinator = new GenesisChannelCatalogCoordinator(
            new CancellingAfterReadCatalog(
                new GenesisChannelCatalogSnapshot([replacement]),
                cancellationSource),
            viewModel);

        var result = await coordinator.RefreshAsync(cancellationSource.Token);

        Assert.Equal(GenesisChannelCatalogRefreshResult.Cancelled, result);
        Assert.Single(viewModel.Channels);
        Assert.Same(existing, viewModel.Channels[0]);
        Assert.False(viewModel.IsChannelCatalogRefreshing);
        Assert.Null(viewModel.ChannelCatalogErrorMessage);
        Assert.Equal("Ready", viewModel.StatusText);
    }

    [Fact]
    public async Task FaultedRefreshPreservesExistingChannelsAndPublishesStatus()
    {
        var existing = new GenesisChannelItem(
            "Existing",
            "pcp://example/existing");
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([existing]);
        var coordinator = new GenesisChannelCatalogCoordinator(
            new FaultingCatalog(),
            viewModel);

        var result = await coordinator.RefreshAsync();

        Assert.Equal(GenesisChannelCatalogRefreshResult.Faulted, result);
        Assert.Single(viewModel.Channels);
        Assert.Same(existing, viewModel.Channels[0]);
        Assert.False(viewModel.IsChannelCatalogRefreshing);
        Assert.Equal("Channel refresh failed.", viewModel.ChannelCatalogErrorMessage);
        Assert.Equal("Channel refresh failed.", viewModel.StatusText);
    }

    [Fact]
    public async Task EmptyCatalogClearsPreviouslyProjectedChannels()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([
            new GenesisChannelItem("Existing", "pcp://example/existing"),
        ]);
        var coordinator = new GenesisChannelCatalogCoordinator(
            EmptyGenesisChannelCatalog.Instance,
            viewModel);

        var result = await coordinator.RefreshAsync();

        Assert.Equal(GenesisChannelCatalogRefreshResult.Completed, result);
        Assert.Empty(viewModel.Channels);
    }

    private sealed class RecordingCatalog : IGenesisChannelCatalog
    {
        private readonly GenesisChannelCatalogSnapshot _snapshot;

        public RecordingCatalog(GenesisChannelCatalogSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_snapshot);
        }
    }

    private sealed class PendingCatalog : IGenesisChannelCatalog
    {
        private readonly TaskCompletionSource<GenesisChannelCatalogSnapshot> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return await _completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public void Complete(GenesisChannelCatalogSnapshot snapshot) =>
            _completion.TrySetResult(snapshot);
    }

    private sealed class CancellingCatalog : IGenesisChannelCatalog
    {
        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromCanceled<GenesisChannelCatalogSnapshot>(cancellationToken);
    }

    private sealed class CancellingAfterReadCatalog : IGenesisChannelCatalog
    {
        private readonly GenesisChannelCatalogSnapshot _snapshot;
        private readonly CancellationTokenSource _cancellationSource;

        public CancellingAfterReadCatalog(
            GenesisChannelCatalogSnapshot snapshot,
            CancellationTokenSource cancellationSource)
        {
            _snapshot = snapshot;
            _cancellationSource = cancellationSource;
        }

        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _cancellationSource.Cancel();
            return ValueTask.FromResult(_snapshot);
        }
    }

    private sealed class FaultingCatalog : IGenesisChannelCatalog
    {
        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<GenesisChannelCatalogSnapshot>(
                new InvalidOperationException("Catalog failed."));
    }
}
