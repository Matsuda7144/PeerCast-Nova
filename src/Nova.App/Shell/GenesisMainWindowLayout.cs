namespace PeerCast.Nova.App;

/// <summary>96-DPI coordinates shared by main-window tracking and shell reflow.</summary>
internal static class GenesisMainWindowLayout
{
    internal const int ContentLeft = 12 + GenesisGuiNextAppShellPlacement.SidebarWidth + GenesisGuiNextAppShellPlacement.SidebarGap;
    internal const int ShellTop = 14;
    internal const int RightInset = 12;
    internal const int BottomInset = 44;
    internal const int MinimumContentWidth = 8 + GenesisGuiNextAppShellBridge.MinimumStatusTextWidth + 8 +
        GenesisChannelSearchBoxBridge.MinimumWidth + GenesisGuiNextAppShellBridge.RefreshButtonGap +
        GenesisGuiNextAppShellBridge.RefreshButtonWidth + GenesisGuiNextAppShellBridge.FavoriteButtonGap +
        GenesisGuiNextAppShellBridge.FavoriteButtonWidth;
    internal const int MinimumClientWidth = ContentLeft + MinimumContentWidth + RightInset;
    internal const int MinimumClientHeight = ShellTop + BottomInset +
        GenesisGuiNextAppShellBridge.MinimumSidebarHeight;

    internal static uint NormalizeDpi(uint dpi) => dpi is >= 48 and <= 768 ? dpi : 96;
    internal static int Scale(int value, uint dpi) => (int)Math.Ceiling(value * (double)NormalizeDpi(dpi) / 96);
    internal static int Logical(int value, uint dpi) => (int)(value * 96L / NormalizeDpi(dpi));

    internal static GenesisGuiNextAppShellPlacement.WindowRectangle ResolveDetailsPaneBounds(
        bool expandedToFullContent,
        GenesisGuiNextAppShellPlacement.WindowRectangle list,
        GenesisGuiNextAppShellPlacement.WindowRectangle details) =>
        expandedToFullContent
            ? new(list.Left, list.Top, list.Right, details.Bottom)
            : details;

    internal static bool TryResolve(int width, int height, uint dpi,
        out GenesisGuiNextAppShellPlacement.WindowRectangle sidebar,
        out GenesisGuiNextAppShellPlacement.WindowRectangle status,
        out GenesisGuiNextAppShellPlacement.WindowRectangle list,
        out GenesisGuiNextAppShellPlacement.WindowRectangle details,
        int? preferredDetailsHeight = null)
    {
        sidebar = status = list = details = default;
        if (width is <= 0 or > 32767 || height is <= 0 or > 32767) return false;
        var logicalWidth = Logical(width, dpi);
        var logicalHeight = Logical(height, dpi);
        return logicalWidth >= MinimumClientWidth && logicalHeight >= MinimumClientHeight &&
            GenesisGuiNextAppShellPlacement.TryResolveLayout(
                new(ContentLeft, 46, logicalWidth - RightInset, logicalHeight - BottomInset), ShellTop,
                out sidebar, out status, out list, out details, preferredDetailsHeight);
    }
}
