namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextSearchNavigationTests
{
    [Fact]
    public void SearchActivationCommitsUtilitySelectionAfterChannelsTransition()
    {
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection())
        {
            SelectedTab = GenesisGuiNextTab.New,
        };
        var selection = AttachSelectionToTabChanges(tabs);

        var focused = selection.ActivateSearch(tabs, static () => true);

        Assert.True(focused);
        Assert.Equal(GenesisGuiNextTab.Channels, tabs.SelectedTab);
        Assert.Equal(
            GenesisGuiNextNavigationPresentation.SearchNavigationItemIndex,
            selection.SelectedItemIndex);
    }

    [Theory]
    [InlineData((int)GenesisGuiNextTab.New)]
    [InlineData((int)GenesisGuiNextTab.Favorites)]
    [InlineData((int)GenesisGuiNextTab.Playing)]
    public void SearchNavigationReturnsToChannelsAndPreservesQueryAndYellowPage(int startingTabValue)
    {
        var startingTab = (GenesisGuiNextTab)startingTabValue;
        var alpha = Channel("Alpha", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        var alpine = Channel("Alpine", "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB");
        var beta = Channel("Beta", "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC");
        var gamma = Channel("Gamma", "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD");
        var presence = new GenesisChannelPresenceTracker().ApplySuccessfulRefresh(
            Snapshot(
                (alpha, "yp-a", "YP A"),
                (alpine, "yp-a", "YP A"),
                (beta, "yp-a", "YP A"),
                (gamma, "yp-b", "YP B")));
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());
        var selection = AttachSelectionToTabChanges(tabs);
        tabs.ApplyChannelViews(
            GenesisChannelViewProjectionSnapshot.FromPresence(presence),
            presence);
        tabs.SelectedTab = startingTab;
        tabs.SearchText = "Al";
        tabs.SelectedYellowPageId = "yp-a";
        var focusCalls = 0;

        var focused = selection.ActivateSearch(
            tabs,
            () =>
            {
                Assert.Equal(
                    GenesisGuiNextNavigationPresentation.SearchNavigationItemIndex,
                    selection.SelectedItemIndex);
                focusCalls++;
                return true;
            });

        Assert.True(focused);
        Assert.Equal(1, focusCalls);
        Assert.Equal(GenesisGuiNextTab.Channels, tabs.SelectedTab);
        Assert.Equal("Al", tabs.SearchText);
        Assert.Equal("yp-a", tabs.SelectedYellowPageId);
        Assert.Equal(
            GenesisGuiNextNavigationPresentation.SearchNavigationItemIndex,
            selection.SelectedItemIndex);
        Assert.Equal([alpha, alpine], tabs.VisibleChannels);

        tabs.SearchText = string.Empty;

        Assert.Equal([alpha, alpine, beta], tabs.VisibleChannels);
        Assert.Equal("yp-a", tabs.SelectedYellowPageId);
        Assert.Equal(
            GenesisGuiNextNavigationPresentation.SearchNavigationItemIndex,
            selection.SelectedItemIndex);
    }

    [Fact]
    public void SearchActivationFromChannelsPreservesSearchUtilitySelection()
    {
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());
        var selection = AttachSelectionToTabChanges(tabs);
        var focusCalls = 0;

        var focused = selection.ActivateSearch(
            tabs,
            () =>
            {
                focusCalls++;
                return true;
            });

        Assert.True(focused);
        Assert.Equal(1, focusCalls);
        Assert.Equal(GenesisGuiNextTab.Channels, tabs.SelectedTab);
        Assert.Equal(
            GenesisGuiNextNavigationPresentation.SearchNavigationItemIndex,
            selection.SelectedItemIndex);
    }

    [Fact]
    public void NormalNavigationClearsUtilitySelectionEvenWhenChannelsIsAlreadySelected()
    {
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());
        var selection = AttachSelectionToTabChanges(tabs);
        selection.SelectUtility(GenesisGuiNextNavigationPresentation.SearchNavigationItemIndex);

        selection.SelectView(tabs, GenesisGuiNextTab.Channels);

        Assert.Equal(GenesisGuiNextTab.Channels, tabs.SelectedTab);
        Assert.Equal(-1, selection.SelectedItemIndex);
    }

    [Fact]
    public void ProgrammaticTabChangeClearsUtilitySelection()
    {
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());
        var selection = AttachSelectionToTabChanges(tabs);
        selection.SelectUtility(9);

        tabs.SelectedTab = GenesisGuiNextTab.New;

        Assert.Equal(-1, selection.SelectedItemIndex);
    }

    [Fact]
    public void SearchUtilitySelectionTakesPrecedenceOverChannelsTabSelection()
    {
        var selection = new GenesisGuiNextUtilityNavigationSelection();
        selection.SelectUtility(GenesisGuiNextNavigationPresentation.SearchNavigationItemIndex);

        Assert.True(selection.IsSelected(
            GenesisGuiNextNavigationPresentation.SearchNavigationItemIndex,
            GenesisGuiNextTab.Channels));
        Assert.False(selection.IsSelected(
            (int)GenesisGuiNextTab.Channels,
            GenesisGuiNextTab.Channels));

        selection.Clear();

        Assert.True(selection.IsSelected(
            (int)GenesisGuiNextTab.Channels,
            GenesisGuiNextTab.Channels));
        Assert.False(selection.IsSelected(
            GenesisGuiNextNavigationPresentation.SearchNavigationItemIndex,
            GenesisGuiNextTab.Channels));
    }

    private static GenesisGuiNextUtilityNavigationSelection AttachSelectionToTabChanges(
        GenesisGuiNextTabsViewModel tabs)
    {
        var selection = new GenesisGuiNextUtilityNavigationSelection();
        tabs.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(GenesisGuiNextTabsViewModel.SelectedTab))
            {
                selection.OnSelectedTabChanged();
            }
        };
        return selection;
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
