namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelRefreshButtonPlacementTests
{
    [Fact]
    public void RefreshOpenUsesCurrentSourceParentAfterReparenting()
    {
        var sourceEdit = (nint)123;
        var currentParent = (nint)456;
        var calls = 0;

        var resolved = GenesisChannelRefreshButtonPlacement.ResolveParentForOpen(
            sourceEdit,
            window =>
            {
                calls++;
                Assert.Equal(sourceEdit, window);
                return currentParent;
            });

        Assert.Equal(currentParent, resolved);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void RefreshOpenRejectsMissingSourceWithoutParentLookup()
    {
        var calls = 0;

        var resolved = GenesisChannelRefreshButtonPlacement.ResolveParentForOpen(
            0,
            _ =>
            {
                calls++;
                return (nint)456;
            });

        Assert.Equal(0, resolved);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void RefreshListButtonReservesExpandedWidth()
    {
        Assert.Equal(104, GenesisChannelRefreshButtonPlacement.RefreshWidth);
    }
}
