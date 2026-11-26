namespace PeerCast.Nova.App;

/// <summary>
/// Framework-neutral text filtering for GUI Next channel views. Search terms are
/// matched case-insensitively across stable user-visible metadata while
/// preserving input ordering and object identities.
/// </summary>
internal static class GenesisChannelSearchFilter
{
    private readonly record struct ParsedTerm(string Text, bool Excluded);

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
                .Where(channel => MatchesAll(channel, terms))
                .ToArray());
    }

    public static IReadOnlyList<GenesisYellowPageNoticeView> ApplyNotices(
        IReadOnlyList<GenesisYellowPageNoticeView> notices,
        string? query)
    {
        ArgumentNullException.ThrowIfNull(notices);
        var terms = ParseTerms(query);
        if (terms.Length == 0)
        {
            return Array.AsReadOnly(notices.ToArray());
        }

        return Array.AsReadOnly(
            notices
                .Where(notice => MatchesAll(notice, terms))
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
                .Where(favorite => MatchesAll(favorite, terms))
                .ToArray());
    }

    private static ParsedTerm[] ParseTerms(string? query)
    {
        var rawTerms = (query ?? string.Empty).Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (rawTerms.Length == 0)
        {
            return [];
        }

        var parsed = new ParsedTerm[rawTerms.Length];
        for (var i = 0; i < rawTerms.Length; i++)
        {
            var token = rawTerms[i];
            if (token.Length > 1 && token[0] is '-' or '－')
            {
                parsed[i] = new(token[1..], Excluded: true);
            }
            else
            {
                parsed[i] = new(token, Excluded: false);
            }
        }

        return parsed;
    }

    private static bool MatchesAll(GenesisChannelItem channel, ParsedTerm[] terms)
    {
        foreach (var term in terms)
        {
            var matched = Matches(channel, term.Text);
            if (term.Excluded ? matched : !matched)
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesAll(GenesisYellowPageNoticeView notice, ParsedTerm[] terms)
    {
        foreach (var term in terms)
        {
            var matched = Matches(notice, term.Text);
            if (term.Excluded ? matched : !matched)
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesAll(
        GenesisFavoriteChannelProjectionEntry favorite,
        ParsedTerm[] terms)
    {
        foreach (var term in terms)
        {
            var matched = Matches(favorite, term.Text);
            if (term.Excluded ? matched : !matched)
            {
                return false;
            }
        }

        return true;
    }

    private static bool Matches(GenesisChannelItem channel, string term) =>
        Contains(channel.Name, term) ||
        Contains(channel.Description, term) ||
        Contains(channel.Type, term) ||
        Contains(channel.YellowPage, term) ||
        Contains(channel.Source, term) ||
        Contains(channel.ChannelId, term);

    private static bool Matches(GenesisYellowPageNoticeView notice, string term) =>
        Contains(notice.Notice.Name, term) ||
        Contains(notice.Notice.Description, term) ||
        Contains(notice.Notice.Type, term) ||
        Contains(notice.YellowPageName, term);

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
