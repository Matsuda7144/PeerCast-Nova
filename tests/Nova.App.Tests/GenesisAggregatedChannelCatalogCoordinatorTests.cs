namespace PeerCast.Nova.App.Tests;

public sealed class GenesisAggregatedChannelCatalogCoordinatorTests
{
    [Fact]
    public async Task TransientAbsenceRemainsProjectedUntilThirdProvenMiss()
    {
        var channel = Channel("Initial");
        var catalog = new SequencedAggregatedCatalog();
        catalog.Enqueue(Snapshot(
            [Entry(channel, "yp")],
            [Status("yp", GenesisYellowPageCatalogReadState.Succeeded)]));
        catalog.Enqueue(EmptySucceeded("yp"));
        catalog.Enqueue(EmptySucceeded("yp"));
        catalog.Enqueue(EmptySucceeded("yp"));
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var coordinator = new GenesisChannelCatalogCoordinator(catalog, viewModel);

        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await coordinator.RefreshAsync());
        Assert.Same(channel, Assert.Single(viewModel.Channels));

        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await coordinator.RefreshAsync());
        Assert.Same(channel, Assert.Single(viewModel.Channels));
        Assert.Equal(
            1,
            Assert.Single(coordinator.PresenceSnapshot!.Active)
                .ConsecutiveMissingSuccessfulRefreshes);

        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await coordinator.RefreshAsync());
        Assert.Same(channel, Assert.Single(viewModel.Channels));
        Assert.Equal(
            2,
            Assert.Single(coordinator.PresenceSnapshot!.Active)
                .ConsecutiveMissingSuccessfulRefreshes);

        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await coordinator.RefreshAsync());
        Assert.Empty(viewModel.Channels);
        var endedPresence = coordinator.PresenceSnapshot!;
        Assert.Empty(endedPresence.Active);
        Assert.Equal(
            GenesisChannelPresenceState.Ended,
            Assert.Single(endedPresence.Entries).State);
        Assert.Equal(0, catalog.FlatReadCount);
    }

    [Fact]
    public async Task FailedSupplyingYellowPageDoesNotAgeProjectedChannel()
    {
        var channel = Channel("Initial");
        var catalog = new SequencedAggregatedCatalog();
        catalog.Enqueue(Snapshot(
            [Entry(channel, "first")],
            [Status("first", GenesisYellowPageCatalogReadState.Succeeded)]));
        catalog.Enqueue(Snapshot(
            [],
            [
                Status("first", GenesisYellowPageCatalogReadState.Failed),
                Status("second", GenesisYellowPageCatalogReadState.Succeeded),
            ]));
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var coordinator = new GenesisChannelCatalogCoordinator(catalog, viewModel);

        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await coordinator.RefreshAsync());
        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await coordinator.RefreshAsync());

        Assert.Same(channel, Assert.Single(viewModel.Channels));
        var active = Assert.Single(coordinator.PresenceSnapshot!.Active);
        Assert.False(active.IsCurrentlyObserved);
        Assert.Equal(0, active.ConsecutiveMissingSuccessfulRefreshes);
    }

    [Fact]
    public async Task CancelledDetailedRefreshDoesNotAdvancePresence()
    {
        var channel = Channel("Initial");
        var catalog = new SequencedAggregatedCatalog();
        catalog.Enqueue(Snapshot(
            [Entry(channel, "yp")],
            [Status("yp", GenesisYellowPageCatalogReadState.Succeeded)]));
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var coordinator = new GenesisChannelCatalogCoordinator(catalog, viewModel);

        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await coordinator.RefreshAsync());
        var before = coordinator.PresenceSnapshot;
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        var result = await coordinator.RefreshAsync(cancellationSource.Token);

        Assert.Equal(GenesisChannelCatalogRefreshResult.Cancelled, result);
        Assert.Same(before, coordinator.PresenceSnapshot);
        Assert.Same(channel, Assert.Single(viewModel.Channels));
    }

    private static GenesisChannelItem Channel(string name) =>
        new(
            name,
            "pcp://example/channel",
            ChannelId: "0123456789ABCDEF0123456789ABCDEF");

    private static GenesisAggregatedChannelCatalogEntry Entry(
        GenesisChannelItem channel,
        string yellowPageId) =>
        new(
            channel,
            Array.AsReadOnly(new[] { yellowPageId }));

    private static GenesisYellowPageCatalogReadStatus Status(
        string id,
        GenesisYellowPageCatalogReadState state) =>
        new(
            new GenesisYellowPageDefinition(
                id,
                id,
                new Uri($"https://{id}.example/index.txt")),
            state);

    private static GenesisAggregatedChannelCatalogSnapshot Snapshot(
        GenesisAggregatedChannelCatalogEntry[] entries,
        GenesisYellowPageCatalogReadStatus[] statuses) =>
        new(entries, statuses);

    private static GenesisAggregatedChannelCatalogSnapshot EmptySucceeded(
        string yellowPageId) =>
        Snapshot(
            [],
            [Status(
                yellowPageId,
                GenesisYellowPageCatalogReadState.Succeeded)]);

    private sealed class SequencedAggregatedCatalog : IGenesisAggregatedChannelCatalog
    {
        private readonly Queue<GenesisAggregatedChannelCatalogSnapshot> _snapshots = [];

        public int FlatReadCount { get; private set; }

        public void Enqueue(GenesisAggregatedChannelCatalogSnapshot snapshot)
        {
            _snapshots.Enqueue(snapshot);
        }

        public ValueTask<GenesisAggregatedChannelCatalogSnapshot> ReadDetailedAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_snapshots.Dequeue());
        }

        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            FlatReadCount++;
            throw new InvalidOperationException(
                "Coordinator should use the detailed aggregate read.");
        }
    }
}
