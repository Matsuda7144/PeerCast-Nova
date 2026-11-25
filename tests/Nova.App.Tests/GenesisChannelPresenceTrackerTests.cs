namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelPresenceTrackerTests
{
    [Fact]
    public void InitialObservationEstablishesBaselineWithoutNewOrActivation()
    {
        var tracker = new GenesisChannelPresenceTracker();
        var initial = Channel("Initial");
        var baseline = tracker.ApplySuccessfulRefresh(Snapshot(
            [Entry(initial, "yp")],
            [Status("yp", GenesisYellowPageCatalogReadState.Succeeded)]));

        var active = Assert.Single(baseline.Active);
        Assert.Same(initial, active.Channel);
        Assert.True(active.IsCurrentlyObserved);
        Assert.False(active.IsNew);
        Assert.Equal(0, active.ConsecutiveMissingSuccessfulRefreshes);
        Assert.Empty(baseline.New);
        Assert.Empty(baseline.Transitions);

        var added = new GenesisChannelItem(
            "Added",
            "pcp://example/added",
            ChannelId: "FEDCBA9876543210FEDCBA9876543210");
        var next = tracker.ApplySuccessfulRefresh(Snapshot(
            [Entry(initial, "yp"), Entry(added, "yp")],
            [Status("yp", GenesisYellowPageCatalogReadState.Succeeded)]));

        var newEntry = Assert.Single(next.New);
        Assert.Same(added, newEntry.Channel);
        Assert.True(newEntry.IsNew);
        var transition = Assert.Single(next.Transitions);
        Assert.Equal(
            GenesisChannelPresenceTransitionKind.BecameActive,
            transition.Kind);
        Assert.Equal(newEntry.StableKey, transition.Entry.StableKey);
    }

    [Fact]
    public void MetadataOnlyRefreshDoesNotCreateAnotherActivation()
    {
        const string channelId = "0123456789ABCDEF0123456789ABCDEF";
        var tracker = new GenesisChannelPresenceTracker();
        var initial = new GenesisChannelItem(
            "Initial",
            "pcp://first.example/channel",
            ChannelId: channelId);
        var updated = new GenesisChannelItem(
            "Updated",
            "pcp://second.example/channel",
            Description: "metadata",
            ChannelId: channelId);

        tracker.ApplySuccessfulRefresh(Snapshot(
            [Entry(initial, "yp")],
            [Status("yp", GenesisYellowPageCatalogReadState.Succeeded)]));
        var result = tracker.ApplySuccessfulRefresh(Snapshot(
            [Entry(updated, "yp")],
            [Status("yp", GenesisYellowPageCatalogReadState.Succeeded)]));

        Assert.Empty(result.Transitions);
        Assert.Empty(result.New);
        var active = Assert.Single(result.Active);
        Assert.Same(updated, active.Channel);
        Assert.False(active.IsNew);
        Assert.True(active.IsCurrentlyObserved);
        Assert.Equal(0, active.ConsecutiveMissingSuccessfulRefreshes);
    }

    [Fact]
    public void ThreeConsecutiveProvenAbsencesEndChannel()
    {
        var tracker = new GenesisChannelPresenceTracker();
        tracker.ApplySuccessfulRefresh(Snapshot(
            [Entry(Channel("Initial"), "yp")],
            [Status("yp", GenesisYellowPageCatalogReadState.Succeeded)]));

        var firstMissing = tracker.ApplySuccessfulRefresh(EmptySucceeded("yp"));
        var secondMissing = tracker.ApplySuccessfulRefresh(EmptySucceeded("yp"));
        var ended = tracker.ApplySuccessfulRefresh(EmptySucceeded("yp"));

        var firstEntry = Assert.Single(firstMissing.Active);
        Assert.False(firstEntry.IsCurrentlyObserved);
        Assert.False(firstEntry.IsNew);
        Assert.Empty(firstMissing.New);
        Assert.Equal(1, firstEntry.ConsecutiveMissingSuccessfulRefreshes);
        Assert.Empty(firstMissing.Transitions);

        var secondEntry = Assert.Single(secondMissing.Active);
        Assert.False(secondEntry.IsCurrentlyObserved);
        Assert.Equal(2, secondEntry.ConsecutiveMissingSuccessfulRefreshes);
        Assert.Empty(secondMissing.Transitions);

        Assert.Empty(ended.Active);
        Assert.Empty(ended.New);
        var endedEntry = Assert.Single(ended.Entries);
        Assert.Equal(GenesisChannelPresenceState.Ended, endedEntry.State);
        Assert.Equal(3, endedEntry.ConsecutiveMissingSuccessfulRefreshes);
        var transition = Assert.Single(ended.Transitions);
        Assert.Equal(
            GenesisChannelPresenceTransitionKind.BecameEnded,
            transition.Kind);
    }

    [Fact]
    public void FailedSupplyingYellowPageDoesNotIncrementAbsence()
    {
        var tracker = new GenesisChannelPresenceTracker();
        tracker.ApplySuccessfulRefresh(EmptySucceeded("first"));
        tracker.ApplySuccessfulRefresh(Snapshot(
            [Entry(Channel("Initial"), "first")],
            [Status("first", GenesisYellowPageCatalogReadState.Succeeded)]));

        var partialFailure = tracker.ApplySuccessfulRefresh(Snapshot(
            [],
            [
                Status("first", GenesisYellowPageCatalogReadState.Failed),
                Status("second", GenesisYellowPageCatalogReadState.Succeeded),
            ]));

        var active = Assert.Single(partialFailure.Active);
        Assert.Equal(0, active.ConsecutiveMissingSuccessfulRefreshes);
        Assert.False(active.IsCurrentlyObserved);
        Assert.True(active.IsNew);
        Assert.Same(active, Assert.Single(partialFailure.New));

        var next = tracker.ApplySuccessfulRefresh(Snapshot(
            [],
            [
                Status("first", GenesisYellowPageCatalogReadState.Succeeded),
                Status("second", GenesisYellowPageCatalogReadState.Succeeded),
            ]));

        var nextActive = Assert.Single(next.Active);
        Assert.Equal(1, nextActive.ConsecutiveMissingSuccessfulRefreshes);
        Assert.False(nextActive.IsNew);
        Assert.Empty(next.New);
    }

    [Fact]
    public void ReturnAfterEndedBecomesNewAgain()
    {
        var tracker = new GenesisChannelPresenceTracker();
        var initial = Channel("Initial");
        tracker.ApplySuccessfulRefresh(Snapshot(
            [Entry(initial, "yp")],
            [Status("yp", GenesisYellowPageCatalogReadState.Succeeded)]));
        tracker.ApplySuccessfulRefresh(EmptySucceeded("yp"));
        tracker.ApplySuccessfulRefresh(EmptySucceeded("yp"));
        tracker.ApplySuccessfulRefresh(EmptySucceeded("yp"));

        var returned = Channel("Returned");
        var result = tracker.ApplySuccessfulRefresh(Snapshot(
            [Entry(returned, "yp")],
            [Status("yp", GenesisYellowPageCatalogReadState.Succeeded)]));

        var active = Assert.Single(result.Active);
        Assert.Same(returned, active.Channel);
        Assert.True(active.IsNew);
        Assert.True(active.IsCurrentlyObserved);
        Assert.Equal(0, active.ConsecutiveMissingSuccessfulRefreshes);
        var transition = Assert.Single(result.Transitions);
        Assert.Equal(
            GenesisChannelPresenceTransitionKind.BecameActive,
            transition.Kind);
    }

    [Fact]
    public void NoSuccessfulYellowPageEvidenceDoesNotAgePresence()
    {
        var tracker = new GenesisChannelPresenceTracker();
        tracker.ApplySuccessfulRefresh(EmptySucceeded("yp"));
        tracker.ApplySuccessfulRefresh(Snapshot(
            [Entry(Channel("Initial"), "yp")],
            [Status("yp", GenesisYellowPageCatalogReadState.Succeeded)]));

        var result = tracker.ApplySuccessfulRefresh(Snapshot([], []));

        var active = Assert.Single(result.Active);
        Assert.Equal(0, active.ConsecutiveMissingSuccessfulRefreshes);
        Assert.False(active.IsCurrentlyObserved);
        Assert.True(active.IsNew);
        Assert.Same(active, Assert.Single(result.New));
        Assert.Empty(result.Transitions);
    }

    [Fact]
    public void ObservationFromAdditionalYellowPagePreservesKnownOrigins()
    {
        var tracker = new GenesisChannelPresenceTracker();
        var channel = Channel("Shared");
        tracker.ApplySuccessfulRefresh(Snapshot(
            [Entry(channel, "first")],
            [Status("first", GenesisYellowPageCatalogReadState.Succeeded)]));

        var result = tracker.ApplySuccessfulRefresh(Snapshot(
            [Entry(channel, "second")],
            [
                Status("first", GenesisYellowPageCatalogReadState.Succeeded),
                Status("second", GenesisYellowPageCatalogReadState.Succeeded),
            ]));

        var active = Assert.Single(result.Active);
        Assert.Collection(
            active.KnownYellowPageIds,
            id => Assert.Equal("first", id),
            id => Assert.Equal("second", id));
        Assert.Empty(result.Transitions);
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
}
