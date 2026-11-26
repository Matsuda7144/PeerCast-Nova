namespace PeerCast.Nova.App;

internal static class GenesisChannelListRenderRecovery
{
    public static IReadOnlyList<GenesisChannelItem> Resolve(
        bool rowsReplaced,
        IReadOnlyList<GenesisChannelItem> channels,
        Action clearManagedSelection)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(clearManagedSelection);
        if (rowsReplaced)
        {
            return channels;
        }

        clearManagedSelection();
        return Array.Empty<GenesisChannelItem>();
    }
}
