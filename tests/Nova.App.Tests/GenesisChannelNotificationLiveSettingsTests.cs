namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelNotificationLiveSettingsTests
{
    [Fact]
    public async Task PublisherReadsUpdatedNotificationSettingsAtPublishTime()
    {
        var before = Channel("Before", 'G');
        var after = Channel("After", 'H');
        var favorites = new GenesisFavoriteChannelCollection();
        var presenter = new RecordingPresenter();
        var soundPlayer = new RecordingSoundPlayer();
        var settings = new GenesisChannelNotificationSettings(
            new GenesisChannelNotificationPreferences(
                NotifyNewChannels: false,
                NotifyFavoriteChannels: false,
                PlaySound: false),
            soundPlayer);
        settings.BindPersistence(static _ => true);
        var publisher = new GenesisChannelNotificationPublisher(
            favorites,
            () => settings.Current,
            new GenesisChannelNotificationDispatcher(presenter, soundPlayer));

        await publisher.PublishAsync(Activation(before));

        Assert.Empty(presenter.Notifications);
        Assert.Equal(0, soundPlayer.PlayCount);

        Assert.Equal(
            GenesisChannelNotificationSettingsMutationResult.Changed,
            settings.SetNotifyNewChannels(true));
        Assert.Equal(
            GenesisChannelNotificationSettingsMutationResult.Changed,
            settings.SetPlaySound(true));

        await publisher.PublishAsync(Activation(after));

        var delivered = Assert.Single(presenter.Notifications);
        Assert.Same(after, delivered.Channel);
        Assert.Equal(
            GenesisChannelNotificationReason.NewChannel,
            delivered.Reasons);
        Assert.True(delivered.PlaySound);
        Assert.Equal(1, soundPlayer.PlayCount);
    }

    private static GenesisChannelPresenceSnapshot Activation(GenesisChannelItem channel)
    {
        var entry = new GenesisChannelPresenceEntry(
            GenesisChannelIdentity.GetStableKey(channel),
            channel,
            GenesisChannelPresenceState.Active,
            IsCurrentlyObserved: true,
            IsNew: true,
            ConsecutiveMissingSuccessfulRefreshes: 0,
            KnownYellowPageIds: Array.Empty<string>());
        return new GenesisChannelPresenceSnapshot(
            [entry],
            [new GenesisChannelPresenceTransition(
                GenesisChannelPresenceTransitionKind.BecameActive,
                entry)]);
    }

    private static GenesisChannelItem Channel(string name, char channelIdCharacter) =>
        new(
            name,
            $"pcp://example/{name.ToLowerInvariant()}",
            ChannelId: new string(channelIdCharacter, 32));

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
}
