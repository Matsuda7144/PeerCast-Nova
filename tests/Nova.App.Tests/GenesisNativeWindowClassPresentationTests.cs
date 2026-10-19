namespace PeerCast.Nova.App.Tests;

public sealed class GenesisNativeWindowClassPresentationTests
{
    [Fact]
    public void WindowBackgroundUsesWin32ColorWindowClassBrush()
    {
        Assert.Equal((nint)6, GenesisNativeWindowClassPresentation.WindowBackgroundBrush);
    }
}
