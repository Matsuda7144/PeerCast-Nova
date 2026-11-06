namespace PeerCast.Nova.App;

// Logical coordinates for Modern only. The Classic calculators remain unchanged.
internal static class GenesisGuiNextTopBarLayout
{
    internal const int Height = 24;
    internal const int SearchLabelWidth = 24;
    internal const int FilterGap = 8;
    internal const int FullStatusWidth = 420;

    internal static bool TryResolve(int width, out int statusWidth, out int searchLeft,
        out int searchWidth, out int refreshLeft, out int favoriteLeft)
    {
        statusWidth = searchLeft = searchWidth = refreshLeft = favoriteLeft = 0;
        if (width < GenesisMainWindowLayout.MinimumContentWidth) return false;
        statusWidth = Math.Min(FullStatusWidth, 312 + (width - GenesisMainWindowLayout.MinimumContentWidth) / 3);
        searchLeft = 8 + statusWidth + 12;
        favoriteLeft = width - GenesisGuiNextAppShellBridge.FavoriteButtonWidth - 8;
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
        ReadOnlySpan<int> weights = width < FullStatusWidth ? [92, 52, 40, 40, 88] : [124, 52, 50, 50, 144];
        var total = width < FullStatusWidth ? 312 : FullStatusWidth;
        var left = 0;
        for (var i = 0; i < index; i++) left += weights[i];
        return (left * width / total, (left + weights[index]) * width / total - left * width / total);
    }
}
