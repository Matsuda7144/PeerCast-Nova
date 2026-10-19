namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelNotificationProjectionTests
{
    private const string ChannelId = "0123456789ABCDEF0123456789ABCDEF";

    [Fact]
    public void NewActivationProducesOneNewChannelNotification()
    {
        var channel = Channel("New", "pcp://example/new");

        var notification = Assert.Single(GenesisChannelNotificationProjection.Create(
            Activation(channel),
            [],
            GenesisChannelNotificationPreferences.Default));

        Assert.Same(channel, notification.Channel);
        Assert.Equal(
            GenesisChannelNotificationReason.NewChannel,
            notification.Reasons);
        Assert.True(notification.PlaySound);
    }

    [Fact]
    public void FavoriteActivationCombinesReasonsIntoOneNotification()
    {
        var channel = Channel("Favorite", "pcp://example/favorite");
        var favorite = GenesisFavoriteChannel.FromChannel(channel);

        var notification = Assert.Single(GenesisChannelNotificationProjection.Create(
            Activation(channel),
            [favorite],
            GenesisChannelNotificationPreferences.Default));

        Assert.Equal(
            GenesisChannelNotificationReason.NewChannel |
                GenesisChannelNotificationReason.FavoriteChannel,
            notification.Reasons);
    }

    [Fact]
    public void FavoriteMatchingUsesStableChannelIdentity()
    {
        var saved = Channel("Saved", "pcp://old.example/channel");
        var current = Channel("Current", "pcp://new.example/channel");
        var preferences = new GenesisChannelNotificationPreferences(
            NotifyNewChannels: false,
            NotifyFavoriteChannels: true,
            PlaySound: true);

        var notification = Assert.Single(GenesisChannelNotificationProjection.Create(
            Activation(current),
            [GenesisFavoriteChannel.FromChannel(saved)],
            preferences));

        Assert.Same(current, notification.Channel);
        Assert.Equal(
            GenesisChannelNotificationReason.FavoriteChannel,
            notification.Reasons);
    }

    [Fact]
    public void DisabledRulesProduceNoNotification()
    {
        var preferences = new GenesisChannelNotificationPreferences(
            NotifyNewChannels: false,
            NotifyFavoriteChannels: false,
            PlaySound: true);

        var notifications = GenesisChannelNotificationProjection.Create(
            Activation(Channel("Ignored", "pcp://example/ignored")),
            [],
            preferences);

        Assert.Empty(notifications);
    }

    [Fact]
    public void SoundCanBeDisabledWithoutSuppressingNotification()
    {
        var preferences = new GenesisChannelNotificationPreferences(
            NotifyNewChannels: true,
            NotifyFavoriteChannels: true,
            PlaySound: false);

        var notification = Assert.Single(GenesisChannelNotificationProjection.Create(
            Activation(Channel("Silent", "pcp://example/silent")),
            [],
            preferences));

        Assert.False(notification.PlaySound);
    }

    [Fact]
    public void CurrentNewStateWithoutActivationDoesNotRepeatNotification()
    {
        var channel = Channel("Existing", "pcp://example/existing");
        var stableKey = GenesisChannelIdentity.GetStableKey(channel);
        var entry = Entry(
            stableKey,
            channel,
            GenesisChannelPresenceState.Active,
            isCurrentlyObserved: true,
            isNew: true);
        var presence = new GenesisChannelPresenceSnapshot([entry], []);

        var notifications = GenesisChannelNotificationProjection.Create(
            presence,
            [],
            GenesisChannelNotificationPreferences.Default);

        Assert.Empty(notifications);
    }

    [Fact]
    public void EndedTransitionDoesNotProduceNotification()
    {
        var channel = Channel("Ended", "pcp://example/ended");
        var stableKey = GenesisChannelIdentity.GetStableKey(channel);
        var entry = Entry(
            stableKey,
            channel,
            GenesisChannelPresenceState.Ended,
            isCurrentlyObserved: false,
            isNew: false);
        var presence = new GenesisChannelPresenceSnapshot(
            [entry],
            [new GenesisChannelPresenceTransition(
                GenesisChannelPresenceTransitionKind.BecameEnded,
                entry)]);

        var notifications = GenesisChannelNotificationProjection.Create(
            presence,
            [GenesisFavoriteChannel.FromChannel(channel)],
            GenesisChannelNotificationPreferences.Default);

        Assert.Empty(notifications);
    }

    private static GenesisChannelPresenceSnapshot Activation(
        GenesisChannelItem channel)
    {
        var stableKey = GenesisChannelIdentity.GetStableKey(channel);
        var entry = Entry(
            stableKey,
            channel,
            GenesisChannelPresenceState.Active,
            isCurrentlyObserved: true,
            isNew: true);
        return new GenesisChannelPresenceSnapshot(
            [entry],
            [new GenesisChannelPresenceTransition(
                GenesisChannelPresenceTransitionKind.BecameActive,
                entry)]);
    }

    private static GenesisChannelPresenceEntry Entry(
        string stableKey,
        GenesisChannelItem channel,
        GenesisChannelPresenceState state,
        bool isCurrentlyObserved,
        bool isNew) =>
        new(
            stableKey,
            channel,
            state,
            isCurrentlyObserved,
            isNew,
            ConsecutiveMissingSuccessfulRefreshes: 0,
            KnownYellowPageIds: Array.Empty<string>());

    private static GenesisChannelItem Channel(string name, string source) =>
        new(
            name,
            source,
            ChannelId: ChannelId);
}
