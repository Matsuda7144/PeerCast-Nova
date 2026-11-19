using System.Runtime.InteropServices;
using PeerCast.Nova.Foundation.Configuration;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisReadOnlyDetailsPaintTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModernReadOnlyEditUsesOpaqueBackgroundForNativeRepaint(bool publishing)
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Directory.CreateTempSubdirectory("Nova-readonly-details-");
        try
        {
            var store = new NovaSettingsStore(Path.Combine(root.FullName, "settings.json"));
            await store.UpdateAsync(settings => settings with { GuiMode = NovaGuiMode.Modern });
            var model = new GenesisMainViewModel(new FakePlaybackSessionController())
            {
                AppearanceSettings = new GenesisAppearanceSettings(store),
            };
            var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection())
            {
                SelectedTab = publishing ? GenesisGuiNextTab.Publishing : GenesisGuiNextTab.Recording,
            };
            var commonControls = new CommonControls { Size = 8, Classes = 1 };
            Assert.True(InitCommonControlsEx(ref commonControls));
            var owner = CreateWindowExW(0, "STATIC", string.Empty, 0, 0, 0, 1120, 620, 0, 0, 0, 0);
            Assert.NotEqual(0, owner);
            try
            {
                var list = CreateWindowExW(0, "SysListView32", string.Empty, 0x40000001,
                    184, 44, 924, 298, owner, 0, 0, 0);
                Assert.NotEqual(0, list);
                await using var shell = GenesisGuiNextAppShellBridge.TryCreate(owner, owner, list,
                    new(184, 44, 1108, 342), new(12, 44, 172, 608),
                    new(184, 44, 1108, 74), new(184, 352, 1108, 568),
                    tabs, model, new GenesisMainWindowInteraction(model), []);
                Assert.NotNull(shell);

                var pane = FindDetailsPane(owner);
                Assert.NotEqual(0, pane);
                var history = GetDlgItem(pane, GenesisGuiNextAppShellBridge.RecordingHistoryDetailsControlId);
                if (!publishing)
                {
                    AssertOpaquePaint(pane, history);
                }
                else
                {
                    var publishingEditors = new List<nint>();
                    for (var edit = FindWindowExW(pane, 0, "EDIT", null); edit != 0;
                         edit = FindWindowExW(pane, edit, "EDIT", null))
                    {
                        if (edit != history) publishingEditors.Add(edit);
                    }
                    Assert.NotEmpty(publishingEditors);
                    Assert.All(publishingEditors, edit => AssertOpaquePaint(pane, edit));
                }
            }
            finally { _ = DestroyWindow(owner); }
        }
        finally { root.Delete(recursive: true); }
    }

    private static nint FindDetailsPane(nint owner)
    {
        for (var child = FindWindowExW(owner, 0, null, null); child != 0;
             child = FindWindowExW(owner, child, null, null))
        {
            if (GetDlgItem(child, GenesisGuiNextAppShellBridge.RecordingHistoryDetailsControlId) != 0)
                return child;
        }
        return 0;
    }

    private static void AssertOpaquePaint(nint pane, nint edit)
    {
        Assert.NotEqual(0, edit);
        var dc = CreateCompatibleDC(0);
        Assert.NotEqual(0, dc);
        try
        {
            // Win32 sends this notification for read-only EDIT controls.
            var brush = SendMessageW(pane, 0x0138, (nuint)dc, edit);
            Assert.NotEqual(0, brush);
            Assert.Equal(2, GetBkMode(dc));
            Assert.Equal((uint)GenesisGuiNextDesign.Input, GetBkColor(dc));
        }
        finally { _ = DeleteDC(dc); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CommonControls { public uint Size, Classes; }

    [DllImport("comctl32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool InitCommonControlsEx(ref CommonControls controls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(uint extended, string className, string text, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowExW(nint parent, nint after, string? className, string? text);
    [DllImport("user32.dll")] private static extern nint GetDlgItem(nint window, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessageW(nint window, uint message, nuint value, nint data);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(nint window);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern int GetBkMode(nint dc);
    [DllImport("gdi32.dll")] private static extern uint GetBkColor(nint dc);
    [DllImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(nint dc);
}
