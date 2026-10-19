namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextNativeChannelFilterTests
{
    [Fact]
    public void NativeChannelRowsComposeYellowPageAndSearchWithCurrentSortOrder()
    {
        var alpha = Channel(
            "Alpha",
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            "Late night music");
        var beta = Channel(
            "Beta",
            "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
            "Late night talk");
        var gamma = Channel(
            "Gamma",
            "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC",
            "Late night music");
        var presence = new GenesisChannelPresenceTracker().ApplySuccessfulRefresh(
            Snapshot(
                (alpha, "yp-a", "YP A"),
                (beta, "yp-b", "YP B"),
                (gamma, "yp-a", "YP A")));
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());
        tabs.ApplyChannelViews(
            GenesisChannelViewProjectionSnapshot.FromPresence(presence),
            presence);
        tabs.SelectedYellowPageId = "yp-a";
        tabs.SearchText = "music";

        var currentlySortedChannels = new[] { gamma, beta, alpha };
        var rows = SystemGenesisMainWindowInteractionBridgeFactory
            .CreateChannelsRowsForNativeList(currentlySortedChannels, tabs);

        Assert.Collection(
            rows,
            row =>
            {
                Assert.Same(gamma, row.DisplayChannel);
                Assert.Same(gamma, row.InteractiveChannel);
            },
            row =>
            {
                Assert.Same(alpha, row.DisplayChannel);
                Assert.Same(alpha, row.InteractiveChannel);
            });
    }

    [Fact]
    public void NativeChannelRowsWithoutGuiNextRetainExistingChannelOrder()
    {
        var alpha = Channel(
            "Alpha",
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            "Music");
        var beta = Channel(
            "Beta",
            "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
            "Talk");
        var channels = new[] { beta, alpha };

        var rows = SystemGenesisMainWindowInteractionBridgeFactory
            .CreateChannelsRowsForNativeList(channels, guiNextTabs: null);

        Assert.Collection(
            rows,
            row =>
            {
                Assert.Same(beta, row.DisplayChannel);
                Assert.Same(beta, row.InteractiveChannel);
            },
            row =>
            {
                Assert.Same(alpha, row.DisplayChannel);
                Assert.Same(alpha, row.InteractiveChannel);
            });
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

    private static GenesisChannelItem Channel(
        string name,
        string channelId,
        string description) =>
        new(
            name,
            $"pcp://example/{name.ToLowerInvariant()}",
            Description: description,
            ChannelId: channelId);
}
