namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextRefreshSearchSelectionContractTests
{
    private const string StableChannelId =
        "0123456789ABCDEF0123456789ABCDEF";

    [Fact]
    public void RefreshReplacementRestoresSelectedStableIdentityToCurrentRow()
    {
        var selectedBeforeRefresh = new GenesisChannelItem(
            "Before refresh",
            "pcp://example/before-refresh",
            Description: "Old metadata",
            Type: "FLV",
            ChannelId: StableChannelId);
        var currentAfterRefresh = new GenesisChannelItem(
            "After refresh",
            "pcp://example/after-refresh",
            Description: "Current metadata",
            Type: "FLV",
            ChannelId: StableChannelId);
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([currentAfterRefresh]);
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());
        tabs.ApplyChannelViews(
            new GenesisChannelViewProjectionSnapshot([currentAfterRefresh], []));

        var rows = SystemGenesisMainWindowInteractionBridgeFactory
            .CreateChannelsRowsForNativeList(viewModel.Channels, tabs);
        var selectedRow = SystemGenesisMainWindowInteractionBridgeFactory
            .FindSelectedRow(rows, selectedBeforeRefresh);

        Assert.Equal(0, selectedRow);
        var row = Assert.Single(rows);
        Assert.Same(currentAfterRefresh, row.DisplayChannel);
        Assert.Same(currentAfterRefresh, row.InteractiveChannel);
        Assert.NotSame(selectedBeforeRefresh, currentAfterRefresh);
    }

    [Fact]
    public void SearchFilteringSelectedIdentityOutLeavesNoRestorableRow()
    {
        var selectedBeforeRefresh = new GenesisChannelItem(
            "Selected before refresh",
            "pcp://example/selected-before-refresh",
            ChannelId: StableChannelId);
        var currentAfterRefresh = new GenesisChannelItem(
            "Selected current",
            "pcp://example/selected-current",
            ChannelId: StableChannelId);
        var other = new GenesisChannelItem(
            "Other visible",
            "pcp://example/other",
            ChannelId: "FEDCBA9876543210FEDCBA9876543210");
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([currentAfterRefresh, other]);
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());
        tabs.ApplyChannelViews(
            new GenesisChannelViewProjectionSnapshot(
                [currentAfterRefresh, other],
                []));
        tabs.SearchText = "Other visible";

        var rows = SystemGenesisMainWindowInteractionBridgeFactory
            .CreateChannelsRowsForNativeList(viewModel.Channels, tabs);
        var selectedRow = SystemGenesisMainWindowInteractionBridgeFactory
            .FindSelectedRow(rows, selectedBeforeRefresh);

        var row = Assert.Single(rows);
        Assert.Same(other, row.DisplayChannel);
        Assert.Same(other, row.InteractiveChannel);
        Assert.Equal(-1, selectedRow);
    }

    [Fact]
    public void NativeRefreshBoundaryRestoresOrClearsManagedSelectionFailClosed()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "Nova.App",
            "SystemGenesisMainWindowInteractionBridgeFactory.cs"));
        var refreshControls = ExtractMethod(
            source,
            "public void RefreshControls(");
        var restoreSelection = ExtractMethod(
            source,
            "private void RestoreSelectedChannel(");

        Assert.Contains(
            "channelsFilterChanged",
            refreshControls,
            StringComparison.Ordinal);
        Assert.Contains(
            "GenesisMainWindowNativeSurface.ReplaceRows(",
            refreshControls,
            StringComparison.Ordinal);
        Assert.Contains(
            "RestoreSelectedChannel();",
            refreshControls,
            StringComparison.Ordinal);

        Assert.Contains(
            "FindSelectedRow(_renderedRows, selectedChannel)",
            restoreSelection,
            StringComparison.Ordinal);
        Assert.Contains(
            "GenesisChannelListNativeSelection.RestoreRow(",
            restoreSelection,
            StringComparison.Ordinal);
        Assert.Contains(
            "_guiNextTabs.SelectedTab != GenesisGuiNextTab.Recording",
            restoreSelection,
            StringComparison.Ordinal);
        Assert.Contains(
            "_viewModel.SelectedChannel = null;",
            restoreSelection,
            StringComparison.Ordinal);
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method was not found: {signature}");

        var openingBrace = source.IndexOf('{', start);
        Assert.True(openingBrace >= 0, $"Method body was not found: {signature}");

        var depth = 0;
        for (var index = openingBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source[start..(index + 1)];
            }
        }

        throw new InvalidOperationException(
            $"Method body was incomplete: {signature}");
    }

    private static string FindRepositoryFile(params string[] relativeParts) =>
        RepositorySourceTestHelper.FindRepositoryFile(relativeParts);
}
