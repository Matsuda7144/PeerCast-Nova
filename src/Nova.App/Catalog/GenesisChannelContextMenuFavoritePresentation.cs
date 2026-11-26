namespace PeerCast.Nova.App;

internal static class GenesisChannelContextMenuFavoritePresentation
{
    internal const string AddFavoriteText = "Add Favorite";
    internal const string RemoveFavoriteText = "Remove Favorite";

    public static string? Resolve(
        GenesisGuiNextTabsViewModel? tabs,
        GenesisGuiNextNativeTabRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (tabs is null || tabs.SelectedTab is GenesisGuiNextTab.Playing or
            GenesisGuiNextTab.Recording)
        {
            return null;
        }

        if (tabs.IsFavorite(row.DisplayChannel))
        {
            return RemoveFavoriteText;
        }

        return row.InteractiveChannel is null
            ? null
            : AddFavoriteText;
    }
}
