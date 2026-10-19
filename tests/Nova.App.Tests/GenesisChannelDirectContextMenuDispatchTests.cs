namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelDirectContextMenuDispatchTests
{
    [Fact]
    public void RenderedRowOpensEvenWhenSelectionSynchronizationFails()
    {
        var channel = new GenesisChannelItem(
            "Channel",
            "pcp://example/channel");
        var row = new GenesisGuiNextNativeTabRow(channel, channel);
        GenesisGuiNextNativeTabRow? opened = null;
        var selectionAttempts = 0;

        var result = GenesisChannelDirectContextMenuDispatch.TryOpen(
            row,
            candidate =>
            {
                Assert.Same(row, candidate);
                selectionAttempts++;
                return false;
            },
            candidate => opened = candidate);

        Assert.True(result);
        Assert.Equal(1, selectionAttempts);
        Assert.Same(row, opened);
    }

    [Fact]
    public void MissingRenderedRowFailsClosed()
    {
        var selectionAttempted = false;
        var menuOpened = false;

        var result = GenesisChannelDirectContextMenuDispatch.TryOpen(
            row: null,
            _ =>
            {
                selectionAttempted = true;
                return true;
            },
            _ => menuOpened = true);

        Assert.False(result);
        Assert.False(selectionAttempted);
        Assert.False(menuOpened);
    }

    [Fact]
    public void OfflineRenderedRowRemainsValidContextTarget()
    {
        var display = new GenesisChannelItem(
            "Offline favorite",
            "pcp://example/offline");
        var row = new GenesisGuiNextNativeTabRow(
            display,
            InteractiveChannel: null);
        GenesisGuiNextNativeTabRow? opened = null;

        var result = GenesisChannelDirectContextMenuDispatch.TryOpen(
            row,
            _ => true,
            candidate => opened = candidate);

        Assert.True(result);
        Assert.Same(row, opened);
        Assert.Null(opened!.InteractiveChannel);
    }

    [Fact]
    public void PointerRowFallsBackToCurrentProjectionWhenRegistryIsLate()
    {
        var first = new GenesisChannelItem(
            "First",
            "pcp://example/first");
        var second = new GenesisChannelItem(
            "Second",
            "pcp://example/second");
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([first, second]);

        var row = GenesisChannelDirectContextMenuRowResolver.Resolve(
            rowIndex: 1,
            registeredRow: null,
            viewModel,
            guiNextTabs: null);

        Assert.NotNull(row);
        Assert.Same(second, row.DisplayChannel);
        Assert.Same(second, row.InteractiveChannel);
    }

    [Fact]
    public void PointerRowKeepsRegisteredIdentityWhenAvailable()
    {
        var registeredChannel = new GenesisChannelItem(
            "Registered",
            "pcp://example/registered");
        var registeredRow = new GenesisGuiNextNativeTabRow(
            registeredChannel,
            registeredChannel);
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([
            new GenesisChannelItem("Current", "pcp://example/current"),
        ]);

        var row = GenesisChannelDirectContextMenuRowResolver.Resolve(
            rowIndex: 0,
            registeredRow,
            viewModel,
            guiNextTabs: null);

        Assert.Same(registeredRow, row);
    }

    [Theory]
    [InlineData(0x79, true, false, "ContextMenu")]
    [InlineData(0x5D, false, false, "ContextMenu")]
    [InlineData(0x79, false, false, "None")]
    [InlineData(0x43, false, true, "CopySource")]
    [InlineData(0x43, false, false, "None")]
    [InlineData(0x0D, false, false, "Play")]
    [InlineData(0x74, false, false, "Refresh")]
    [InlineData(0x1B, false, false, "Stop")]
    public void PreferredDirectRouteMapsKeyboardCommands(
        int virtualKey,
        bool shiftPressed,
        bool controlPressed,
        string expected)
    {
        Assert.Equal(
            expected,
            GenesisChannelDirectKeyboard.Resolve(
                (ushort)virtualKey,
                shiftPressed,
                controlPressed).ToString());
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(7144, false)]
    public void NativeContextMenuUsesMinusOneForKeyboardInvocation(
        long lParam,
        bool expectedKeyboard)
    {
        Assert.Equal(
            expectedKeyboard,
            GenesisChannelDirectContextMenuMessage.IsKeyboardInvocation((nint)lParam));
    }
}
