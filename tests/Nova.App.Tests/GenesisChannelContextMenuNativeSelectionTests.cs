namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelContextMenuNativeSelectionTests
{
    [Fact]
    public void KeyboardMenuPlacementUsesRenderedSelectionDuringCatalogReorder()
    {
        var selectedRowReads = 0;

        var row = GenesisChannelContextMenuNative.ResolveKeyboardMenuRow(
            (nint)7144,
            requestedRow: 1,
            window =>
            {
                Assert.Equal((nint)7144, window);
                selectedRowReads++;
                return 0;
            });

        Assert.Equal(0, row);
        Assert.Equal(1, selectedRowReads);
    }

    [Fact]
    public void KeyboardMenuPlacementFallsBackWhenNativeSelectionIsUnavailable()
    {
        var row = GenesisChannelContextMenuNative.ResolveKeyboardMenuRow(
            (nint)7144,
            requestedRow: 1,
            static _ => -1);

        Assert.Equal(1, row);
    }

    [Fact]
    public void PointerMenuDoesNotDependOnSelectionSynchronization()
    {
        var menuOpened = false;

        var result = GenesisChannelContextMenuPointer.TryOpenAfterSelection(
            selectionSynchronized: false,
            () => menuOpened = true);

        Assert.True(result);
        Assert.True(menuOpened);
    }

    [Fact]
    public void KeyboardMenuTargetUsesRenderedIdentityFromResolvedNativeRow()
    {
        var nativeChannel = new GenesisChannelItem(
            "Native",
            "pcp://example/native");
        var listView = (nint)7144;

        var target = GenesisChannelContextMenuKeyboard.ResolveTarget(
            listView,
            requestedRow: 1,
            (window, requestedRow) =>
            {
                Assert.Equal(listView, window);
                Assert.Equal(1, requestedRow);
                return 0;
            },
            (window, row) =>
            {
                Assert.Equal(listView, window);
                Assert.Equal(0, row);
                return nativeChannel;
            });

        Assert.NotNull(target);
        Assert.Equal(0, target.Value.Row);
        Assert.Same(nativeChannel, target.Value.Channel);
    }

    [Fact]
    public void KeyboardMenuTargetFailsClosedWhenResolvedRowHasNoRenderedIdentity()
    {
        var renderedLookupCalled = false;

        var target = GenesisChannelContextMenuKeyboard.ResolveTarget(
            (nint)7144,
            requestedRow: 1,
            static (_, _) => 0,
            (_, row) =>
            {
                Assert.Equal(0, row);
                renderedLookupCalled = true;
                return null;
            });

        Assert.Null(target);
        Assert.True(renderedLookupCalled);
    }

    [Fact]
    public void RenderedKeyboardTargetKeepsOfflineDisplayRowAvailable()
    {
        var listView = (nint)7144;
        var offlineDisplay = new GenesisChannelItem(
            "Offline favorite",
            "pcp://example/offline");
        var offlineRow = new GenesisGuiNextNativeTabRow(
            offlineDisplay,
            InteractiveChannel: null);

        var target = GenesisChannelContextMenuKeyboard.ResolveRenderedTarget(
            listView,
            (window, requestedRow) =>
            {
                Assert.Equal(listView, window);
                Assert.Equal(int.MaxValue, requestedRow);
                return 2;
            },
            (window, row) =>
            {
                Assert.Equal(listView, window);
                Assert.Equal(2, row);
                return offlineRow;
            });

        Assert.NotNull(target);
        Assert.Equal(2, target.Value.Row);
        Assert.Same(offlineDisplay, target.Value.RenderedRow.DisplayChannel);
        Assert.Null(target.Value.RenderedRow.InteractiveChannel);
    }

    [Fact]
    public void RenderedKeyboardTargetRetainsLiveInteractiveIdentity()
    {
        var live = new GenesisChannelItem(
            "Live",
            "pcp://example/live");
        var liveRow = new GenesisGuiNextNativeTabRow(live, live);

        var target = GenesisChannelContextMenuKeyboard.ResolveRenderedTarget(
            (nint)7144,
            static (_, _) => 1,
            (_, row) => row == 1 ? liveRow : null);

        Assert.NotNull(target);
        Assert.Same(live, target.Value.RenderedRow.DisplayChannel);
        Assert.Same(live, target.Value.RenderedRow.InteractiveChannel);
    }

    [Fact]
    public void RenderedKeyboardTargetFailsClosedWithoutNativeSelection()
    {
        var renderedLookupCalled = false;

        var target = GenesisChannelContextMenuKeyboard.ResolveRenderedTarget(
            (nint)7144,
            static (_, requestedRow) => requestedRow,
            (_, _) =>
            {
                renderedLookupCalled = true;
                return null;
            });

        Assert.Null(target);
        Assert.False(renderedLookupCalled);
    }
}
