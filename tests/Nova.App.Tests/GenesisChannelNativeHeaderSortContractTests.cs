namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelNativeHeaderSortContractTests
{
    [Fact]
    public void HeaderItemClickNotificationsAreRecognizedForAnsiAndUnicode()
    {
        Assert.True(GenesisChannelNativeHeaderSort.IsColumnClick(unchecked((uint)-302)));
        Assert.True(GenesisChannelNativeHeaderSort.IsColumnClick(unchecked((uint)-322)));
        Assert.False(GenesisChannelNativeHeaderSort.IsColumnClick(unchecked((uint)-301)));
        Assert.False(GenesisChannelNativeHeaderSort.IsColumnClick(0));
    }
}
