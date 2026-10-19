namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelNotificationCancellationTests
{
    [Fact]
    public async Task PublisherStopsAfterRequestCancellationDuringPresentation()
    {
        var first = Channel("First", 'I');
        var second = Channel("Second", 'J');
        var favorites = new GenesisFavoriteChannelCollection();
        using var cancellation = new CancellationTokenSource();
        var presenter = new CancelFirstPresenter(cancellation);
        var soundPlayer = new RecordingSoundPlayer();
        var publisher = new GenesisChannelNotificationPublisher(
            favorites,
            static () => GenesisChannelNotificationPreferences.Default,
            new GenesisChannelNotificationDispatcher(presenter, soundPlayer));

        await publisher.PublishAsync(
            Activations(first, second),
            cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(1, presenter.Attempts);
        Assert.Empty(presenter.Notifications);
        Assert.Equal(0, soundPlayer.PlayCount);
    }

    [Fact]
    public async Task PublisherContinuesAfterUnrelatedOperationCanceledException()
    {
        var first = Channel("First", 'K');
        var second = Channel("Second", 'L');
        var favorites = new GenesisFavoriteChannelCollection();
        var presenter = new ThrowUnrelatedCancellationFirstPresenter();
        var soundPlayer = new RecordingSoundPlayer();
        var publisher = new GenesisChannelNotificationPublisher(
            favorites,
            static () => GenesisChannelNotificationPreferences.Default,
            new GenesisChannelNotificationDispatcher(presenter, soundPlayer));

        await publisher.PublishAsync(Activations(first, second));

        Assert.Equal(2, presenter.Attempts);
        var delivered = Assert.Single(presenter.Notifications);
        Assert.Same(second, delivered.Channel);
        Assert.Equal(1, soundPlayer.PlayCount);
    }

    private static GenesisChannelPresenceSnapshot Activations(
        params GenesisChannelItem[] channels)
    {
        var entries = channels
            .Select(channel => new GenesisChannelPresenceEntry(
                GenesisChannelIdentity.GetStableKey(channel),
                channel,
                GenesisChannelPresenceState.Active,
                IsCurrentlyObserved: true,
                IsNew: true,
                ConsecutiveMissingSuccessfulRefreshes: 0,
                KnownYellowPageIds: Array.Empty<string>()))
            .ToArray();
        var transitions = entries
            .Select(entry => new GenesisChannelPresenceTransition(
                GenesisChannelPresenceTransitionKind.BecameActive,
                entry))
            .ToArray();
        return new GenesisChannelPresenceSnapshot(entries, transitions);
    }

    private static GenesisChannelItem Channel(string name, char channelIdCharacter) =>
        new(
            name,
            $"pcp://example/{name.ToLowerInvariant()}",
            ChannelId: new string(channelIdCharacter, 32));

    private sealed class CancelFirstPresenter : IGenesisChannelNotificationPresenter
    {
        private readonly CancellationTokenSource _cancellation;

        public CancelFirstPresenter(CancellationTokenSource cancellation)
        {
            _cancellation = cancellation;
        }

        public int Attempts { get; private set; }

        public List<GenesisChannelNotification> Notifications { get; } = [];

        public ValueTask PresentAsync(
            GenesisChannelNotification notification,
            CancellationToken cancellationToken = default)
        {
            Attempts++;
            _cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            Notifications.Add(notification);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowUnrelatedCancellationFirstPresenter :
        IGenesisChannelNotificationPresenter
    {
        public int Attempts { get; private set; }

        public List<GenesisChannelNotification> Notifications { get; } = [];

        public ValueTask PresentAsync(
            GenesisChannelNotification notification,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Attempts++;
            if (Attempts == 1)
            {
                throw new OperationCanceledException("Synthetic unrelated cancellation.");
            }

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
}
