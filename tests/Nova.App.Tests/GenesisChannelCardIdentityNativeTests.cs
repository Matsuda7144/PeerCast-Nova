using System.Runtime.InteropServices;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelCardIdentityNativeTests
{
    [Theory]
    [InlineData(96u)]
    [InlineData(120u)]
    [InlineData(144u)]
    [InlineData(192u)]
    public void CurrentChannelIdentityFitsBothLinesWithTheActualUiFont(uint dpi)
    {
        if (!OperatingSystem.IsWindows()) return;
        var api = new SystemUiFontNativeApi();
        using var font = GenesisUiFontLease.TryCreate(api, dpi);
        Assert.NotNull(font);
        var dc = CreateCompatibleDC(0);
        Assert.NotEqual(0, dc);
        var previous = api.SelectFont(dc, font.Handle);
        try
        {
            Assert.NotEqual(0, previous);
            Assert.NotEqual((nint)(-1), previous);
            const string identity = "ChannelId: 38200000000000000000000000000001\r\n" +
                "Source: pcp://127.0.0.1:17149/channel/38200000000000000000000000000001";
            foreach (var size in new[] { (1088, 474), (1120, 620) })
            {
                Assert.True(GenesisMainWindowLayout.TryResolve(
                    GenesisMainWindowLayout.Scale(size.Item1, dpi),
                    GenesisMainWindowLayout.Scale(size.Item2, dpi), dpi,
                    out _, out _, out _, out var details));
                var bounds = new GenesisGuiNextChannelCardLayout(details.Width).Identity;
                var measured = new Rectangle { Right = GenesisMainWindowLayout.Scale(bounds.Width, dpi) };
                // Match the card's multiline GDI drawing and measure without clipping.
                Assert.True(DrawTextW(dc, identity, -1, ref measured, 0x0010 | 0x0400 | 0x0800 | 0x8000) > 0);
                Assert.True(measured.Bottom <= GenesisMainWindowLayout.Scale(bounds.Height, dpi),
                    $"Identity needs {measured.Bottom}px at {dpi} DPI; available height is {GenesisMainWindowLayout.Scale(bounds.Height, dpi)}px.");
            }
        }
        finally
        {
            if (previous != 0 && previous != -1) _ = api.SelectFont(dc, previous);
            _ = DeleteDC(dc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle { public int Left, Top, Right, Bottom; }

    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(nint dc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DrawTextW(nint dc, string text, int count, ref Rectangle rectangle, uint flags);
}
