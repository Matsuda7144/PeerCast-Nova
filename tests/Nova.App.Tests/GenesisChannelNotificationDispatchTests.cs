namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelNotificationDispatchTests
{
    [Fact]
    public async Task DispatchPresentsBeforePlayingSound()
    {
        var events = new List<string>();
        var presenter = new RecordingPresenter(events);
        var soundPlayer = new RecordingSoundPlayer(events);
        var dispatcher = new GenesisChannelNotificationDispatcher(
            presenter,
            soundPlayer);
        var notification = Notification(
            Channel("New", 'A'),
            playSound: true);

        await dispatcher.DispatchAsync(notification);

        Assert.Collection(
            events,
            item => Assert.Equal("present", item),
            item => Assert.Equal("sound", item));
        Assert.Same(notification, Assert.Single(presenter.Notifications));
        Assert.Equal(1, soundPlayer.PlayCount);
    }

    [Fact]
    public async Task DispatchWithoutSoundSkipsSoundBoundary()
    {
        var presenter = new RecordingPresenter();
        var soundPlayer = new RecordingSoundPlayer();
        var dispatcher = new GenesisChannelNotificationDispatcher(
            presenter,
            soundPlayer);
        var notification = Notification(
            Channel("Silent", 'B'),
            playSound: false);

        await dispatcher.DispatchAsync(notification);

        Assert.Same(notification, Assert.Single(presenter.Notifications));
        Assert.Equal(0, soundPlayer.PlayCount);
    }

    [Fact]
    public async Task TestSoundUsesSystemSoundBoundaryWithoutPresentation()
    {
        var presenter = new RecordingPresenter();
        var soundPlayer = new RecordingSoundPlayer();
        var dispatcher = new GenesisChannelNotificationDispatcher(
            presenter,
            soundPlayer);

        await dispatcher.TestSoundAsync();

        Assert.Empty(presenter.Notifications);
        Assert.Equal(1, soundPlayer.PlayCount);
    }

    [Fact]
    public async Task PublisherContinuesAfterOnePresentationFailure()
    {
        var first = Channel("First", 'C');
        var second = Channel("Second", 'D');
        var favorites = new GenesisFavoriteChannelCollection();
        var presenter = new FailFirstPresenter();
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

    [Fact]
    public async Task PublisherReadsCurrentFavoritesAtPublishTime()
    {
        var channel = Channel("Favorite", 'E');
        var favorites = new GenesisFavoriteChannelCollection();
        var preferences = new GenesisChannelNotificationPreferences(
            NotifyNewChannels: false,
            NotifyFavoriteChannels: true,
            PlaySound: false);
        var presenter = new RecordingPresenter();
        var soundPlayer = new RecordingSoundPlayer();
        var publisher = new GenesisChannelNotificationPublisher(
            favorites,
            () => preferences,
            new GenesisChannelNotificationDispatcher(presenter, soundPlayer));
        var activation = Activations(channel);

        await publisher.PublishAsync(activation);
        Assert.Empty(presenter.Notifications);

        favorites.AddOrUpdate(channel);
        await publisher.PublishAsync(activation);

        var delivered = Assert.Single(presenter.Notifications);
        Assert.Equal(
            GenesisChannelNotificationReason.FavoriteChannel,
            delivered.Reasons);
        Assert.Equal(0, soundPlayer.PlayCount);
    }

    [Fact]
    public async Task GuiNextPresencePublisherProjectsTabsBeforeNotifications()
    {
        var channel = Channel("Projected", 'F');
        var favorites = new GenesisFavoriteChannelCollection();
        favorites.AddOrUpdate(channel);
        var tabs = new GenesisGuiNextTabsViewModel(favorites);
        var next = new RecordingPresencePublisher(tabs);
        var publisher = new GenesisGuiNextChannelPresencePublisher(tabs, next);
        var presence = Activations(channel);

        await publisher.PublishAsync(presence);

        Assert.Same(presence, next.Presence);
        Assert.True(next.SawProjectedTabs);
        Assert.Same(channel, Assert.Single(tabs.Channels));
        Assert.Same(channel, Assert.Single(tabs.NewChannels));
        var favorite = Assert.Single(tabs.FavoriteChannels);
        Assert.Equal(GenesisChannelIdentity.GetStableKey(channel), favorite.Favorite.StableKey);
        Assert.Same(channel, favorite.CurrentChannel);
    }

    private static GenesisChannelNotification Notification(
        GenesisChannelItem channel,
        bool playSound) =>
        new(
            channel,
            GenesisChannelNotificationReason.NewChannel,
            playSound);

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

    private sealed class RecordingPresenter : IGenesisChannelNotificationPresenter
    {
        private readonly List<string>? _events;

        public RecordingPresenter(List<string>? events = null)
        {
            _events = events;
        }

        public List<GenesisChannelNotification> Notifications { get; } = [];

        public ValueTask PresentAsync(
            GenesisChannelNotification notification,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Notifications.Add(notification);
            _events?.Add("present");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingSoundPlayer : IGenesisSystemNotificationSoundPlayer
    {
        private readonly List<string>? _events;

        public RecordingSoundPlayer(List<string>? events = null)
        {
            _events = events;
        }

        public int PlayCount { get; private set; }

        public ValueTask PlayAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PlayCount++;
            _events?.Add("sound");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailFirstPresenter : IGenesisChannelNotificationPresenter
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
                throw new InvalidOperationException("Synthetic presentation failure.");
            }

            Notifications.Add(notification);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingPresencePublisher :
        IGenesisChannelPresenceNotificationPublisher
    {
        private readonly GenesisGuiNextTabsViewModel _tabs;

        public RecordingPresencePublisher(GenesisGuiNextTabsViewModel tabs)
        {
            _tabs = tabs;
        }

        public GenesisChannelPresenceSnapshot? Presence { get; private set; }

        public bool SawProjectedTabs { get; private set; }

        public ValueTask PublishAsync(
            GenesisChannelPresenceSnapshot presence,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Presence = presence;
            SawProjectedTabs = _tabs.ChannelCount == 1 &&
                _tabs.NewChannelCount == 1 &&
                _tabs.FavoriteChannelCount == 1;
            return ValueTask.CompletedTask;
        }
    }
}
