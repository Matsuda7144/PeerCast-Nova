namespace PeerCast.Nova.App;

/// <summary>
/// Resolves channel identity independently from a particular Yellow Page tracker.
/// Valid PeerCast channel ids are preferred; source is the compatibility fallback
/// for catalogs that do not expose a channel id.
/// </summary>
internal static class GenesisChannelIdentity
{
    private const int ChannelIdCharacters = 32;

    public static string GetStableKey(GenesisChannelItem channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var channelId = NormalizeChannelId(channel.ChannelId);
        return channelId is not null
            ? $"id:{channelId}"
            : $"source:{channel.Source.Trim()}";
    }

    public static bool Matches(GenesisChannelItem left, GenesisChannelItem right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var leftChannelId = NormalizeChannelId(left.ChannelId);
        var rightChannelId = NormalizeChannelId(right.ChannelId);
        if (leftChannelId is not null && rightChannelId is not null)
        {
            return string.Equals(leftChannelId, rightChannelId, StringComparison.Ordinal);
        }

        return string.Equals(
            left.Source.Trim(),
            right.Source.Trim(),
            StringComparison.Ordinal);
    }

    private static string? NormalizeChannelId(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length != ChannelIdCharacters ||
            normalized.All(character => character == '0'))
        {
            return null;
        }

        foreach (var character in normalized)
        {
            if (!Uri.IsHexDigit(character))
            {
                return null;
            }
        }

        return normalized.ToUpperInvariant();
    }
}
