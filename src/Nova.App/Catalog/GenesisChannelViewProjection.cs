namespace PeerCast.Nova.App;

internal sealed record GenesisYellowPageNoticeView(
    GenesisYellowPageNotice Notice,
    string? YellowPageId,
    string? YellowPageName,
    int SourceIndex)
{
    public string StableKey => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"notice:{YellowPageId ?? string.Empty}:{SourceIndex}:{Notice.Name}");
}

internal sealed class GenesisChannelViewProjectionSnapshot
{
    public GenesisChannelViewProjectionSnapshot(
        IEnumerable<GenesisChannelItem> channels,
        IEnumerable<GenesisChannelItem> newChannels)
        : this(
            channels,
            newChannels,
            Array.Empty<GenesisYellowPageNoticeView>())
    {
    }

    public GenesisChannelViewProjectionSnapshot(
        IEnumerable<GenesisChannelItem> channels,
        IEnumerable<GenesisChannelItem> newChannels,
        IEnumerable<GenesisYellowPageNoticeView> notices)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(newChannels);
        ArgumentNullException.ThrowIfNull(notices);

        Channels = Array.AsReadOnly(channels.ToArray());
        NewChannels = Array.AsReadOnly(newChannels.ToArray());
        Notices = Array.AsReadOnly(notices.ToArray());
    }

    public IReadOnlyList<GenesisChannelItem> Channels { get; }

    public IReadOnlyList<GenesisChannelItem> NewChannels { get; }

    public IReadOnlyList<GenesisYellowPageNoticeView> Notices { get; }

    public static GenesisChannelViewProjectionSnapshot FromCatalog(
        GenesisChannelCatalogSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new(
            snapshot.Channels,
            Array.Empty<GenesisChannelItem>(),
            snapshot.Notices.Select(static (notice, index) =>
                new GenesisYellowPageNoticeView(
                    notice,
                    YellowPageId: null,
                    YellowPageName: null,
                    SourceIndex: index)));
    }

    public static GenesisChannelViewProjectionSnapshot FromPresence(
        GenesisChannelPresenceSnapshot presence) =>
        FromPresence(
            presence,
            Array.Empty<GenesisAggregatedYellowPageNotice>());

    public static GenesisChannelViewProjectionSnapshot FromPresence(
        GenesisChannelPresenceSnapshot presence,
        IEnumerable<GenesisAggregatedYellowPageNotice> notices)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(notices);
        return new(
            presence.Active.Select(static entry => entry.Channel),
            presence.New.Select(static entry => entry.Channel),
            notices.Select(static entry =>
                new GenesisYellowPageNoticeView(
                    entry.Notice,
                    entry.YellowPageId,
                    entry.YellowPageName,
                    entry.SourceIndex)));
    }
}
