namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelSearchBoxPlacementTests
{
    [Fact]
    public void SearchControlsReserveYellowPageFilterAtRightEdge()
    {
        const int totalWidth = 1096;

        Assert.True(GenesisChannelSearchBoxBridge.TryResolveControlLayout(
            totalWidth,
            out var editWidth,
            out var yellowPageFilterLeft));

        Assert.Equal(
            totalWidth - GenesisChannelSearchBoxBridge.YellowPageFilterWidth,
            yellowPageFilterLeft);
        Assert.Equal(
            yellowPageFilterLeft -
                GenesisChannelSearchBoxBridge.YellowPageFilterGap -
                52,
            editWidth);
        Assert.True(editWidth >= GenesisChannelSearchBoxBridge.MinimumSearchEditWidth);
    }

    [Fact]
    public void SearchControlsRejectWidthThatWouldCollapseSearchEdit()
    {
        var totalWidth =
            52 +
            GenesisChannelSearchBoxBridge.MinimumSearchEditWidth +
            GenesisChannelSearchBoxBridge.YellowPageFilterGap +
            GenesisChannelSearchBoxBridge.YellowPageFilterWidth - 1;

        Assert.False(GenesisChannelSearchBoxBridge.TryResolveControlLayout(
            totalWidth,
            out _,
            out _));
    }

    [Fact]
    public void NativeYellowPageSelectionMapsAllAndProviderOptions()
    {
        GenesisGuiNextYellowPageFilterOption[] options =
        [
            new("yp-a", "YP A"),
            new("yp-b", "YP B"),
        ];

        Assert.Equal(
            0,
            GenesisChannelSearchBoxBridge.ResolveYellowPageSelectionIndex(
                options,
                selectedYellowPageId: null));
        Assert.Equal(
            2,
            GenesisChannelSearchBoxBridge.ResolveYellowPageSelectionIndex(
                options,
                "yp-b"));

        Assert.True(GenesisChannelSearchBoxBridge.TryResolveYellowPageId(
            options,
            0,
            out var allId));
        Assert.Null(allId);

        Assert.True(GenesisChannelSearchBoxBridge.TryResolveYellowPageId(
            options,
            1,
            out var firstId));
        Assert.Equal("yp-a", firstId);

        Assert.False(GenesisChannelSearchBoxBridge.TryResolveYellowPageId(
            options,
            3,
            out _));
    }
}
