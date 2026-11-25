namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelManualRefreshGuiNextTests
{
    [Fact]
    public async Task ManualRefreshProjectsNewlyObservedChannelIntoNewTab()
    {
        var first = new GenesisChannelItem(
            "First",
            "pcp://example/first",
            ChannelId: "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        var second = new GenesisChannelItem(
            "Second",
            "pcp://example/second",
            ChannelId: "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB");
        var catalog = new SequenceAggregatedCatalog(
            [first],
            [first, second]);
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());
        var publisher = new GenesisGuiNextChannelPresencePublisher(
            tabs,
            EmptyGenesisChannelPresenceNotificationPublisher.Instance);
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        await using var session = GenesisChannelCatalogSession.Create(
            catalog,
            viewModel,
            publisher);

        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await session.RefreshAsync());
        Assert.Same(first, Assert.Single(tabs.Channels));
        Assert.Empty(tabs.NewChannels);

        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await session.RefreshAsync());

        Assert.Equal(2, tabs.ChannelCount);
        Assert.Equal(1, tabs.NewChannelCount);
        Assert.DoesNotContain(tabs.NewChannels, channel => ReferenceEquals(channel, first));
        Assert.Same(second, Assert.Single(tabs.NewChannels));
    }

    private sealed class SequenceAggregatedCatalog : IGenesisAggregatedChannelCatalog
    {
        private static readonly string[] YellowPageIds = ["yp"];
        private readonly IReadOnlyList<GenesisChannelItem>[] _generations;
        private int _nextGeneration;

        public SequenceAggregatedCatalog(
            params IReadOnlyList<GenesisChannelItem>[] generations)
        {
            _generations = generations;
        }

        public ValueTask<GenesisAggregatedChannelCatalogSnapshot> ReadDetailedAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = Math.Min(
                Interlocked.Increment(ref _nextGeneration) - 1,
                _generations.Length - 1);
            var entries = _generations[index]
                .Select(static channel => new GenesisAggregatedChannelCatalogEntry(
                    channel,
                    Array.AsReadOnly(YellowPageIds)))
                .ToArray();
            return ValueTask.FromResult(new GenesisAggregatedChannelCatalogSnapshot(
                entries,
                [new GenesisYellowPageCatalogReadStatus(
                    new GenesisYellowPageDefinition(
                        "yp",
                        "YP",
                        new Uri("https://yp.example/index.txt")),
                    GenesisYellowPageCatalogReadState.Succeeded)]));
        }

        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "GUI Next manual refresh should use the detailed aggregate read.");
    }
}
