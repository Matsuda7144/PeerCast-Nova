namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelCatalogTests
{
    [Fact]
    public async Task RefreshReplacesChannelsFromCatalog()
    {
        var channels = new[]
        {
            new GenesisChannelItem(
                "配信A",
                "pcp://example/a",
                "[game - example]",
                12,
                4,
                2_000,
                TimeSpan.FromMinutes(75),
                "FLV"),
        };
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var coordinator = new GenesisChannelCatalogCoordinator(
            new RecordingCatalog(new(channels)),
            viewModel);

        var result = await coordinator.RefreshAsync();

        Assert.Equal(GenesisChannelCatalogRefreshResult.Completed, result);
        Assert.Single(viewModel.Channels);
        Assert.Equal("配信A", viewModel.Channels[0].Name);
        Assert.Equal("pcp://example/a", viewModel.Channels[0].Source);
    }

    [Fact]
    public async Task RefreshPreservesSelectionBySource()
    {
        var selected = new GenesisChannelItem("Before", "pcp://example/a");
        var replacement = new GenesisChannelItem("After", "pcp://example/a");
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([selected]);
        viewModel.SelectedChannel = selected;
        var coordinator = new GenesisChannelCatalogCoordinator(
            new RecordingCatalog(new([replacement])),
            viewModel);

        var result = await coordinator.RefreshAsync();

        Assert.Equal(GenesisChannelCatalogRefreshResult.Completed, result);
        Assert.Same(replacement, viewModel.SelectedChannel);
        Assert.Equal("pcp://example/a", viewModel.SourceText);
    }

    [Fact]
    public async Task CancellationLeavesExistingChannelsUntouched()
    {
        var existing = new GenesisChannelItem("Existing", "pcp://example/existing");
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([existing]);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var coordinator = new GenesisChannelCatalogCoordinator(
            EmptyGenesisChannelCatalog.Instance,
            viewModel);

        var result = await coordinator.RefreshAsync(cancellationSource.Token);

        Assert.Equal(GenesisChannelCatalogRefreshResult.Cancelled, result);
        Assert.Single(viewModel.Channels);
        Assert.Same(existing, viewModel.Channels[0]);
    }

    [Fact]
    public async Task CatalogFailureLeavesExistingChannelsUntouched()
    {
        var existing = new GenesisChannelItem("Existing", "pcp://example/existing");
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([existing]);
        var coordinator = new GenesisChannelCatalogCoordinator(
            new ThrowingCatalog(),
            viewModel);

        var result = await coordinator.RefreshAsync();

        Assert.Equal(GenesisChannelCatalogRefreshResult.Faulted, result);
        Assert.Single(viewModel.Channels);
        Assert.Same(existing, viewModel.Channels[0]);
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

    private sealed class ThrowingCatalog : IGenesisChannelCatalog
    {
        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<GenesisChannelCatalogSnapshot>(
                new InvalidOperationException("catalog failed"));
    }
}
