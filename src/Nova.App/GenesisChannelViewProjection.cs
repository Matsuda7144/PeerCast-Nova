namespace PeerCast.Nova.App;

internal sealed class GenesisChannelViewProjectionSnapshot
{
    public GenesisChannelViewProjectionSnapshot(
        IEnumerable<GenesisChannelItem> channels,
        IEnumerable<GenesisChannelItem> newChannels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(newChannels);

        Channels = Array.AsReadOnly(channels.ToArray());
        NewChannels = Array.AsReadOnly(newChannels.ToArray());
    }

    public IReadOnlyList<GenesisChannelItem> Channels { get; }

    public IReadOnlyList<GenesisChannelItem> NewChannels { get; }

    public static GenesisChannelViewProjectionSnapshot FromCatalog(
        GenesisChannelCatalogSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new(
            snapshot.Channels,
            Array.Empty<GenesisChannelItem>());
    }

    public static GenesisChannelViewProjectionSnapshot FromPresence(
        GenesisChannelPresenceSnapshot presence)
    {
        ArgumentNullException.ThrowIfNull(presence);
        return new(
            presence.Active.Select(static entry => entry.Channel),
            presence.New.Select(static entry => entry.Channel));
    }
}
