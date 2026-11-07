namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextTopBarTests
{
    private static readonly string[] CompactLabels = ["Relay", "PCP", "↑", "↓", "Port"];
    private static readonly string[] FullLabels = ["PCP Relay", "PCP", "↑", "↓", "Port"];
    [Theory]
    [InlineData(1088, 474, 96u)]
    [InlineData(1120, 620, 96u)]
    [InlineData(1920, 1080, 96u)]
    [InlineData(1088, 474, 120u)]
    [InlineData(1120, 620, 144u)]
    [InlineData(1920, 1080, 192u)]
    [InlineData(1088, 474, 192u)]
    public void ModernControlsFitWithoutTakingSpaceFromListOrCard(int width, int height, uint dpi)
    {
        int S(int value) => GenesisMainWindowLayout.Scale(value, dpi);
        Assert.True(GenesisMainWindowLayout.TryResolve(S(width), S(height), dpi,
            out _, out var top, out var list, out var details));
        Assert.True(GenesisGuiNextAppShellBridge.TryResolveControlLayout(top.Width,
            out var status, out var searchLeft, out var searchWidth, out var refresh, out var favorite, true));
        Assert.True(GenesisChannelSearchBoxBridge.TryResolveControlLayout(searchWidth,
            out var inputWidth, out var filterLeft, true));
        Assert.True(inputWidth >= GenesisChannelSearchBoxBridge.MinimumSearchEditWidth);
        Assert.Equal(GenesisGuiNextTopBarLayout.Height, top.Height - 4);
        Assert.True(S(8) + S(status) < S(searchLeft));
        Assert.True(S(searchLeft + GenesisGuiNextTopBarLayout.SearchLabelWidth) + S(inputWidth) < S(searchLeft + filterLeft));
        Assert.True(S(searchLeft + filterLeft) + S(GenesisGuiNextTopBarLayout.FilterWidth) < S(refresh));
        Assert.True(S(refresh) + S(GenesisGuiNextAppShellBridge.RefreshButtonWidth) < S(favorite));
        Assert.True(S(favorite) + S(GenesisGuiNextTopBarLayout.FavoriteWidth) <= S(top.Width));
        Assert.Equal(28, top.Height);
        Assert.Equal(GenesisGuiNextAppShellPlacement.DetailsHeight, details.Height);
        Assert.True(list.Height >= GenesisGuiNextAppShellPlacement.MinimumListHeight);
        var right = 0;
        for (var i = 0; i < 5; i++)
        {
            var cell = GenesisGuiNextTopBarLayout.StatusCell(status, i);
            Assert.Equal(right, cell.Left);
            Assert.True(cell.Width >= 40);
            right = cell.Left + cell.Width;
        }
        Assert.Equal(status, right);
    }

    [Fact]
    public void ExpansionSharesSpaceBetweenSearchAndStatusAndRestoresDeterministically()
    {
        var minimum = GenesisMainWindowLayout.MinimumContentWidth;
        Assert.True(GenesisGuiNextTopBarLayout.TryResolve(minimum, out var compact, out var left, out var narrow, out _, out _));
        Assert.True(GenesisGuiNextTopBarLayout.TryResolve(minimum + 800, out var full, out _, out var wide, out _, out _));
        Assert.True(full > compact);
        Assert.Equal(GenesisGuiNextTopBarLayout.FullStatusWidth, full);
        Assert.True(wide > narrow);
        Assert.True(GenesisGuiNextTopBarLayout.TryResolve(minimum, out var restored, out var restoredLeft, out var restoredSearch, out _, out _));
        Assert.Equal((compact, left, narrow), (restored, restoredLeft, restoredSearch));
        Assert.False(GenesisGuiNextTopBarLayout.TryResolve(minimum - 1, out _, out _, out _, out _, out _));
    }

    [Fact]
    public void ClassicRetainsOriginalStatusSearchAndButtonCoordinates()
    {
        Assert.True(GenesisGuiNextAppShellBridge.TryResolveControlLayout(924,
            out var status, out var searchLeft, out var searchWidth, out var refresh, out var favorite, false));
        Assert.Equal((300, 316, 388, 712, 792), (status, searchLeft, searchWidth, refresh, favorite));
        Assert.True(GenesisChannelSearchBoxBridge.TryResolveControlLayout(searchWidth, out var input, out var filter, false));
        Assert.Equal((152, 212), (input, filter));
        Assert.Equal("接続 — | P2Pノード数 — | 上り — | 下り — | ポート開放 —",
            GenesisGuiNextTopStatusPresentation.FormatUnavailable());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TopButtonsDistinguishHoverPressFocusAndDisabledWithoutChangingTheirCommands(bool primary)
    {
        var normal = GenesisGuiNextDesign.TopButtonColors(primary, false, false, false, false);
        var hot = GenesisGuiNextDesign.TopButtonColors(primary, false, true, false, false);
        var pressed = GenesisGuiNextDesign.TopButtonColors(primary, false, true, true, false);
        var focused = GenesisGuiNextDesign.TopButtonColors(primary, false, false, false, true);
        Assert.NotEqual(normal.Background, hot.Background);
        Assert.NotEqual(hot.Background, pressed.Background);
        Assert.Equal(normal.Background, focused.Background);
        Assert.Equal(GenesisGuiNextDesign.Accent, focused.Border);
        Assert.Equal(GenesisGuiNextDesign.DisabledText,
            GenesisGuiNextDesign.TopButtonColors(primary, true, true, true, true).Text);
    }

    [Fact]
    public void CompactLabelsKeepEveryUnknownMeasurementAndItsMeaning()
    {
        Assert.Equal(CompactLabels,
            Enumerable.Range(0, 5).Select(i => GenesisGuiNextTopStatusPresentation.Label(i, true)));
        Assert.Equal(FullLabels,
            Enumerable.Range(0, 5).Select(i => GenesisGuiNextTopStatusPresentation.Label(i, false)));
        Assert.Equal("—", GenesisGuiNextTopStatusPresentation.UnknownValue);
    }
}
