namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelNotificationCatalogIntegrationTests
{
    private static readonly string[] YellowPageIds = ["yp"];

    [Fact]
    public async Task AggregatedRefreshDispatchesActivationWithoutMetadataRepeat()
    {
        const string channelId = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var initial = new GenesisChannelItem(
            "Initial",
            "pcp://first.example/channel",
            ChannelId: channelId);
        var updated = new GenesisChannelItem(
            "Updated",
            "pcp://second.example/channel",
            Description: "metadata",
            ChannelId: channelId);
        var catalog = new SequencedAggregatedCatalog();
        catalog.Enqueue(Snapshot(initial));
        catalog.Enqueue(Snapshot(updated));
        var presenter = new RecordingPresenter();
        var soundPlayer = new RecordingSoundPlayer();
        var publisher = new GenesisChannelNotificationPublisher(
            new GenesisFavoriteChannelCollection(),
            static () => GenesisChannelNotificationPreferences.Default,
            new GenesisChannelNotificationDispatcher(presenter, soundPlayer));
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var coordinator = new GenesisChannelCatalogCoordinator(
            catalog,
            viewModel,
            publisher);

        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await coordinator.RefreshAsync());
        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await coordinator.RefreshAsync());

        var notification = Assert.Single(presenter.Notifications);
        Assert.Same(initial, notification.Channel);
        Assert.Equal(1, soundPlayer.PlayCount);
        Assert.Same(updated, Assert.Single(viewModel.Channels));
        Assert.Empty(coordinator.PresenceSnapshot!.Transitions);
    }

    [Fact]
    public async Task SameChannelAcrossYellowPagesDispatchesSingleActivationNotification()
    {
        const string channelId = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";
        var firstChannel = new GenesisChannelItem(
            "Shared from first",
            "pcp://first.example/channel",
            ChannelId: channelId);
        var secondChannel = new GenesisChannelItem(
            "Shared from second",
            "pcp://second.example/channel",
            ChannelId: channelId);
        var first = Definition("first");
        var second = Definition("second");
        var catalog = new GenesisAggregatingYellowPageChannelCatalog(
            new GenesisYellowPageConfiguration([first, second]),
            new FixedCatalogFactory(new Dictionary<string, IGenesisChannelCatalog>
            {
                ["first"] = new FixedCatalog(new([firstChannel])),
                ["second"] = new FixedCatalog(new([secondChannel])),
            }));
        var presenter = new RecordingPresenter();
        var soundPlayer = new RecordingSoundPlayer();
        var publisher = new GenesisChannelNotificationPublisher(
            new GenesisFavoriteChannelCollection(),
            static () => GenesisChannelNotificationPreferences.Default,
            new GenesisChannelNotificationDispatcher(presenter, soundPlayer));
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var coordinator = new GenesisChannelCatalogCoordinator(
            catalog,
            viewModel,
            publisher);

        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await coordinator.RefreshAsync());

        var expected = firstChannel with { YellowPage = "first, second" };
        var notification = Assert.Single(presenter.Notifications);
        Assert.Equal(expected, notification.Channel);
        Assert.Equal(
            GenesisChannelNotificationReason.NewChannel,
            notification.Reasons);
        Assert.Equal(1, soundPlayer.PlayCount);
        Assert.Equal(expected, Assert.Single(viewModel.Channels));

        var transition = Assert.Single(coordinator.PresenceSnapshot!.Transitions);
        Assert.Equal(
            GenesisChannelPresenceTransitionKind.BecameActive,
            transition.Kind);
        Assert.Collection(
            transition.Entry.KnownYellowPageIds,
            id => Assert.Equal("first", id),
            id => Assert.Equal("second", id));
    }

    [Fact]
    public async Task FailedSupplyingYellowPageRecoveryDoesNotDispatchReactivationNotification()
    {
        const string channelId = "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD";
        var channel = new GenesisChannelItem(
            "Recovered",
            "pcp://first.example/channel",
            ChannelId: channelId);
        var first = Definition("first");
        var second = Definition("second");
        var firstCatalog = new SequencedCatalog();
        firstCatalog.Enqueue(new([channel]));
        firstCatalog.EnqueueFailure();
        firstCatalog.Enqueue(new([channel]));
        var catalog = new GenesisAggregatingYellowPageChannelCatalog(
            new GenesisYellowPageConfiguration([first, second]),
            new FixedCatalogFactory(new Dictionary<string, IGenesisChannelCatalog>
            {
                ["first"] = firstCatalog,
                ["second"] = new FixedCatalog(GenesisChannelCatalogSnapshot.Empty),
            }));
        var presenter = new RecordingPresenter();
        var soundPlayer = new RecordingSoundPlayer();
        var publisher = new GenesisChannelNotificationPublisher(
            new GenesisFavoriteChannelCollection(),
            static () => GenesisChannelNotificationPreferences.Default,
            new GenesisChannelNotificationDispatcher(presenter, soundPlayer));
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var coordinator = new GenesisChannelCatalogCoordinator(
            catalog,
            viewModel,
            publisher);

        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await coordinator.RefreshAsync());
        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await coordinator.RefreshAsync());
        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await coordinator.RefreshAsync());

        var expected = channel with { YellowPage = "first" };
        var notification = Assert.Single(presenter.Notifications);
        Assert.Equal(expected, notification.Channel);
        Assert.Equal(
            GenesisChannelNotificationReason.NewChannel,
            notification.Reasons);
        Assert.Equal(1, soundPlayer.PlayCount);
        Assert.Equal(expected, Assert.Single(viewModel.Channels));

        var presence = coordinator.PresenceSnapshot!;
        Assert.Empty(presence.Transitions);
        var active = Assert.Single(presence.Active);
        Assert.True(active.IsCurrentlyObserved);
        Assert.Equal(0, active.ConsecutiveMissingSuccessfulRefreshes);
    }

    [Fact]
    public async Task ProvenEndThenReturnDispatchesReactivationNotification()
    {
        const string channelId = "EEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEE";
        var initial = new GenesisChannelItem(
            "Initial",
            "pcp://first.example/channel",
            ChannelId: channelId);
        var returned = new GenesisChannelItem(
            "Returned",
            "pcp://first.example/channel",
            Description: "updated metadata",
            ChannelId: channelId);
        var first = Definition("first");
        var firstCatalog = new SequencedCatalog();
        firstCatalog.Enqueue(new([initial]));
        firstCatalog.Enqueue(GenesisChannelCatalogSnapshot.Empty);
        firstCatalog.Enqueue(GenesisChannelCatalogSnapshot.Empty);
        firstCatalog.Enqueue(GenesisChannelCatalogSnapshot.Empty);
        firstCatalog.Enqueue(new([returned]));
        var catalog = new GenesisAggregatingYellowPageChannelCatalog(
            new GenesisYellowPageConfiguration([first]),
            new FixedCatalogFactory(new Dictionary<string, IGenesisChannelCatalog>
            {
                ["first"] = firstCatalog,
            }));
        var presenter = new RecordingPresenter();
        var soundPlayer = new RecordingSoundPlayer();
        var publisher = new GenesisChannelNotificationPublisher(
            new GenesisFavoriteChannelCollection(),
            static () => GenesisChannelNotificationPreferences.Default,
            new GenesisChannelNotificationDispatcher(presenter, soundPlayer));
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var coordinator = new GenesisChannelCatalogCoordinator(
            catalog,
            viewModel,
            publisher);

        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await coordinator.RefreshAsync());
        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await coordinator.RefreshAsync());
        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await coordinator.RefreshAsync());
        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await coordinator.RefreshAsync());

        Assert.Empty(viewModel.Channels);
        var ended = coordinator.PresenceSnapshot!;
        Assert.Empty(ended.Active);
        var endedTransition = Assert.Single(ended.Transitions);
        Assert.Equal(
            GenesisChannelPresenceTransitionKind.BecameEnded,
            endedTransition.Kind);

        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await coordinator.RefreshAsync());

        var expectedInitial = initial with { YellowPage = "first" };
        var expectedReturned = returned with { YellowPage = "first" };
        Assert.Collection(
            presenter.Notifications,
            notification =>
            {
                Assert.Equal(expectedInitial, notification.Channel);
                Assert.Equal(
                    GenesisChannelNotificationReason.NewChannel,
                    notification.Reasons);
            },
            notification =>
            {
                Assert.Equal(expectedReturned, notification.Channel);
                Assert.Equal(
                    GenesisChannelNotificationReason.NewChannel,
                    notification.Reasons);
            });
        Assert.Equal(2, soundPlayer.PlayCount);
        Assert.Equal(expectedReturned, Assert.Single(viewModel.Channels));

        var reactivated = coordinator.PresenceSnapshot!;
        var transition = Assert.Single(reactivated.Transitions);
        Assert.Equal(
            GenesisChannelPresenceTransitionKind.BecameActive,
            transition.Kind);
        Assert.True(transition.Entry.IsNew);
        Assert.True(transition.Entry.IsCurrentlyObserved);
        Assert.Equal(0, transition.Entry.ConsecutiveMissingSuccessfulRefreshes);
    }

    [Fact]
    public async Task NotificationPublisherFailureDoesNotFaultCatalogRefresh()
    {
        var channel = new GenesisChannelItem(
            "Channel",
            "pcp://example/channel",
            ChannelId: "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB");
        var catalog = new SequencedAggregatedCatalog();
        catalog.Enqueue(Snapshot(channel));
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var coordinator = new GenesisChannelCatalogCoordinator(
            catalog,
            viewModel,
            new ThrowingPublisher());

        var result = await coordinator.RefreshAsync();

        Assert.Equal(GenesisChannelCatalogRefreshResult.Completed, result);
        Assert.Same(channel, Assert.Single(viewModel.Channels));
        Assert.Null(viewModel.ChannelCatalogErrorMessage);
        var transition = Assert.Single(coordinator.PresenceSnapshot!.Transitions);
        Assert.Equal(
            GenesisChannelPresenceTransitionKind.BecameActive,
            transition.Kind);
    }

    private static GenesisAggregatedChannelCatalogSnapshot Snapshot(
        GenesisChannelItem channel) =>
        new(
            [new GenesisAggregatedChannelCatalogEntry(
                channel,
                Array.AsReadOnly(YellowPageIds))],
            [new GenesisYellowPageCatalogReadStatus(
                new GenesisYellowPageDefinition(
                    "yp",
                    "YP",
                    new Uri("https://yp.example/index.txt")),
                GenesisYellowPageCatalogReadState.Succeeded)]);

    private static GenesisYellowPageDefinition Definition(string id) =>
        new(
            id,
            id,
            new Uri($"https://{id}.example/index.txt"));

    private sealed class SequencedAggregatedCatalog : IGenesisAggregatedChannelCatalog
    {
        private readonly Queue<GenesisAggregatedChannelCatalogSnapshot> _snapshots = [];

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
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "Coordinator should use the detailed aggregate read.");
    }

    private sealed class FixedCatalogFactory : IGenesisYellowPageChannelCatalogFactory
    {
        private readonly IReadOnlyDictionary<string, IGenesisChannelCatalog> _catalogs;

        public FixedCatalogFactory(
            IReadOnlyDictionary<string, IGenesisChannelCatalog> catalogs)
        {
            _catalogs = catalogs;
        }

        public IGenesisChannelCatalog Create(GenesisYellowPageDefinition definition) =>
            _catalogs[definition.Id];
    }

    private sealed class FixedCatalog : IGenesisChannelCatalog
    {
        private readonly GenesisChannelCatalogSnapshot _snapshot;

        public FixedCatalog(GenesisChannelCatalogSnapshot snapshot)
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

    private sealed class SequencedCatalog : IGenesisChannelCatalog
    {
        private readonly Queue<GenesisChannelCatalogSnapshot?> _snapshots = [];

        public void Enqueue(GenesisChannelCatalogSnapshot snapshot)
        {
            _snapshots.Enqueue(snapshot);
        }

        public void EnqueueFailure()
        {
            _snapshots.Enqueue(null);
        }

        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = _snapshots.Dequeue();
            if (snapshot is null)
            {
                throw new InvalidOperationException("Synthetic Yellow Page failure.");
            }

            return ValueTask.FromResult(snapshot);
        }
    }

    private sealed class RecordingPresenter : IGenesisChannelNotificationPresenter
    {
        public List<GenesisChannelNotification> Notifications { get; } = [];

        public ValueTask PresentAsync(
            GenesisChannelNotification notification,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Notifications.Add(notification);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingSoundPlayer : IGenesisSystemNotificationSoundPlayer
    {
        public int PlayCount { get; private set; }

        public ValueTask PlayAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PlayCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingPublisher : IGenesisChannelPresenceNotificationPublisher
    {
        public ValueTask PublishAsync(
            GenesisChannelPresenceSnapshot presence,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Synthetic notification failure.");
    }
}
