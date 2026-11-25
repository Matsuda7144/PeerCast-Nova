namespace PeerCast.Nova.App;

[Flags]
internal enum GenesisChannelNotificationReason
{
    None = 0,
    NewChannel = 1,
    FavoriteChannel = 2,
}

internal sealed record GenesisChannelNotificationPreferences(
    bool NotifyNewChannels,
    bool NotifyFavoriteChannels,
    bool PlaySound)
{
    public static GenesisChannelNotificationPreferences Default { get; } =
        new(
            NotifyNewChannels: true,
            NotifyFavoriteChannels: true,
            PlaySound: true);
}

internal sealed record GenesisChannelNotification(
    GenesisChannelItem Channel,
    GenesisChannelNotificationReason Reasons,
    bool PlaySound);

/// <summary>
/// Projects presence transitions into one application-owned notification per
/// activation. New and favorite reasons are combined so one activation cannot
/// produce duplicate native notifications or duplicate sounds.
/// </summary>
internal static class GenesisChannelNotificationProjection
{
    public static IReadOnlyList<GenesisChannelNotification> Create(
        GenesisChannelPresenceSnapshot presence,
        IReadOnlyList<GenesisFavoriteChannel> favorites,
        GenesisChannelNotificationPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(favorites);
        ArgumentNullException.ThrowIfNull(preferences);

        var favoriteKeys = new HashSet<string>(StringComparer.Ordinal);
        var favoriteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var favorite in favorites)
        {
            ArgumentNullException.ThrowIfNull(favorite);
            favoriteKeys.Add(favorite.StableKey);
            if (GenesisFavoriteChannel.NormalizeMatchName(favorite.Name) is { } normalizedName)
            {
                favoriteNames.Add(normalizedName);
            }
        }

        var notifications = new List<GenesisChannelNotification>();
        foreach (var transition in presence.Transitions)
        {
            if (transition.Kind != GenesisChannelPresenceTransitionKind.BecameActive)
            {
                continue;
            }

            var reasons = GenesisChannelNotificationReason.None;
            if (preferences.NotifyNewChannels && transition.Entry.IsNew)
            {
                reasons |= GenesisChannelNotificationReason.NewChannel;
            }

            var isFavoriteMatch =
                favoriteKeys.Contains(transition.Entry.StableKey) ||
                (GenesisFavoriteChannel.NormalizeMatchName(transition.Entry.Channel.Name) is { } channelName &&
                    favoriteNames.Contains(channelName));

            if (preferences.NotifyFavoriteChannels && isFavoriteMatch)
            {
                reasons |= GenesisChannelNotificationReason.FavoriteChannel;
            }

            if (reasons == GenesisChannelNotificationReason.None)
            {
                continue;
            }

            notifications.Add(new(
                transition.Entry.Channel,
                reasons,
                preferences.PlaySound));
        }

        return Array.AsReadOnly(notifications.ToArray());
    }
}
