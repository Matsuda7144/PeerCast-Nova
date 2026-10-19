namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelCatalogSanitizationIntegrationTests
{
    [Fact]
    public async Task RefreshSanitizesExternalRowsBeforeViewModelProjection()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var catalog = new StaticCatalog(new([
            new GenesisChannelItem(
                "  Example  ",
                "  pcp://example/channel  ",
                "  [game - example]  ",
                -1,
                4,
                1800,
                TimeSpan.FromMinutes(42),
                "  FLV  "),
            new GenesisChannelItem(
                "Duplicate",
                "pcp://example/channel"),
            new GenesisChannelItem(
                "Invalid",
                "   "),
        ]));
        var coordinator = new GenesisChannelCatalogCoordinator(catalog, viewModel);

        var result = await coordinator.RefreshAsync();

        Assert.Equal(GenesisChannelCatalogRefreshResult.Completed, result);
        var channel = Assert.Single(viewModel.Channels);
        Assert.Equal("Example", channel.Name);
        Assert.Equal("pcp://example/channel", channel.Source);
        Assert.Equal("[game - example]", channel.Description);
        Assert.Null(channel.Listeners);
        Assert.Equal(4, channel.Relays);
        Assert.Equal(1800, channel.BitrateKbps);
        Assert.Equal(TimeSpan.FromMinutes(42), channel.Uptime);
        Assert.Equal("FLV", channel.Type);
        Assert.Null(viewModel.ChannelCatalogErrorMessage);
        Assert.Equal("Ready", viewModel.StatusText);
    }

    private sealed class StaticCatalog : IGenesisChannelCatalog
    {
        private readonly GenesisChannelCatalogSnapshot _snapshot;

        public StaticCatalog(GenesisChannelCatalogSnapshot snapshot)
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
}
