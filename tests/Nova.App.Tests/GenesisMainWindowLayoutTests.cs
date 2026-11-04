namespace PeerCast.Nova.App.Tests;

public sealed class GenesisMainWindowLayoutTests
{
    [Fact]
    public void InitialClientLayoutPreservesExistingShellGeometry()
    {
        Assert.True(GenesisMainWindowLayout.TryResolve(1120, 620, 96,
            out var sidebar, out var status, out var list, out var details));
        Assert.Equal(new(12, 14, 172, 576), sidebar);
        Assert.Equal(new(184, 14, 1108, 42), status);
        Assert.Equal(new(184, 46, 1108, 344), list);
        Assert.Equal(new(184, 352, 1108, 576), details);
    }

    [Theory]
    [InlineData(96u)]
    [InlineData(120u)]
    [InlineData(144u)]
    [InlineData(192u)]
    public void MinimumClientFitsStatusNavigationListAndDetailsAtDpi(uint dpi)
    {
        var width = GenesisMainWindowLayout.Scale(GenesisMainWindowLayout.MinimumClientWidth, dpi);
        var height = GenesisMainWindowLayout.Scale(GenesisMainWindowLayout.MinimumClientHeight, dpi);
        Assert.True(GenesisMainWindowLayout.TryResolve(width, height, dpi,
            out var sidebar, out var status, out var list, out var details));
        Assert.True(GenesisGuiNextAppShellBridge.TryResolveControlLayout(status.Width,
            out _, out _, out _, out _, out var favoriteLeft));
        Assert.Equal(status.Width, favoriteLeft + GenesisGuiNextAppShellBridge.FavoriteButtonWidth);
        Assert.True(GenesisGuiNextAppShellBridge.TryResolvePlaybackButtonLayout(sidebar.Width, sidebar.Height,
            out _, out _, out var playbackTop, out _, out var buttonHeight));
        Assert.True(playbackTop + buttonHeight <= sidebar.Height);
        Assert.True(list.Height >= GenesisGuiNextAppShellPlacement.MinimumListHeight);
        Assert.Equal(GenesisGuiNextAppShellPlacement.DetailsHeight, details.Height);
        Assert.Equal(list.Bottom + GenesisGuiNextAppShellPlacement.DetailsGap, details.Top);
        var actions = GenesisGuiNextAppShellBridge.ResolveDetailsActions(details.Width);
        Assert.True(actions.TextWidth > 0);
        Assert.True(actions.RecordLeft < actions.StopLeft && actions.StopLeft < actions.CopyLeft);
    }

    [Fact]
    public void ExpansionAndRestoreAllocateExtraSpaceToListAndRetainFixedDetails()
    {
        Assert.True(GenesisMainWindowLayout.TryResolve(1120, 620, 96, out var sidebar,
            out _, out var original, out var originalDetails));
        Assert.True(GenesisMainWindowLayout.TryResolve(1920, 1080, 96, out var enlargedSidebar,
            out _, out var enlarged, out var enlargedDetails));
        Assert.Equal(original.Width + 800, enlarged.Width);
        Assert.Equal(original.Height + 460, enlarged.Height);
        Assert.Equal(originalDetails.Height, enlargedDetails.Height);
        Assert.Equal(sidebar.Width, enlargedSidebar.Width);
        Assert.Equal(sidebar.Height + 460, enlargedSidebar.Height);
        Assert.True(GenesisMainWindowLayout.TryResolve(1120, 620, 96, out _, out _, out var restored, out _));
        Assert.Equal(original, restored);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 620)]
    [InlineData(1120, -1)]
    [InlineData(1087, 620)]
    [InlineData(1120, 473)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void InvalidOrUndersizedClientDoesNotProducePartialBounds(int width, int height)
    {
        Assert.False(GenesisMainWindowLayout.TryResolve(width, height, 96,
            out var sidebar, out var status, out var list, out var details));
        Assert.Equal(default, sidebar);
        Assert.Equal(default, status);
        Assert.Equal(default, list);
        Assert.Equal(default, details);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(uint.MaxValue)]
    public void UnavailableDpiUsesExisting96DpiCoordinates(uint dpi)
    {
        Assert.Equal(1088, GenesisMainWindowLayout.Scale(1088, dpi));
        Assert.True(GenesisMainWindowLayout.TryResolve(1120, 620, dpi, out _, out _, out _, out _));
    }
}
