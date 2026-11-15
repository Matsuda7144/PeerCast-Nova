using System.Runtime.InteropServices;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisConnectionBodyNativeTests
{
    [Theory]
    [InlineData(false, 1)] // HTCLIENT: card background must receive the wheel.
    [InlineData(true, 7)] // HTVSCROLL: the native scrollbar must receive pointer input.
    public void ScrollableCardUsesNativeHitTesting(bool scrollbar, int expected)
    {
        if (!OperatingSystem.IsWindows()) return;
        var owner = CreateWindowExW(0, "STATIC", "Connection body hit-test owner", 0,
            0, 0, 420, 300, 0, 0, 0, 0);
        Assert.NotEqual(0, owner);
        try
        {
            // Use the production control and subclass; no visible window or injected mouse input.
            var body = GenesisGuiNextAppShellBridge.CreateConnectionBodyWindow(owner, 0, 0);
            Assert.NotEqual(0, body);
            Assert.True(MoveWindow(body, 0, 0, 320, 180, false));
            Assert.True(ShowScrollBar(body, 1, true));
            Assert.True(GetWindowRect(body, out var rectangle));
            var x = scrollbar ? rectangle.Right - 2 : rectangle.Left + 8;
            var y = rectangle.Top + 90;
            var point = unchecked((nint)((uint)(ushort)x | ((uint)(ushort)y << 16)));
            Assert.Equal((nint)expected, SendMessageW(body, 0x0084, 0, point));
        }
        finally { _ = DestroyWindow(owner); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(uint extended, string className, string text, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool MoveWindow(nint window, int x, int y, int width, int height, [MarshalAs(UnmanagedType.Bool)] bool repaint);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowScrollBar(nint window, int bar, [MarshalAs(UnmanagedType.Bool)] bool show);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out Rectangle rectangle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessageW(nint window, uint message, nuint wParam, nint lParam);
}
