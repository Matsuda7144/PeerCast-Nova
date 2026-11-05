namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextChannelCardTests
{
    [Fact]
    public void ClassicAndDedicatedTabsNeverUseTheChannelCard()
    {
        foreach (var tab in Enum.GetValues<GenesisGuiNextTab>())
        {
            Assert.False(GenesisGuiNextChannelCardLayout.Applies(false, tab));
            Assert.Equal(tab is GenesisGuiNextTab.Channels or GenesisGuiNextTab.New or GenesisGuiNextTab.Favorites,
                GenesisGuiNextChannelCardLayout.Applies(true, tab));
        }
    }

    [Theory]
    [InlineData(1088, 474, 96u)]
    [InlineData(1120, 620, 96u)]
    [InlineData(1920, 1080, 96u)]
    [InlineData(1088, 474, 144u)]
    [InlineData(1088, 474, 192u)]
    public void CardContentsFitTheExistingPaneWithoutOverlapAtMinimumAndExpandedSizes(int width, int height, uint dpi)
    {
        Assert.True(GenesisMainWindowLayout.TryResolve(GenesisMainWindowLayout.Scale(width, dpi),
            GenesisMainWindowLayout.Scale(height, dpi), dpi, out _, out _, out var list, out var details));
        Assert.Equal(224, details.Height);
        Assert.True(list.Height >= GenesisGuiNextAppShellPlacement.MinimumListHeight);
        var layout = new GenesisGuiNextChannelCardLayout(details.Width);
        var texts = new[] { layout.Title, layout.Description, layout.Metadata, layout.Identity, layout.RecordingStatus, layout.RelayStatus };
        var buttons = new[] { GenesisGuiNextChannelCardLayout.Record, GenesisGuiNextChannelCardLayout.Stop,
            GenesisGuiNextChannelCardLayout.Copy, GenesisGuiNextChannelCardLayout.RelayStart, GenesisGuiNextChannelCardLayout.RelayStop };
        var all = texts.Concat(buttons).ToArray();
        foreach (var bounds in all)
        {
            Assert.InRange(bounds.Left, 7, details.Width - 7);
            Assert.InRange(bounds.Right, bounds.Left + 1, details.Width - 7);
            Assert.InRange(bounds.Top, 7, details.Height - 7);
            Assert.InRange(bounds.Bottom, bounds.Top + 1, details.Height - 7);
            Assert.True(GenesisMainWindowLayout.Scale(bounds.Height, dpi) > 0);
        }
        for (var i = 0; i < all.Length; i++)
            for (var j = i + 1; j < all.Length; j++)
                Assert.True(all[i].Right <= all[j].Left || all[j].Right <= all[i].Left ||
                    all[i].Bottom <= all[j].Top || all[j].Bottom <= all[i].Top, $"Overlap: {i}, {j}");
        Assert.Equal(GenesisGuiNextChannelCardLayout.Record.Height, GenesisGuiNextChannelCardLayout.Copy.Height);
        Assert.True(layout.Title.Right < layout.DividerLeft && layout.Metadata.Left > layout.DividerLeft);
    }

    [Fact]
    public void DisabledActionsDoNotShowActivePlaybackRecordingOrHoverAccents()
    {
        var disabled = GenesisGuiNextDesign.CardButtonColors(false, false, true, false, false);
        foreach (var play in new[] { false, true })
            foreach (var record in new[] { false, true })
                foreach (var hot in new[] { false, true })
                    Assert.Equal(disabled, GenesisGuiNextDesign.CardButtonColors(play, record, true, hot, true));
        Assert.NotEqual(GenesisGuiNextDesign.Text, disabled.Text);
        Assert.NotEqual(GenesisGuiNextDesign.Recording, disabled.Border);
        Assert.NotEqual(GenesisGuiNextDesign.Selected, disabled.Background);
    }

    [Fact]
    public void EnabledActionsDistinguishPlaybackRecordingSecondaryHoverAndPress()
    {
        var play = GenesisGuiNextDesign.CardButtonColors(true, false, false, false, false);
        var record = GenesisGuiNextDesign.CardButtonColors(false, true, false, false, false);
        var copy = GenesisGuiNextDesign.CardButtonColors(false, false, false, false, false);
        Assert.Equal(GenesisGuiNextDesign.Selected, play.Background);
        Assert.Equal(GenesisGuiNextDesign.Recording, record.Border);
        Assert.NotEqual(record.Border, copy.Border);
        Assert.Equal(GenesisGuiNextDesign.Hover, GenesisGuiNextDesign.CardButtonColors(false, true, false, true, false).Background);
        Assert.Equal(GenesisGuiNextDesign.Selected, GenesisGuiNextDesign.CardButtonColors(false, true, false, true, true).Background);
    }
}
