namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelDirectContextMenuEmptyListTests
{
    [Fact]
    public void ClassicChannelListAllowsEmptyListMenuUnlessViewingRecordingHistory()
    {
        Assert.True(GenesisChannelDirectContextMenuDispatch.CanOpenEmptyListMenu(
            guiNextTabs: null,
            isRecordingHistoryView: false));
        Assert.False(GenesisChannelDirectContextMenuDispatch.CanOpenEmptyListMenu(
            guiNextTabs: null,
            isRecordingHistoryView: true));
    }

    [Fact]
    public void GuiNextEmptyListMenuOpensOnlyOnChannelCatalogTabs()
    {
        (GenesisGuiNextTab Tab, bool Expected)[] cases =
        [
            (GenesisGuiNextTab.Channels, true),
            (GenesisGuiNextTab.New, true),
            (GenesisGuiNextTab.Favorites, true),
            (GenesisGuiNextTab.Playing, false),
            (GenesisGuiNextTab.Recording, false),
        ];

        foreach (var (tab, expected) in cases)
        {
            var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection())
            {
                SelectedTab = tab,
            };

            Assert.Equal(
                expected,
                GenesisChannelDirectContextMenuDispatch.CanOpenEmptyListMenu(
                    tabs,
                    isRecordingHistoryView: false));
        }
    }
}
