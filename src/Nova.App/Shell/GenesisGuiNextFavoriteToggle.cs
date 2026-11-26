namespace PeerCast.Nova.App;

internal enum GenesisGuiNextFavoriteToggleResult
{
    Unavailable,
    PersistenceFailed,
    Added,
    Removed,
}

/// <summary>
/// Framework-neutral favorite toggle used by native GUI entry points. A displayed
/// offline favorite can be removed, but adding always requires a row whose current
/// catalog state proves that the channel is live and interactive.
/// </summary>
internal static class GenesisGuiNextFavoriteToggle
{
    internal static event Action<
        GenesisGuiNextTabsViewModel,
        GenesisGuiNextFavoriteToggleResult>? ResultPublished;

    public static GenesisGuiNextFavoriteToggleResult Apply(
        GenesisGuiNextTabsViewModel tabs,
        GenesisGuiNextNativeTabRow? row)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        if (row is null || row.RecordingRule is not null || row.RecordingLibrary is not null || row.RecordingHistory is not null || row.Suppression is not null ||
            tabs.SelectedTab is GenesisGuiNextTab.Playing or
            GenesisGuiNextTab.Recording)
        {
            return Publish(tabs, GenesisGuiNextFavoriteToggleResult.Unavailable);
        }

        if (tabs.IsFavorite(row.DisplayChannel))
        {
            if (tabs.RemoveFavorite(row.DisplayChannel))
            {
                return Publish(tabs, GenesisGuiNextFavoriteToggleResult.Removed);
            }

            return Publish(
                tabs,
                tabs.IsFavorite(row.DisplayChannel)
                    ? GenesisGuiNextFavoriteToggleResult.PersistenceFailed
                    : GenesisGuiNextFavoriteToggleResult.Unavailable);
        }

        if (row.InteractiveChannel is not { } currentChannel)
        {
            return Publish(tabs, GenesisGuiNextFavoriteToggleResult.Unavailable);
        }

        var result = tabs.AddFavorite(currentChannel);
        if (result != GenesisFavoriteChannelMutationResult.Unchanged)
        {
            return Publish(tabs, GenesisGuiNextFavoriteToggleResult.Added);
        }

        return Publish(
            tabs,
            tabs.IsFavorite(currentChannel)
                ? GenesisGuiNextFavoriteToggleResult.Unavailable
                : GenesisGuiNextFavoriteToggleResult.PersistenceFailed);
    }

    private static GenesisGuiNextFavoriteToggleResult Publish(
        GenesisGuiNextTabsViewModel tabs,
        GenesisGuiNextFavoriteToggleResult result)
    {
        try
        {
            ResultPublished?.Invoke(tabs, result);
        }
        catch
        {
            // Feedback publication is auxiliary presentation. It must never alter
            // the already-completed favorite mutation result.
        }

        return result;
    }
}
