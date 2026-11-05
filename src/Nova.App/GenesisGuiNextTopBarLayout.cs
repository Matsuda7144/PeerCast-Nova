namespace PeerCast.Nova.App;

// Logical coordinates for Modern only. The Classic calculators remain unchanged.
internal static class GenesisGuiNextTopBarLayout
{
    internal const int Height = 24;
    internal const int SearchLabelWidth = 28;
    internal const int FilterGap = 12;
    internal const int FullStatusWidth = 420;

    internal static bool TryResolve(int width, out int statusWidth, out int searchLeft,
        out int searchWidth, out int refreshLeft, out int favoriteLeft)
    {
        statusWidth = searchLeft = searchWidth = refreshLeft = favoriteLeft = 0;
        if (width < GenesisMainWindowLayout.MinimumContentWidth) return false;
        statusWidth = Math.Min(FullStatusWidth, 300 + (width - GenesisMainWindowLayout.MinimumContentWidth) / 3);
        searchLeft = 8 + statusWidth + 12;
        favoriteLeft = width - GenesisGuiNextAppShellBridge.FavoriteButtonWidth - 4;
        refreshLeft = favoriteLeft - GenesisGuiNextAppShellBridge.FavoriteButtonGap - GenesisGuiNextAppShellBridge.RefreshButtonWidth;
        searchWidth = refreshLeft - GenesisGuiNextAppShellBridge.RefreshButtonGap - searchLeft;
        return TryResolveSearch(searchWidth, out _, out _);
    }

    internal static bool TryResolveSearch(int width, out int editWidth, out int filterLeft)
    {
        filterLeft = width - GenesisChannelSearchBoxBridge.YellowPageFilterWidth;
        editWidth = filterLeft - FilterGap - SearchLabelWidth;
        if (editWidth >= GenesisChannelSearchBoxBridge.MinimumSearchEditWidth) return true;
        editWidth = filterLeft = 0;
        return false;
    }

    internal static (int Left, int Width) StatusCell(int width, int index)
    {
        ReadOnlySpan<int> weights = width < FullStatusWidth ? [64, 60, 48, 48, 80] : [64, 112, 64, 64, 116];
        var total = width < FullStatusWidth ? 300 : FullStatusWidth;
        var left = 0;
        for (var i = 0; i < index; i++) left += weights[i];
        return (left * width / total, (left + weights[index]) * width / total - left * width / total);
    }
}
