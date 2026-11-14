using System.Text;
using System.Xml.Linq;
using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>Read-only player metadata. Contains no peer endpoints or control capabilities.</summary>
public sealed record PcpPlayerChannelInfo(ChannelMetadataSnapshot Metadata)
{
    public TimeSpan Uptime { get; init; }
    public long? Listeners { get; init; }
    public long? Relays { get; init; }
    public int Hosts { get; init; }
    public int LocalRelays { get; init; }
    public ChannelSessionState State { get; init; }
}

/// <summary>Counts from distinct, unexpired HOST identities; not a complete global census.</summary>
public sealed record PcpPlayerPeerCounts(int Hosts, long? Listeners, long? Relays, int LocalRelays);

internal static class PcpPlayerViewXml
{
    internal const int MaximumResponseBytes = 512 * 1024;

    internal static byte[] Encode(IEnumerable<(Guid Id, PcpPlayerChannelInfo Info, int LocalDirects)> channels)
    {
        var list = new XElement("channels_relayed");
        foreach (var (id, info, localDirects) in channels.Take(8))
        {
            var m = info.Metadata;
            var uptime = (long)Math.Clamp(info.Uptime.TotalSeconds, 0, int.MaxValue);
            var hits = new XElement("hits", new XAttribute("hosts", Math.Max(0, info.Hosts)));
            if (info.Listeners is >= 0) hits.Add(new XAttribute("listeners", info.Listeners.Value));
            if (info.Relays is >= 0) hits.Add(new XAttribute("relays", info.Relays.Value));
            var channel = new XElement("channel", new XAttribute("id", id.ToString("N").ToUpperInvariant()),
                Text("name", m.Name), Text("genre", m.Genre), Text("desc", m.Description), Text("comment", m.Comment),
                Text("url", m.ContactUrl, 2048), Text("type", m.ContentType),
                new XAttribute("uptime", uptime), new XAttribute("age", uptime), hits,
                new XElement("relay", new XAttribute("listeners", Math.Max(0, localDirects)),
                    new XAttribute("relays", Math.Max(0, info.LocalRelays)), new XAttribute("hosts", Math.Max(0, info.Hosts)),
                    new XAttribute("status", info.State switch
                    {
                        ChannelSessionState.Receiving => "RECEIVE",
                        ChannelSessionState.Connecting or ChannelSessionState.Negotiating => "CONNECT",
                        ChannelSessionState.Reconnecting => "SEARCH",
                        ChannelSessionState.Faulted => "ERROR",
                        _ => "IDLE",
                    })),
                new XElement("track", Text("title", m.Track?.Title), Text("album", m.Track?.Album),
                    Text("genre", m.Track?.Genre), Text("artist", m.Track?.Creator), Text("contact", m.Track?.ContactUrl, 2048)));
            if (m.BitrateKbps is >= 0) channel.Add(new XAttribute("bitrate", m.BitrateKbps.Value));
            list.Add(channel);
        }
        list.Add(new XAttribute("total", list.Elements().Count()));
        var document = new XDocument(new XElement("peercast", list,
            new XElement("channels_found", new XAttribute("total", 0))));
        var bytes = Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting));
        if (bytes.Length > MaximumResponseBytes) throw new InvalidDataException("Player metadata exceeds its response bound.");
        return bytes;
    }

    private static XAttribute Text(string name, string? value, int maximum = 512)
    {
        var text = new StringBuilder();
        foreach (var rune in (value ?? string.Empty).EnumerateRunes())
        {
            if (text.Length + rune.Utf16SequenceLength > maximum) break;
            if (rune.Value is 9 or 10 or 13 or >= 0x20 and <= 0xD7FF or >= 0xE000 and <= 0xFFFD or >= 0x10000 and <= 0x10FFFF)
                text.Append(rune.ToString());
        }
        return new(name, text.ToString());
    }
}

internal sealed partial class PcpPeerDirectory
{
    internal PcpPlayerPeerCounts ReadPlayerCounts()
    {
        lock (_gate)
        {
            if (_retired) return new(0, null, null, 0);
            var now = _clock.GetTimestamp();
            var hosts = _reports.Where(r => _clock.GetElapsedTime(r.At, now) < Lifetime)
                .GroupBy(r => r.Host.PeerSessionId).Select(g => g.MaxBy(r => r.At)!.Host).ToArray();
            return new(hosts.Length,
                hosts.Length > 0 && hosts.All(h => h.DirectCount.HasValue) ? hosts.Sum(h => (long)h.DirectCount!.Value) : null,
                hosts.Length > 0 && hosts.All(h => h.RelayCount.HasValue) ? hosts.Sum(h => (long)h.RelayCount!.Value) : null,
                _downstreams.Count);
        }
    }
}

public sealed partial class PcpRelayCoordinator
{
    public PcpPlayerPeerCounts ReadPlayerPeerCounts() => _downstreamServer.Peers.ReadPlayerCounts();
}
