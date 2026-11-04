using System.Runtime.InteropServices;

namespace PeerCast.Nova.App.Tests;

public sealed class PublishingSettingsScrollPaintTests
{
    [Fact]
    public async Task ScrollingInvalidatesChildPaintAndPreservesInput()
    {
        if (!OperatingSystem.IsWindows()) return;
        var settings = new PublishingSettingsController(new PublishingSettingsTests.MemoryStore());
        await settings.LoadAsync();
        var owner = CreateWindowExW(0, "STATIC", "Publishing scroll test owner", 0, 0, 0, 800, 800, 0, 0, 0, 0);
        Assert.NotEqual(0, owner);
        try
        {
            using var editor = GenesisGuiNextPublishingSettingsWindow.TryCreate(owner, settings, () => false);
            Assert.NotNull(editor);
            var window = FindWindowW("PeerCastNovaPublishingSettingsV1", "配信設定");
            Assert.NotEqual(0, window);
            Assert.Equal(owner, GetWindow(window, 4)); // GW_OWNER
            var heading = GetDlgItem(window, 1031);
            var input = GetDlgItem(window, 1000);
            Assert.True(SetWindowTextW(input, "scroll input 日本語"));
            foreach (var direction in new nuint[] { 1, 1, 0, 0 })
            {
                // Start without dirty regions, as after a completed paint. A scroll must
                // repaint STATIC children too, not just the clipped parent background.
                Assert.True(RedrawWindow(window, 0, 0, 0xA8)); // VALIDATE | NOERASE | ALLCHILDREN
                Assert.False(GetUpdateRect(heading, 0, false));
                _ = SendMessageW(window, 0x0115, direction, 0); // WM_VSCROLL
                Assert.True(GetUpdateRect(heading, 0, false));
                var text = new char[128];
                var length = GetWindowTextW(input, text, text.Length);
                Assert.Equal("scroll input 日本語", new string(text, 0, length));
            }
            Assert.Equal(0, settings.Saved.SettingsRevision);
        }
        finally
        {
            _ = DestroyWindow(owner);
            await settings.DisposeAsync();
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(uint extended, string className, string text, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowW(string className, string text);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] private static extern nint GetDlgItem(nint window, int id);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowTextW(nint window, string text);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(nint window, [Out] char[] text, int maximum);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool RedrawWindow(nint window, nint rectangle, nint region, uint flags);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetUpdateRect(nint window, nint rectangle, [MarshalAs(UnmanagedType.Bool)] bool erase);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessageW(nint window, uint message, nuint wParam, nint lParam);
}
