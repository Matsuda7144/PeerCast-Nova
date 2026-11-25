namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelCatalogNotificationSessionTests
{
    [Fact]
    public async Task InjectedPublisherReceivesCommittedPresenceSnapshot()
    {
        var channel = new GenesisChannelItem(
            "Channel",
            "pcp://example/channel",
            ChannelId: "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC");
        var catalog = new SingleAggregatedCatalog(channel);
        var publisher = new RecordingPublisher();
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        await using var session = GenesisChannelCatalogSession.Create(
            catalog,
            viewModel,
            publisher);

        var result = await session.RefreshAsync();

        Assert.Equal(GenesisChannelCatalogRefreshResult.Completed, result);
        Assert.Equal(1, publisher.PublishCount);
        Assert.Empty(publisher.LastPresence!.Transitions);
        Assert.Same(channel, Assert.Single(publisher.LastPresence.Active).Channel);
        Assert.Same(channel, Assert.Single(viewModel.Channels));
    }

    private sealed class SingleAggregatedCatalog : IGenesisAggregatedChannelCatalog
    {
        private static readonly string[] YellowPageIds = ["yp"];
        private readonly GenesisChannelItem _channel;

        public SingleAggregatedCatalog(GenesisChannelItem channel)
        {
            _channel = channel;
        }

        public ValueTask<GenesisAggregatedChannelCatalogSnapshot> ReadDetailedAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new GenesisAggregatedChannelCatalogSnapshot(
                [new GenesisAggregatedChannelCatalogEntry(
                    _channel,
                    Array.AsReadOnly(YellowPageIds))],
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
                "Session should use the detailed aggregate read.");
    }

    private sealed class RecordingPublisher :
        IGenesisChannelPresenceNotificationPublisher
    {
        public int PublishCount { get; private set; }

        public GenesisChannelPresenceSnapshot? LastPresence { get; private set; }

        public ValueTask PublishAsync(
            GenesisChannelPresenceSnapshot presence,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PublishCount++;
            LastPresence = presence;
            return ValueTask.CompletedTask;
        }
    }
}
