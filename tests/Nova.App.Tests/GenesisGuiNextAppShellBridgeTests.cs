namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextAppShellBridgeTests
{
    private static readonly string[] BaselineNavigationLabels =
    [
        "チャンネル一覧",
        "新着",
        "お気に入り",
        "視聴中",
        "録画",
        "検索",
        "配信",
        "設定",
        "ログ",
        "について",
    ];

    [Fact]
    public void ShellKeepsSidebarLeftOfContentAndReservesTopStatusBar()
    {
        var original = new GenesisGuiNextAppShellPlacement.WindowRectangle(
            184,
            46,
            1184,
            700);

        var resolved = GenesisGuiNextAppShellPlacement.TryResolveLayout(
            original,
            out var sidebar,
            out var statusBar,
            out var movedList,
            out var details);

        Assert.True(resolved);
        Assert.Equal(12, sidebar.Left);
        Assert.Equal(172, sidebar.Right);
        Assert.Equal(original.Top, sidebar.Top);
        Assert.Equal(original.Bottom, sidebar.Bottom);
        Assert.Equal(original.Left, statusBar.Left);
        Assert.Equal(original.Top, statusBar.Top);
        Assert.Equal(GenesisGuiNextAppShellPlacement.StatusHeight, statusBar.Height);
        Assert.Equal(
            original.Top +
                GenesisGuiNextAppShellPlacement.StatusHeight +
                GenesisGuiNextAppShellPlacement.StatusGap,
            movedList.Top);
        Assert.Equal(details.Top - GenesisGuiNextAppShellPlacement.DetailsGap, movedList.Bottom);
        Assert.Equal(original.Left, details.Left);
        Assert.Equal(original.Right, details.Right);
        Assert.Equal(original.Bottom, details.Bottom);
        Assert.Equal(GenesisGuiNextAppShellPlacement.DetailsHeight, details.Height);
    }

    [Fact]
    public void ShellUsesRetiredSourceRowForItsTopEdgeAndReclaimsListSpace()
    {
        var original = new GenesisGuiNextAppShellPlacement.WindowRectangle(
            184,
            46,
            1184,
            700);
        const int sourceRowTop = 10;

        var resolved = GenesisGuiNextAppShellPlacement.TryResolveLayout(
            original,
            sourceRowTop,
            out var sidebar,
            out var statusBar,
            out var movedList,
            out var details);

        Assert.True(resolved);
        Assert.Equal(sourceRowTop, sidebar.Top);
        Assert.Equal(sourceRowTop, statusBar.Top);
        Assert.Equal(
            sourceRowTop + GenesisGuiNextAppShellPlacement.StatusHeight +
                GenesisGuiNextAppShellPlacement.StatusGap,
            movedList.Top);
        Assert.True(movedList.Top < original.Top);
        Assert.Equal(details.Top - GenesisGuiNextAppShellPlacement.DetailsGap, movedList.Bottom);
        Assert.Equal(original.Bottom, details.Bottom);
    }

    [Fact]
    public void ShellIntegratesStatusSearchAndFavoriteIntoOneTopRow()
    {
        const int totalWidth = 1000;

        Assert.True(GenesisGuiNextAppShellBridge.TryResolveControlLayout(
            totalWidth,
            out var statusTextWidth,
            out var searchLeft,
            out var searchWidth,
            out var refreshButtonLeft,
            out var favoriteButtonLeft));

        Assert.Equal(GenesisGuiNextAppShellBridge.MinimumStatusTextWidth, statusTextWidth);
        Assert.True(searchLeft > statusTextWidth);
        Assert.True(searchWidth >= GenesisChannelSearchBoxBridge.MinimumSearchEditWidth);
        Assert.Equal(
            refreshButtonLeft - GenesisGuiNextAppShellBridge.RefreshButtonGap,
            searchLeft + searchWidth);
        Assert.Equal(
            favoriteButtonLeft -
                GenesisGuiNextAppShellBridge.FavoriteButtonGap -
                GenesisGuiNextAppShellBridge.RefreshButtonWidth,
            refreshButtonLeft);
        Assert.Equal(
            totalWidth - GenesisGuiNextAppShellBridge.FavoriteButtonWidth,
            favoriteButtonLeft);
        Assert.True(GenesisChannelSearchBoxBridge.TryResolveControlLayout(
            searchWidth,
            out var searchEditWidth,
            out _));
        Assert.True(searchEditWidth >= GenesisChannelSearchBoxBridge.MinimumSearchEditWidth);
    }

    [Fact]
    public void ShellRejectsListThatWouldBecomeTooShort()
    {
        var original = new GenesisGuiNextAppShellPlacement.WindowRectangle(
            184,
            0,
            584,
            GenesisGuiNextAppShellPlacement.StatusHeight +
                GenesisGuiNextAppShellPlacement.StatusGap +
                GenesisGuiNextAppShellPlacement.MinimumListHeight - 1);

        Assert.False(GenesisGuiNextAppShellPlacement.TryResolveLayout(
            original,
            out _,
            out _,
            out _));
    }

    [Fact]
    public void PlaybackCommandsFitBelowNavigationWithoutOverlappingIt()
    {
        Assert.True(GenesisGuiNextAppShellBridge.TryResolvePlaybackButtonLayout(
            GenesisGuiNextAppShellPlacement.SidebarWidth,
            sidebarHeight: 550,
            out var playLeft,
            out var stopLeft,
            out var top,
            out var buttonWidth,
            out var buttonHeight));

        Assert.Equal(6, playLeft);
        Assert.Equal(6 + buttonWidth + 8, stopLeft);
        Assert.Equal(510, top);
        Assert.Equal(30, buttonHeight);
        Assert.True(buttonWidth >= 48);
        Assert.True(top >= 376);
    }

    [Fact]
    public void PlaybackCommandsAreRejectedWhenSidebarCannotFitThem()
    {
        Assert.False(GenesisGuiNextAppShellBridge.TryResolvePlaybackButtonLayout(
            GenesisGuiNextAppShellPlacement.SidebarWidth,
            sidebarHeight: 415,
            out _,
            out _,
            out _,
            out _,
            out _));
    }

    [Fact]
    public void StatusLayoutReservesFavoriteActionWithoutOverlappingStatusText()
    {
        const int totalWidth = 1000;

        Assert.True(GenesisGuiNextAppShellBridge.TryResolveControlLayout(
            totalWidth,
            out var statusTextWidth,
            out var searchLeft,
            out var searchWidth,
            out var refreshButtonLeft,
            out var favoriteButtonLeft));

        Assert.Equal(
            totalWidth - GenesisGuiNextAppShellBridge.FavoriteButtonWidth,
            favoriteButtonLeft);
        Assert.Equal(GenesisGuiNextAppShellBridge.MinimumStatusTextWidth, statusTextWidth);
        Assert.True(statusTextWidth < searchLeft);
        Assert.Equal(refreshButtonLeft - GenesisGuiNextAppShellBridge.RefreshButtonGap,
            searchLeft + searchWidth);
    }

    [Fact]
    public void StatusLayoutRejectsInsufficientContentWidth()
    {
        var totalWidth =
            8 +
            GenesisGuiNextAppShellBridge.MinimumStatusTextWidth +
            8 +
            52 +
            GenesisChannelSearchBoxBridge.MinimumSearchEditWidth +
            GenesisChannelSearchBoxBridge.YellowPageFilterGap +
            GenesisChannelSearchBoxBridge.YellowPageFilterWidth +
            GenesisGuiNextAppShellBridge.RefreshButtonGap +
            GenesisGuiNextAppShellBridge.RefreshButtonWidth +
            GenesisGuiNextAppShellBridge.FavoriteButtonGap +
            GenesisGuiNextAppShellBridge.FavoriteButtonWidth -
            1;

        Assert.False(GenesisGuiNextAppShellBridge.TryResolveControlLayout(
            totalWidth,
            out _,
            out _,
            out _,
            out _,
            out _));
    }

    [Fact]
    public void NavigationLabelsMatchDesignBaseline()
    {
        Assert.Equal(
            BaselineNavigationLabels,
            GenesisGuiNextNavigationPresentation.Items);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    public void ExistingNavigationItemsRouteToTheirCurrentViews(
        int itemIndex,
        int expected)
    {
        Assert.True(GenesisGuiNextNavigationPresentation.TryResolveExistingView(
            itemIndex,
            out var actual));
        Assert.Equal(expected, (int)actual);
    }

    [Fact]
    public void RecordingNavigationSelectsTheActiveManualRecordingPageAndEmptyStateHasNoRows()
    {
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());
        var selection = new GenesisGuiNextUtilityNavigationSelection();

        Assert.True(GenesisGuiNextNavigationPresentation.TryResolveExistingView(
            4,
            out var recordingTab));
        selection.SelectView(tabs, recordingTab);

        Assert.Equal(GenesisGuiNextTab.Recording, tabs.SelectedTab);
        Assert.True(selection.IsSelected(4, tabs.SelectedTab));
        Assert.Empty(GenesisGuiNextNativeTabProjection.Create(tabs));
    }

    [Theory]
    [InlineData(6)]
    public void FutureNavigationItemsRemainUnavailableUntilTheirViewsExist(int itemIndex)
    {
        Assert.False(GenesisGuiNextNavigationPresentation.IsImplemented(itemIndex));
        Assert.False(GenesisGuiNextNavigationPresentation.TryResolveExistingView(
            itemIndex,
            out _));
    }

    [Fact]
    public void SearchNavigationIsImplementedAndResolvesWithoutEnablingOtherFutureItems()
    {
        Assert.True(GenesisGuiNextNavigationPresentation.IsImplemented(5));
        Assert.True(GenesisGuiNextNavigationPresentation.TryResolvePrimaryCommand(
            5,
            out var command));
        Assert.Equal("Search", command.ToString());

        foreach (var itemIndex in new[] { 6 })
        {
            Assert.False(GenesisGuiNextNavigationPresentation.IsImplemented(itemIndex));
            Assert.False(GenesisGuiNextNavigationPresentation.TryResolvePrimaryCommand(
                itemIndex,
                out _));
        }
    }

    [Theory]
    [InlineData(7, "Settings")]
    [InlineData(9, "About")]
    public void SettingsAndAboutNavigationItemsDispatchExistingCommands(
        int itemIndex,
        string expectedCommand)
    {
        Assert.True(GenesisGuiNextNavigationPresentation.IsImplemented(itemIndex));
        Assert.True(GenesisGuiNextNavigationPresentation.TryResolvePrimaryCommand(
            itemIndex,
            out var actualCommand));
        Assert.Equal(expectedCommand, actualCommand.ToString());
    }

    [Theory]
    [InlineData(0x0201u)]
    [InlineData(0x0202u)]
    [InlineData(0x0204u)]
    [InlineData(0x0205u)]
    [InlineData(0x0100u)]
    [InlineData(0x0101u)]
    [InlineData(0x102Bu)]
    public void FavoriteButtonTracksMessagesThatCanChangeListSelection(uint message)
    {
        Assert.True(
            GenesisGuiNextAppShellBridge.RequiresFavoriteButtonSynchronization(message));
    }

    [Fact]
    public void FavoriteButtonIgnoresUnrelatedListMessages()
    {
        Assert.False(
            GenesisGuiNextAppShellBridge.RequiresFavoriteButtonSynchronization(0x000Fu));
    }

    [Fact]
    public void ListViewSelectedRowAccentUsesVisibleWidthInsideItemBounds()
    {
        Assert.True(GenesisGuiNextAppShellBridge.TryResolveSelectedRowAccent(
            selected: true,
            left: 10,
            top: 20,
            right: 100,
            bottom: 32,
            out var accent));

        Assert.Equal(10, accent.Left);
        Assert.Equal(20, accent.Top);
        Assert.Equal(15, accent.Right);
        Assert.Equal(32, accent.Bottom);
    }

    [Fact]
    public void ListViewSelectedRowAccentIsSkippedForUnselectedOrInvalidItems()
    {
        Assert.False(GenesisGuiNextAppShellBridge.TryResolveSelectedRowAccent(
            selected: false,
            left: 10,
            top: 20,
            right: 100,
            bottom: 32,
            out _));
        Assert.False(GenesisGuiNextAppShellBridge.TryResolveSelectedRowAccent(
            selected: true,
            left: 10,
            top: 20,
            right: 10,
            bottom: 32,
            out _));
        Assert.False(GenesisGuiNextAppShellBridge.TryResolveSelectedRowAccent(
            selected: true,
            left: 10,
            top: 20,
            right: 100,
            bottom: 20,
            out _));
    }
}
