using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.App;

internal static class GenesisPlayerChannelInfo
{
    internal static PcpPlayerChannelInfo Create(GenesisChannelItem catalog, ChannelStatusSnapshot? status,
        PcpPlayerPeerCounts? peers, TimeSpan uptime, TimeSpan catalogAge)
    {
        var m = status?.Metadata ?? ChannelMetadataSnapshot.Empty;
        var track = m.Track;
        var metadata = new ChannelMetadataSnapshot(m.Name ?? catalog.Name, m.Genre,
            m.Description ?? catalog.Description,
            GenesisContactUrl.Normalize(m.ContactUrl ?? catalog.ContactUrl), m.ContentType ?? catalog.Type,
            m.BitrateKbps ?? (catalog.BitrateKbps is >= 0 ? catalog.BitrateKbps : null), m.StreamType, m.Comment,
            m.StreamExtension, track is null ? null : track with { ContactUrl = GenesisContactUrl.Normalize(track.ContactUrl) });
        var freshCatalog = catalogAge <= TimeSpan.FromMinutes(2);
        return new(metadata)
        {
            Uptime = uptime,
            State = status?.State ?? ChannelSessionState.Connecting,
            Hosts = peers?.Hosts ?? 0,
            Listeners = peers?.Listeners ?? (freshCatalog && catalog.Listeners is >= 0 ? catalog.Listeners : null),
            Relays = peers?.Relays ?? (freshCatalog && catalog.Relays is >= 0 ? catalog.Relays : null),
            LocalRelays = peers?.LocalRelays ?? 0,
        };
    }
}
