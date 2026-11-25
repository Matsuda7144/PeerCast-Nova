namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextYellowPageFilterTests
{
    [Fact]
    public void PresenceSnapshotRetainsAttemptedYellowPagesForGuiFiltering()
    {
        var channel = Channel(
            "Alpha",
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        var detailed = Snapshot(
            (channel, "yp-a", "YP A"));
        var tracker = new GenesisChannelPresenceTracker();

        var presence = tracker.ApplySuccessfulRefresh(detailed);

        var yellowPage = Assert.Single(presence.YellowPages);
        Assert.Equal("yp-a", yellowPage.Definition.Id);
        Assert.Equal("YP A", yellowPage.Definition.Name);
        Assert.Equal(GenesisYellowPageCatalogReadState.Succeeded, yellowPage.State);
    }

    [Fact]
    public void SelectedYellowPageFiltersChannelsNewAndFavorites()
    {
        var alpha = Channel(
            "Alpha",
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        var beta = Channel(
            "Beta",
            "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB");
        var tracker = new GenesisChannelPresenceTracker();
        tracker.ApplySuccessfulRefresh(new GenesisAggregatedChannelCatalogSnapshot(
            [],
            [
                new GenesisYellowPageCatalogReadStatus(
                    new GenesisYellowPageDefinition("yp-a", "YP A", new Uri("https://yp-a.example/index.txt")),
                    GenesisYellowPageCatalogReadState.Succeeded),
                new GenesisYellowPageCatalogReadStatus(
                    new GenesisYellowPageDefinition("yp-b", "YP B", new Uri("https://yp-b.example/index.txt")),
                    GenesisYellowPageCatalogReadState.Succeeded),
            ]));
        var presence = tracker.ApplySuccessfulRefresh(
            Snapshot(
                (alpha, "yp-a", "YP A"),
                (beta, "yp-b", "YP B")));
        var favorites = new GenesisFavoriteChannelCollection([
            GenesisFavoriteChannel.FromChannel(alpha),
            GenesisFavoriteChannel.FromChannel(beta),
        ]);
        var tabs = new GenesisGuiNextTabsViewModel(favorites);
        tabs.ApplyChannelViews(
            GenesisChannelViewProjectionSnapshot.FromPresence(presence),
            presence);

        Assert.Collection(
            tabs.YellowPageFilters,
            option =>
            {
                Assert.Equal("yp-a", option.Id);
                Assert.Equal("YP A", option.DisplayName);
            },
            option =>
            {
                Assert.Equal("yp-b", option.Id);
                Assert.Equal("YP B", option.DisplayName);
            });

        tabs.SelectedYellowPageId = "yp-a";

        Assert.Same(alpha, Assert.Single(tabs.VisibleChannels));
        Assert.Same(alpha, Assert.Single(tabs.VisibleNewChannels));
        var favorite = Assert.Single(tabs.VisibleFavoriteChannels);
        Assert.Same(alpha, favorite.CurrentChannel);
        Assert.Equal(1, tabs.VisibleChannelCount);
        Assert.Equal(1, tabs.VisibleNewChannelCount);
        Assert.Equal(1, tabs.VisibleFavoriteChannelCount);
    }

    [Fact]
    public void YellowPageFilterComposesWithSearchWithoutMutatingCanonicalViews()
    {
        var alpha = new GenesisChannelItem(
            "Alpha",
            "pcp://example/alpha",
            Description: "Late night music",
            ChannelId: "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        var beta = new GenesisChannelItem(
            "Beta",
            "pcp://example/beta",
            Description: "Late night music",
            ChannelId: "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB");
        var tracker = new GenesisChannelPresenceTracker();
        tracker.ApplySuccessfulRefresh(new GenesisAggregatedChannelCatalogSnapshot(
            [],
            [
                new GenesisYellowPageCatalogReadStatus(
                    new GenesisYellowPageDefinition("yp-a", "YP A", new Uri("https://yp-a.example/index.txt")),
                    GenesisYellowPageCatalogReadState.Succeeded),
                new GenesisYellowPageCatalogReadStatus(
                    new GenesisYellowPageDefinition("yp-b", "YP B", new Uri("https://yp-b.example/index.txt")),
                    GenesisYellowPageCatalogReadState.Succeeded),
            ]));
        var presence = tracker.ApplySuccessfulRefresh(
            Snapshot(
                (alpha, "yp-a", "YP A"),
                (beta, "yp-b", "YP B")));
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());
        tabs.ApplyChannelViews(
            GenesisChannelViewProjectionSnapshot.FromPresence(presence),
            presence);

        tabs.SelectedYellowPageId = "yp-a";
        tabs.SearchText = "late alpha";

        Assert.Equal(2, tabs.ChannelCount);
        Assert.Equal(2, tabs.NewChannelCount);
        Assert.Same(alpha, Assert.Single(tabs.VisibleChannels));
        Assert.Same(alpha, Assert.Single(tabs.VisibleNewChannels));

        tabs.SearchText = "beta";

        Assert.Empty(tabs.VisibleChannels);
        Assert.Empty(tabs.VisibleNewChannels);
        Assert.Equal(2, tabs.ChannelCount);
        Assert.Equal(2, tabs.NewChannelCount);
    }

    [Fact]
    public void MissingSelectedYellowPageFallsBackToAllAfterRefresh()
    {
        var alpha = Channel(
            "Alpha",
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        var beta = Channel(
            "Beta",
            "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB");
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());
        var initial = new GenesisChannelPresenceTracker().ApplySuccessfulRefresh(
            Snapshot(
                (alpha, "yp-a", "YP A"),
                (beta, "yp-b", "YP B")));
        tabs.ApplyChannelViews(
            GenesisChannelViewProjectionSnapshot.FromPresence(initial),
            initial);
        tabs.SelectedYellowPageId = "yp-b";
        Assert.Same(beta, Assert.Single(tabs.VisibleChannels));

        var refreshed = new GenesisChannelPresenceTracker().ApplySuccessfulRefresh(
            Snapshot((alpha, "yp-a", "YP A")));
        tabs.ApplyChannelViews(
            GenesisChannelViewProjectionSnapshot.FromPresence(refreshed),
            refreshed);

        Assert.Null(tabs.SelectedYellowPageId);
        Assert.Same(alpha, Assert.Single(tabs.VisibleChannels));
        var option = Assert.Single(tabs.YellowPageFilters);
        Assert.Equal("yp-a", option.Id);
    }

    [Fact]
    public void YellowPageFilterDoesNotAlterPlayingProjection()
    {
        var channel = Channel(
            "Alpha",
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        var presence = new GenesisChannelPresenceTracker().ApplySuccessfulRefresh(
            Snapshot((channel, "yp-a", "YP A")));
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());
        tabs.ApplyChannelViews(
            GenesisChannelViewProjectionSnapshot.FromPresence(presence),
            presence);
        tabs.UpdatePlayback(new PlaybackSessionSnapshot(
            PlaybackSessionState.Playing,
            channel.Source,
            null));

        tabs.SelectedYellowPageId = "yp-a";
        tabs.SearchText = "no-match";

        var playing = Assert.Single(tabs.Playing);
        Assert.Equal(channel.Source, playing.Source);
    }

    private static GenesisAggregatedChannelCatalogSnapshot Snapshot(
        params (GenesisChannelItem Channel, string YellowPageId, string YellowPageName)[] entries)
    {
        var definitions = entries
            .GroupBy(static entry => entry.YellowPageId, StringComparer.Ordinal)
            .Select(static group => new GenesisYellowPageDefinition(
                group.Key,
                group.First().YellowPageName,
                new Uri($"https://{group.Key}.example/index.txt")))
            .ToArray();
        var aggregatedEntries = entries
            .Select(static entry => new GenesisAggregatedChannelCatalogEntry(
                entry.Channel,
                Array.AsReadOnly(new[] { entry.YellowPageId })))
            .ToArray();
        var statuses = definitions
            .Select(static definition => new GenesisYellowPageCatalogReadStatus(
                definition,
                GenesisYellowPageCatalogReadState.Succeeded))
            .ToArray();
        return new GenesisAggregatedChannelCatalogSnapshot(
            aggregatedEntries,
            statuses);
    }

    private static GenesisChannelItem Channel(string name, string channelId) =>
        new(
            name,
            $"pcp://example/{name.ToLowerInvariant()}",
            ChannelId: channelId);
}
