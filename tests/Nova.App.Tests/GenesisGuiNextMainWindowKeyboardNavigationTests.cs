namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextMainWindowKeyboardNavigationTests
{
    [Fact]
    public void RemovedTabKeyDownFilterRejectsUnrelatedMessages()
    {
        Assert.True(GenesisGuiNextMainWindowKeyboardNavigation.IsRemovedTabKeyDown(
            hookCode: 0,
            removalAction: 1,
            message: 0x0100,
            virtualKey: 0x09));
        Assert.False(GenesisGuiNextMainWindowKeyboardNavigation.IsRemovedTabKeyDown(
            hookCode: -1,
            removalAction: 1,
            message: 0x0100,
            virtualKey: 0x09));
        Assert.False(GenesisGuiNextMainWindowKeyboardNavigation.IsRemovedTabKeyDown(
            hookCode: 0,
            removalAction: 0,
            message: 0x0100,
            virtualKey: 0x09));
        Assert.False(GenesisGuiNextMainWindowKeyboardNavigation.IsRemovedTabKeyDown(
            hookCode: 0,
            removalAction: 1,
            message: 0x0101,
            virtualKey: 0x09));
        Assert.False(GenesisGuiNextMainWindowKeyboardNavigation.IsRemovedTabKeyDown(
            hookCode: 0,
            removalAction: 1,
            message: 0x0100,
            virtualKey: 0x0D));
    }

    [Theory]
    [InlineData(4, -1, false, 0)]
    [InlineData(4, -1, true, 3)]
    [InlineData(4, 0, false, 1)]
    [InlineData(4, 3, false, 0)]
    [InlineData(4, 3, true, 2)]
    [InlineData(4, 0, true, 3)]
    [InlineData(0, -1, false, -1)]
    public void ResolveNextIndexWrapsInBothDirections(
        int count,
        int currentIndex,
        bool reverse,
        int expected)
    {
        Assert.Equal(
            expected,
            GenesisGuiNextMainWindowKeyboardNavigation.ResolveNextIndex(
                count,
                currentIndex,
                reverse));
    }

    private static readonly int[] MultiRegionMap = [0, 0, 1, 1, 2];
    private static readonly int[] SingleRegionMap = [0, 0];

    [Fact]
    public void ResolvePaneJumpIndex_JumpsAcrossRegionsForwardAndBackward()
    {
        Assert.Equal(2, GenesisGuiNextMainWindowKeyboardNavigation.ResolvePaneJumpIndex(MultiRegionMap, 0, reverse: false));
        Assert.Equal(2, GenesisGuiNextMainWindowKeyboardNavigation.ResolvePaneJumpIndex(MultiRegionMap, 1, reverse: false));
        Assert.Equal(4, GenesisGuiNextMainWindowKeyboardNavigation.ResolvePaneJumpIndex(MultiRegionMap, 2, reverse: false));
        Assert.Equal(0, GenesisGuiNextMainWindowKeyboardNavigation.ResolvePaneJumpIndex(MultiRegionMap, 4, reverse: false));
        Assert.Equal(4, GenesisGuiNextMainWindowKeyboardNavigation.ResolvePaneJumpIndex(MultiRegionMap, 0, reverse: true));
        Assert.Equal(0, GenesisGuiNextMainWindowKeyboardNavigation.ResolvePaneJumpIndex(MultiRegionMap, 2, reverse: true));
        Assert.Equal(0, GenesisGuiNextMainWindowKeyboardNavigation.ResolvePaneJumpIndex(MultiRegionMap, -1, reverse: false));
        Assert.Equal(-1, GenesisGuiNextMainWindowKeyboardNavigation.ResolvePaneJumpIndex(SingleRegionMap, 0, reverse: false));
    }

    [Fact]
    public void TryHandlePaneJumpKeyDown_HandlesF6AndShiftF6AndRejectsInvalidStates()
    {
        var calls = 0;
        var lastReverse = false;
        bool Jump(bool reverse)
        {
            calls++;
            lastReverse = reverse;
            return true;
        }

        Assert.True(GenesisGuiNextMainWindowKeyboardNavigation.TryHandlePaneJumpKeyDown(
            hookCode: 0,
            removalAction: 1,
            message: 0x0100,
            virtualKey: 0x75,
            keyData: 0,
            isActiveOwnerTarget: true,
            ctrlOrAltPressed: false,
            shiftPressed: false,
            Jump));
        Assert.Equal(1, calls);
        Assert.False(lastReverse);

        Assert.True(GenesisGuiNextMainWindowKeyboardNavigation.TryHandlePaneJumpKeyDown(
            hookCode: 0,
            removalAction: 1,
            message: 0x0100,
            virtualKey: 0x75,
            keyData: 0,
            isActiveOwnerTarget: true,
            ctrlOrAltPressed: false,
            shiftPressed: true,
            Jump));
        Assert.Equal(2, calls);
        Assert.True(lastReverse);

        Assert.True(GenesisGuiNextMainWindowKeyboardNavigation.TryHandlePaneJumpKeyDown(
            hookCode: 0,
            removalAction: 1,
            message: 0x0100,
            virtualKey: 0x75,
            keyData: (nint)(1L << 30),
            isActiveOwnerTarget: true,
            ctrlOrAltPressed: false,
            shiftPressed: false,
            Jump));
        Assert.Equal(2, calls);

        Assert.False(GenesisGuiNextMainWindowKeyboardNavigation.TryHandlePaneJumpKeyDown(
            hookCode: 0,
            removalAction: 1,
            message: 0x0100,
            virtualKey: 0x75,
            keyData: 0,
            isActiveOwnerTarget: true,
            ctrlOrAltPressed: true,
            shiftPressed: false,
            Jump));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void TryHandleSearchShortcutKeyDown_HandlesCtrlFAndRejectsInvalidStates()
    {
        var calls = 0;
        bool FocusSearch()
        {
            calls++;
            return true;
        }

        Assert.True(GenesisGuiNextMainWindowKeyboardNavigation.TryHandleSearchShortcutKeyDown(
            hookCode: 0,
            removalAction: 1,
            message: 0x0100,
            virtualKey: 0x46,
            keyData: 0,
            isActiveOwnerTarget: true,
            ctrlPressed: true,
            shiftOrAltPressed: false,
            FocusSearch));
        Assert.Equal(1, calls);

        Assert.True(GenesisGuiNextMainWindowKeyboardNavigation.TryHandleSearchShortcutKeyDown(
            hookCode: 0,
            removalAction: 1,
            message: 0x0100,
            virtualKey: 0x46,
            keyData: (nint)(1L << 30),
            isActiveOwnerTarget: true,
            ctrlPressed: true,
            shiftOrAltPressed: false,
            FocusSearch));
        Assert.Equal(1, calls);

        Assert.False(GenesisGuiNextMainWindowKeyboardNavigation.TryHandleSearchShortcutKeyDown(
            hookCode: 0,
            removalAction: 1,
            message: 0x0100,
            virtualKey: 0x46,
            keyData: 0,
            isActiveOwnerTarget: true,
            ctrlPressed: false,
            shiftOrAltPressed: false,
            FocusSearch));

        Assert.False(GenesisGuiNextMainWindowKeyboardNavigation.TryHandleSearchShortcutKeyDown(
            hookCode: 0,
            removalAction: 1,
            message: 0x0100,
            virtualKey: 0x46,
            keyData: 0,
            isActiveOwnerTarget: true,
            ctrlPressed: true,
            shiftOrAltPressed: true,
            FocusSearch));
        Assert.Equal(1, calls);
    }
}
