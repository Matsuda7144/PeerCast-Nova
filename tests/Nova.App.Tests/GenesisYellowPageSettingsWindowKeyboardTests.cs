namespace PeerCast.Nova.App.Tests;

public sealed class GenesisYellowPageSettingsWindowKeyboardTests
{
    private const uint WindowMessageKeyDown = 0x0100;
    private const uint WindowMessageKeyUp = 0x0101;
    private const nuint VirtualKeyTab = 0x09;
    private const nuint VirtualKeyEnter = 0x0d;
    private const nuint VirtualKeyEscape = 0x1b;
    private const nuint VirtualKeySpace = 0x20;

    [Fact]
    public void TabMovesFocusForward()
    {
        var action = GenesisYellowPageSettingsWindow.ResolveKeyboardAction(
            WindowMessageKeyDown,
            VirtualKeyTab,
            actionButton: false,
            shiftPressed: false);

        Assert.Equal(
            GenesisYellowPageSettingsWindow.KeyboardAction.FocusNext,
            action);
    }

    [Fact]
    public void ShiftTabMovesFocusBackward()
    {
        var action = GenesisYellowPageSettingsWindow.ResolveKeyboardAction(
            WindowMessageKeyDown,
            VirtualKeyTab,
            actionButton: false,
            shiftPressed: true);

        Assert.Equal(
            GenesisYellowPageSettingsWindow.KeyboardAction.FocusPrevious,
            action);
    }

    [Fact]
    public void EscapeRequestsCloseFromAnyFocusableControl()
    {
        var action = GenesisYellowPageSettingsWindow.ResolveKeyboardAction(
            WindowMessageKeyDown,
            VirtualKeyEscape,
            actionButton: false,
            shiftPressed: false);

        Assert.Equal(
            GenesisYellowPageSettingsWindow.KeyboardAction.Close,
            action);
    }

    [Fact]
    public void EnterActivatesOnlyActionButtons()
    {
        var buttonAction = GenesisYellowPageSettingsWindow.ResolveKeyboardAction(
            WindowMessageKeyDown,
            VirtualKeyEnter,
            actionButton: true,
            shiftPressed: false);
        var editorAction = GenesisYellowPageSettingsWindow.ResolveKeyboardAction(
            WindowMessageKeyDown,
            VirtualKeyEnter,
            actionButton: false,
            shiftPressed: false);

        Assert.Equal(
            GenesisYellowPageSettingsWindow.KeyboardAction.ActivateButton,
            buttonAction);
        Assert.Equal(
            GenesisYellowPageSettingsWindow.KeyboardAction.None,
            editorAction);
    }

    [Fact]
    public void SpaceRemainsOwnedByNativeControls()
    {
        var action = GenesisYellowPageSettingsWindow.ResolveKeyboardAction(
            WindowMessageKeyDown,
            VirtualKeySpace,
            actionButton: true,
            shiftPressed: false);

        Assert.Equal(
            GenesisYellowPageSettingsWindow.KeyboardAction.None,
            action);
    }

    [Theory]
    [InlineData(0x09)]
    [InlineData(0x0d)]
    [InlineData(0x1b)]
    public void KeyUpIsNotIntercepted(int virtualKey)
    {
        var action = GenesisYellowPageSettingsWindow.ResolveKeyboardAction(
            WindowMessageKeyUp,
            (nuint)virtualKey,
            actionButton: true,
            shiftPressed: true);

        Assert.Equal(
            GenesisYellowPageSettingsWindow.KeyboardAction.None,
            action);
    }
}
