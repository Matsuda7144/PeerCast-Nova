namespace PeerCast.Nova.App;

/// <summary>
/// Framework-neutral text filtering for GUI Next channel views. Search terms are
/// matched case-insensitively across stable user-visible channel metadata while
/// preserving the input ordering and channel object identities.
/// </summary>
internal static class GenesisChannelSearchFilter
{
    public static IReadOnlyList<GenesisChannelItem> Apply(
        IReadOnlyList<GenesisChannelItem> channels,
        string? query)
    {
        ArgumentNullException.ThrowIfNull(channels);
        var terms = ParseTerms(query);
        if (terms.Length == 0)
        {
            return Array.AsReadOnly(channels.ToArray());
        }

        return Array.AsReadOnly(
            channels
                .Where(channel => terms.All(term => Matches(channel, term)))
                .ToArray());
    }

    public static IReadOnlyList<GenesisFavoriteChannelProjectionEntry> ApplyFavorites(
        IReadOnlyList<GenesisFavoriteChannelProjectionEntry> favorites,
        string? query)
    {
        ArgumentNullException.ThrowIfNull(favorites);
        var terms = ParseTerms(query);
        if (terms.Length == 0)
        {
            return Array.AsReadOnly(favorites.ToArray());
        }

        return Array.AsReadOnly(
            favorites
                .Where(favorite => terms.All(term => Matches(favorite, term)))
                .ToArray());
    }

    private static string[] ParseTerms(string? query) =>
        (query ?? string.Empty).Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool Matches(GenesisChannelItem channel, string term) =>
        Contains(channel.Name, term) ||
        Contains(channel.Description, term) ||
        Contains(channel.Type, term) ||
        Contains(channel.YellowPage, term) ||
        Contains(channel.Source, term) ||
        Contains(channel.ChannelId, term);

    private static bool Matches(
        GenesisFavoriteChannelProjectionEntry favorite,
        string term) =>
        favorite.CurrentChannel is { } currentChannel && Matches(currentChannel, term) ||
        Contains(favorite.Favorite.Name, term) ||
        Contains(favorite.Favorite.Description, term) ||
        Contains(favorite.Favorite.Type, term) ||
        Contains(favorite.Favorite.LastKnownYellowPage, term) ||
        Contains(favorite.Favorite.LastKnownSource, term) ||
        Contains(favorite.Favorite.ChannelId, term);

    private static bool Contains(string? value, string term) =>
        value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
}
