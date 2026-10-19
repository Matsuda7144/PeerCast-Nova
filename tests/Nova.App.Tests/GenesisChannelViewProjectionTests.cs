namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelViewProjectionTests
{
    private static readonly string[] YellowPageIds = ["yp"];
    private static readonly GenesisYellowPageCatalogReadStatus[] SuccessfulStatuses =
    [
        Status(GenesisYellowPageCatalogReadState.Succeeded),
    ];

    [Fact]
    public void PresenceProjectionSharesCurrentChannelInstances()
    {
        var tracker = new GenesisChannelPresenceTracker();
        var first = Channel(
            "First",
            "0123456789ABCDEF0123456789ABCDEF");
        var second = Channel(
            "Second",
            "FEDCBA9876543210FEDCBA9876543210");
        var presence = tracker.ApplySuccessfulRefresh(Snapshot(
            [Entry(first), Entry(second)]));

        var projection = GenesisChannelViewProjectionSnapshot.FromPresence(presence);

        Assert.Equal(2, projection.Channels.Count);
        Assert.Equal(2, projection.NewChannels.Count);
        Assert.Same(first, projection.Channels[0]);
        Assert.Same(first, projection.NewChannels[0]);
        Assert.Same(second, projection.Channels[1]);
        Assert.Same(second, projection.NewChannels[1]);
    }

    [Fact]
    public void MetadataRefreshUpdatesChannelAndExpiresNewProjectionWithoutDuplicateActivation()
    {
        const string channelId = "0123456789ABCDEF0123456789ABCDEF";
        var tracker = new GenesisChannelPresenceTracker();
        tracker.ApplySuccessfulRefresh(Snapshot([
            Entry(Channel("Initial", channelId)),
        ]));
        var updated = Channel("Updated", channelId);
        var presence = tracker.ApplySuccessfulRefresh(Snapshot([
            Entry(updated),
        ]));

        var projection = GenesisChannelViewProjectionSnapshot.FromPresence(presence);

        Assert.Empty(presence.Transitions);
        Assert.Same(updated, Assert.Single(projection.Channels));
        Assert.Empty(projection.NewChannels);
    }

    [Fact]
    public void EndedChannelLeavesChannelsAndNewViews()
    {
        var tracker = new GenesisChannelPresenceTracker();
        tracker.ApplySuccessfulRefresh(Snapshot([
            Entry(Channel(
                "Initial",
                "0123456789ABCDEF0123456789ABCDEF")),
        ]));
        tracker.ApplySuccessfulRefresh(EmptySucceeded());
        tracker.ApplySuccessfulRefresh(EmptySucceeded());
        var ended = tracker.ApplySuccessfulRefresh(EmptySucceeded());

        var projection = GenesisChannelViewProjectionSnapshot.FromPresence(ended);

        Assert.Empty(projection.Channels);
        Assert.Empty(projection.NewChannels);
    }

    [Fact]
    public void ReturnAfterEndedAppearsInNewViewAgain()
    {
        const string channelId = "0123456789ABCDEF0123456789ABCDEF";
        var tracker = new GenesisChannelPresenceTracker();
        tracker.ApplySuccessfulRefresh(Snapshot([
            Entry(Channel("Initial", channelId)),
        ]));
        tracker.ApplySuccessfulRefresh(EmptySucceeded());
        tracker.ApplySuccessfulRefresh(EmptySucceeded());
        tracker.ApplySuccessfulRefresh(EmptySucceeded());
        var returned = Channel("Returned", channelId);
        var presence = tracker.ApplySuccessfulRefresh(Snapshot([
            Entry(returned),
        ]));

        var projection = GenesisChannelViewProjectionSnapshot.FromPresence(presence);

        Assert.Same(returned, Assert.Single(projection.Channels));
        Assert.Same(returned, Assert.Single(projection.NewChannels));
        Assert.Equal(
            GenesisChannelPresenceTransitionKind.BecameActive,
            Assert.Single(presence.Transitions).Kind);
    }

    [Fact]
    public void PlainCatalogProjectionHasNoNewClassification()
    {
        var channel = Channel(
            "Legacy",
            "0123456789ABCDEF0123456789ABCDEF");
        var projection = GenesisChannelViewProjectionSnapshot.FromCatalog(
            new GenesisChannelCatalogSnapshot([channel]));

        Assert.Same(channel, Assert.Single(projection.Channels));
        Assert.Empty(projection.NewChannels);
    }

    private static GenesisChannelItem Channel(string name, string channelId) =>
        new(
            name,
            $"pcp://example/channel/{channelId}",
            ChannelId: channelId);

    private static GenesisAggregatedChannelCatalogEntry Entry(
        GenesisChannelItem channel) =>
        new(
            channel,
            Array.AsReadOnly(YellowPageIds));

    private static GenesisAggregatedChannelCatalogSnapshot Snapshot(
        GenesisAggregatedChannelCatalogEntry[] entries) =>
        new(entries, SuccessfulStatuses);

    private static GenesisAggregatedChannelCatalogSnapshot EmptySucceeded() =>
        Snapshot([]);

    private static GenesisYellowPageCatalogReadStatus Status(
        GenesisYellowPageCatalogReadState state) =>
        new(
            new GenesisYellowPageDefinition(
                "yp",
                "YP",
                new Uri("https://yp.example/index.txt")),
            state);
}
