namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextRenderedChannelRowsTests
{
    [Fact]
    public void RenderedRegistrySeparatesDisplayRowsFromInteractiveRows()
    {
        var listView = (nint)0x71442300;
        var live = new GenesisChannelItem(
            "Live",
            "pcp://example/live");
        var offlineDisplay = new GenesisChannelItem(
            "Offline favorite",
            "pcp://example/stale");
        var rows = new GenesisGuiNextNativeTabRow[]
        {
            new(live, live),
            new(offlineDisplay, InteractiveChannel: null),
        };

        try
        {
            GenesisChannelListRenderedChannels.SetRows(listView, rows);

            var liveRow = Assert.IsType<GenesisGuiNextNativeTabRow>(
                GenesisChannelListRenderedChannels.FindRow(listView, 0));
            Assert.Same(live, liveRow.DisplayChannel);
            Assert.Same(live, liveRow.InteractiveChannel);
            Assert.Same(live, GenesisChannelListRenderedChannels.Find(listView, 0));

            var offlineRow = Assert.IsType<GenesisGuiNextNativeTabRow>(
                GenesisChannelListRenderedChannels.FindRow(listView, 1));
            Assert.Same(offlineDisplay, offlineRow.DisplayChannel);
            Assert.Null(offlineRow.InteractiveChannel);
            Assert.Null(GenesisChannelListRenderedChannels.Find(listView, 1));
        }
        finally
        {
            GenesisChannelListRenderedChannels.Remove(listView);
        }
    }
}
