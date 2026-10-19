namespace PeerCast.Nova.App.Tests;

public sealed class GenesisNetworkSettingsWindowFocusTests
{
    [Fact]
    public void ZeroHandleDoesNotRequestInitialFocus()
    {
        var focusCalls = 0;

        var requested = GenesisNetworkSettingsWindow.RequestInitialFocus(
            0,
            _ =>
            {
                focusCalls++;
                return 0;
            });

        Assert.False(requested);
        Assert.Equal(0, focusCalls);
    }

    [Fact]
    public void CloseButtonRequestsInitialFocusExactlyOnce()
    {
        var focusedControl = nint.Zero;
        var focusCalls = 0;
        var closeButton = (nint)0x7144;

        var requested = GenesisNetworkSettingsWindow.RequestInitialFocus(
            closeButton,
            control =>
            {
                focusCalls++;
                focusedControl = control;
                return 0;
            });

        Assert.True(requested);
        Assert.Equal(1, focusCalls);
        Assert.Equal(closeButton, focusedControl);
    }

    [Fact]
    public void InitialFocusCallbackIsRequired()
    {
        Assert.Throws<ArgumentNullException>(() =>
            GenesisNetworkSettingsWindow.RequestInitialFocus(
                (nint)0x7144,
                null!));
    }
}
