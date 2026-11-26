namespace PeerCast.Nova.App;

internal static class GenesisChannelCatalogSanitizer
{
    internal const int MaximumChannels = 10_000;
    internal const int MaximumNameCharacters = 512;
    internal const int MaximumSourceCharacters = 2_048;
    internal const int MaximumDescriptionCharacters = 4_096;
    internal const int MaximumTypeCharacters = 64;
    internal const int MaximumYellowPageCharacters = 4_096;
    internal const int ChannelIdCharacters = 32;

    public static GenesisChannelCatalogSnapshot Sanitize(
        GenesisChannelCatalogSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Channels);
        ArgumentNullException.ThrowIfNull(snapshot.Notices);

        var channels = new List<GenesisChannelItem>(
            Math.Min(snapshot.Channels.Count, MaximumChannels));
        var sources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var channel in snapshot.Channels)
        {
            if (channels.Count >= MaximumChannels)
            {
                break;
            }

            if (channel is null)
            {
                continue;
            }

            var name = NormalizeRequired(channel.Name, MaximumNameCharacters);
            var source = NormalizeSource(channel.Source);
            if (name is null || source is null || !sources.Add(source))
            {
                continue;
            }

            var description = NormalizeOptional(
                channel.Description,
                MaximumDescriptionCharacters);
            var listeners = NormalizeNonNegative(channel.Listeners);
            var relays = NormalizeNonNegative(channel.Relays);
            var bitrateKbps = NormalizeNonNegative(channel.BitrateKbps);
            var uptime = NormalizeNonNegative(channel.Uptime);
            var type = NormalizeOptional(channel.Type, MaximumTypeCharacters);
            var channelId = NormalizeChannelId(channel.ChannelId);
            var contactUrl = GenesisContactUrl.Normalize(channel.ContactUrl);

            channels.Add(IsUnchanged(
                    channel,
                    name,
                    source,
                    description,
                    listeners,
                    relays,
                    bitrateKbps,
                    uptime,
                    type,
                    channelId) && string.Equals(channel.ContactUrl, contactUrl, StringComparison.Ordinal)
                ? channel
                : new GenesisChannelItem(
                    name,
                    source,
                    description,
                    listeners,
                    relays,
                    bitrateKbps,
                    uptime,
                    type,
                    channelId)
                {
                    ContactUrl = contactUrl,
                    YellowPage = channel.YellowPage,
                });
        }

        var remainingEntries = MaximumChannels - channels.Count;
        var notices = new List<GenesisYellowPageNotice>(
            Math.Min(snapshot.Notices.Count, remainingEntries));
        foreach (var notice in snapshot.Notices)
        {
            if (notices.Count >= remainingEntries)
            {
                break;
            }

            if (notice is null)
            {
                continue;
            }

            var name = NormalizeRequired(notice.Name, MaximumNameCharacters);
            var description = NormalizeOptional(
                notice.Description,
                MaximumDescriptionCharacters);
            var type = NormalizeOptional(notice.Type, MaximumTypeCharacters);
            if (name is null ||
                !string.Equals(type, "RAW", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            notices.Add(
                string.Equals(notice.Name, name, StringComparison.Ordinal) &&
                string.Equals(notice.Description, description, StringComparison.Ordinal) &&
                string.Equals(notice.Type, type, StringComparison.Ordinal)
                    ? notice
                    : new GenesisYellowPageNotice(name, description, type));
        }

        return new(
            Array.AsReadOnly(channels.ToArray()),
            Array.AsReadOnly(notices.ToArray()));
    }

    internal static string? NormalizeYellowPage(string? value) =>
        NormalizeOptional(value, MaximumYellowPageCharacters);

    private static bool IsUnchanged(
        GenesisChannelItem channel,
        string name,
        string source,
        string? description,
        int? listeners,
        int? relays,
        int? bitrateKbps,
        TimeSpan? uptime,
        string? type,
        string? channelId) =>
        string.Equals(channel.Name, name, StringComparison.Ordinal) &&
        string.Equals(channel.Source, source, StringComparison.Ordinal) &&
        string.Equals(channel.Description, description, StringComparison.Ordinal) &&
        channel.Listeners == listeners &&
        channel.Relays == relays &&
        channel.BitrateKbps == bitrateKbps &&
        channel.Uptime == uptime &&
        string.Equals(channel.Type, type, StringComparison.Ordinal) &&
        string.Equals(channel.ChannelId, channelId, StringComparison.Ordinal);

    private static string? NormalizeRequired(string? value, int maximumCharacters)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return Bound(normalized, maximumCharacters);
    }

    private static string? NormalizeSource(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > MaximumSourceCharacters
                ? null
                : normalized;
    }

    private static string? NormalizeOptional(string? value, int maximumCharacters)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : Bound(normalized, maximumCharacters);
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

    private static string Bound(string value, int maximumCharacters)
    {
        if (value.Length <= maximumCharacters)
        {
            return value;
        }

        var boundedLength = maximumCharacters;
        if (boundedLength > 0 &&
            char.IsHighSurrogate(value[boundedLength - 1]) &&
            char.IsLowSurrogate(value[boundedLength]))
        {
            boundedLength--;
        }

        return value[..boundedLength];
    }

    private static int? NormalizeNonNegative(int? value) =>
        value is >= 0 ? value : null;

    private static TimeSpan? NormalizeNonNegative(TimeSpan? value) =>
        value is { } time && time >= TimeSpan.Zero ? time : null;
}
