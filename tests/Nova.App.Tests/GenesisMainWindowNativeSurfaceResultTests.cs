namespace PeerCast.Nova.App.Tests;

public sealed class GenesisMainWindowNativeSurfaceResultTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(-1, true)]
    public void ItemTextUpdateReflectsNativeResult(int nativeResult, bool expected)
    {
        Assert.Equal(
            expected,
            GenesisMainWindowNativeSurface.IsSuccessfulItemTextUpdate(nativeResult));
    }
}
