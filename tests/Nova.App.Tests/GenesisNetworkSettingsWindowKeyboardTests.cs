namespace PeerCast.Nova.App.Tests;

public sealed class GenesisNetworkSettingsWindowKeyboardTests
{
    private const uint WindowMessageKeyDown = 0x0100;
    private const uint WindowMessageKeyUp = 0x0101;
    private const nuint VirtualKeyEnter = 0x0d;
    private const nuint VirtualKeyEscape = 0x1b;
    private const nuint VirtualKeySpace = 0x20;

    [Theory]
    [InlineData(0x0d)]
    [InlineData(0x1b)]
    public void CloseKeysRequestCloseExactlyOnce(int virtualKey)
    {
        var parentWindow = (nint)0x7144;
        var postedWindow = nint.Zero;
        var postCalls = 0;

        var requested = GenesisNetworkSettingsWindow.TryRequestCloseFromButtonKey(
            WindowMessageKeyDown,
            (nuint)virtualKey,
            parentWindow,
            window =>
            {
                postCalls++;
                postedWindow = window;
                return true;
            });

        Assert.True(requested);
        Assert.Equal(1, postCalls);
        Assert.Equal(parentWindow, postedWindow);
    }

    [Fact]
    public void SpaceIsNotIntercepted()
    {
        var postCalls = 0;

        var requested = GenesisNetworkSettingsWindow.TryRequestCloseFromButtonKey(
            WindowMessageKeyDown,
            VirtualKeySpace,
            (nint)0x7144,
            _ =>
            {
                postCalls++;
                return true;
            });

        Assert.False(requested);
        Assert.Equal(0, postCalls);
    }

    [Theory]
    [InlineData(0x0d)]
    [InlineData(0x1b)]
    public void CloseKeyUpIsNotIntercepted(int virtualKey)
    {
        var postCalls = 0;

        var requested = GenesisNetworkSettingsWindow.TryRequestCloseFromButtonKey(
            WindowMessageKeyUp,
            (nuint)virtualKey,
            (nint)0x7144,
            _ =>
            {
                postCalls++;
                return true;
            });

        Assert.False(requested);
        Assert.Equal(0, postCalls);
    }

    [Fact]
    public void MissingParentDoesNotRequestClose()
    {
        var postCalls = 0;

        var requested = GenesisNetworkSettingsWindow.TryRequestCloseFromButtonKey(
            WindowMessageKeyDown,
            VirtualKeyEscape,
            0,
            _ =>
            {
                postCalls++;
                return true;
            });

        Assert.False(requested);
        Assert.Equal(0, postCalls);
    }

    [Fact]
    public void PostCloseCallbackIsRequired()
    {
        Assert.Throws<ArgumentNullException>(() =>
            GenesisNetworkSettingsWindow.TryRequestCloseFromButtonKey(
                WindowMessageKeyDown,
                VirtualKeyEnter,
                (nint)0x7144,
                null!));
    }
}
