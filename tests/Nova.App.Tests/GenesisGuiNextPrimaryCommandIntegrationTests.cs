namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextPrimaryCommandIntegrationTests
{
    [Fact]
    public void ShellAndKeyboardRefreshUseTheMainWindowRefreshBoundary()
    {
        var shell = ReadSource("GenesisGuiNextAppShellBridge.cs");
        var refreshButton = ReadSource("GenesisChannelRefreshButtonBridge.cs");
        var keyboard = ReadSource("GenesisChannelDirectContextMenuBridge.cs");
        var sourceKeyboard = ReadSource("GenesisSourceEnterBridge.cs");
        var contextMenu = ReadSource("GenesisChannelContextMenuBridge.cs");
        var interaction = ReadSource("GenesisMainWindowInteraction.cs");

        Assert.Contains("TryRefreshForOwner(_owner)", shell, StringComparison.Ordinal);
        Assert.Contains("CanRefreshForOwner(_owner)", shell, StringComparison.Ordinal);
        Assert.Contains("new GenesisMainWindowInteraction(viewModel, session)", refreshButton, StringComparison.Ordinal);
        Assert.Contains("_interaction.RefreshCurrentViewAsync()", refreshButton, StringComparison.Ordinal);
        Assert.Contains("_interaction.RefreshCurrentViewAsync()", keyboard, StringComparison.Ordinal);
        Assert.Contains("_interaction.RefreshCurrentViewAsync()", sourceKeyboard, StringComparison.Ordinal);
        Assert.Contains("_interaction.RefreshCurrentViewAsync()", contextMenu, StringComparison.Ordinal);
        Assert.Contains("CanRefreshCurrentView", refreshButton, StringComparison.Ordinal);
        Assert.Contains("CanRefreshCurrentView", interaction, StringComparison.Ordinal);
        Assert.Contains("ReloadRecordingRulesAsync()", interaction, StringComparison.Ordinal);
        Assert.Contains("_channelCatalogSession.RefreshAsync(cancellationToken)", interaction, StringComparison.Ordinal);
    }

    [Fact]
    public void GuiNextHidesLegacySettingsAboutAndRefreshButtons()
    {
        var factory = ReadSource("SystemGenesisRefreshAwareInteractionBridgeFactory.cs");
        var surface = ReadSource("SystemGenesisInteractiveMainWindowSurfaceTarget.cs");

        Assert.Contains("showButton: guiNextTabs is null),", factory, StringComparison.Ordinal);
        Assert.Contains("aboutPlacement?.Open(showButton: guiNextTabs is null)", factory, StringComparison.Ordinal);
        Assert.Contains("showButton: false", surface, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchNavigationUsesChannelsProjectionAndFocusesOwnedSearchEdit()
    {
        var shell = ReadSource("GenesisGuiNextAppShellBridge.cs");
        var searchBox = ReadSource("GenesisChannelSearchBoxBridge.cs");

        Assert.Contains(
            "SearchNavigationItemIndex => GenesisGuiNextPrimaryCommand.Search,",
            shell,
            StringComparison.Ordinal);
        Assert.Contains(
            "primaryCommand == GenesisGuiNextPrimaryCommand.Search",
            shell,
            StringComparison.Ordinal);
        Assert.Contains(
            "_utilityNavigationSelection.ActivateSearch(",
            shell,
            StringComparison.Ordinal);
        Assert.Contains("_searchBox.Focus", shell, StringComparison.Ordinal);
        Assert.Contains("_utilityNavigationSelection.SelectUtility(itemIndex);", shell, StringComparison.Ordinal);
        Assert.Contains("_utilityNavigationSelection.SelectView(_tabs, selectedTab);", shell, StringComparison.Ordinal);
        Assert.Contains("_utilityNavigationSelection.OnSelectedTabChanged();", shell, StringComparison.Ordinal);

        var helperStart = shell.IndexOf(
            "internal sealed class GenesisGuiNextUtilityNavigationSelection",
            StringComparison.Ordinal);
        Assert.True(helperStart >= 0);
        var helperEnd = shell.IndexOf(
            "internal sealed record GenesisGuiNextFavoriteButtonPresentation",
            helperStart,
            StringComparison.Ordinal);
        Assert.True(helperEnd > helperStart);
        var helper = shell[helperStart..helperEnd];
        Assert.Contains("tabs.SelectedTab = GenesisGuiNextTab.Channels;", helper, StringComparison.Ordinal);
        Assert.Contains("SelectUtility(GenesisGuiNextNavigationPresentation.SearchNavigationItemIndex);", helper, StringComparison.Ordinal);
        Assert.Contains("focusSearchEdit()", helper, StringComparison.Ordinal);
        Assert.DoesNotContain("tabs.SearchText =", helper, StringComparison.Ordinal);
        Assert.DoesNotContain("tabs.SelectedYellowPageId =", helper, StringComparison.Ordinal);

        var focusStart = searchBox.IndexOf("public bool Focus()", StringComparison.Ordinal);
        Assert.True(focusStart >= 0);
        var focusEnd = searchBox.IndexOf("internal static bool TryResolveControlLayout", focusStart, StringComparison.Ordinal);
        Assert.True(focusEnd > focusStart);
        var focus = searchBox[focusStart..focusEnd];
        Assert.Contains("Volatile.Read(ref _disposed) != 0", focus, StringComparison.Ordinal);
        Assert.Contains("!IsWindow(_edit)", focus, StringComparison.Ordinal);
        Assert.Contains("SetFocus(_edit)", focus, StringComparison.Ordinal);
        Assert.Contains("GetFocus() == _edit", focus, StringComparison.Ordinal);
    }

    private static string ReadSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var sourcePath = Path.Combine(directory.FullName, "src", "Nova.App", fileName);
            if (File.Exists(sourcePath))
            {
                return File.ReadAllText(sourcePath);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Repository source was not found: {fileName}");
    }
}
