using System.Net;
using System.Text;
using System.Xml.Linq;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpPlayerInformationTests
{
    [Fact]
    public void CountsDeduplicatePeerIdentityExpireAndRetireWithoutExposingAddresses()
    {
        using var clock = new AnnounceManualTimeProvider();
        using var directory = new PcpPeerDirectory(PcpPeerDirectoryTests.Channel, PcpPeerDirectoryTests.Owner, clock);
        var host = PcpPeerDirectoryTests.Host() with { DirectCount = 3, RelayCount = 2 };
        directory.ObserveReport(host, new());
        clock.Advance(TimeSpan.FromSeconds(1));
        directory.ObserveReport(host with { GlobalEndpoint = new(IPAddress.Parse("9.9.9.9"), 7144), DirectCount = 4 }, new());
        Assert.Equal(new PcpPlayerPeerCounts(1, 4, 2, 0), directory.ReadPlayerCounts());
        using (directory.TrackDownstream(9, new(Guid.NewGuid(), new(IPAddress.Parse("1.1.1.1"), 7144))))
            Assert.Equal(1, directory.ReadPlayerCounts().LocalRelays);
        clock.Advance(PcpPeerDirectory.Lifetime);
        Assert.Equal(new PcpPlayerPeerCounts(0, null, null, 0), directory.ReadPlayerCounts());
        directory.Dispose();
        Assert.Null(directory.ReadPlayerCounts().Listeners);
    }

    [Fact]
    public void ViewXmlPreservesUnicodeEscapesTextAndKeepsUnknownCountsAbsent()
    {
        var id = Guid.NewGuid();
        var metadata = new ChannelMetadataSnapshot("船長😀<&\"\u0001", "genre", "details", "https://example.test/?a=1&b=2",
            "FLV", 1500, comment: "comment", track: new("title", "track genre", "album", "artist", "https://example.test/track"));
        var bytes = PcpPlayerViewXml.Encode([(id, new(metadata) { Uptime = TimeSpan.FromSeconds(99), State = ChannelSessionState.Receiving }, 1)]);
        var root = XDocument.Parse(Encoding.UTF8.GetString(bytes)).Root!;
        var channel = Assert.Single(root.Element("channels_relayed")!.Elements("channel"));
        Assert.Equal(id.ToString("N").ToUpperInvariant(), (string?)channel.Attribute("id"));
        Assert.Equal("船長😀<&\"", (string?)channel.Attribute("name"));
        Assert.Equal(metadata.ContactUrl, (string?)channel.Attribute("url"));
        Assert.Equal("comment", (string?)channel.Attribute("comment"));
        Assert.Equal("1500", (string?)channel.Attribute("bitrate"));
        Assert.Equal("artist", (string?)channel.Element("track")!.Attribute("artist"));
        Assert.Equal("1", (string?)channel.Element("relay")!.Attribute("listeners"));
        Assert.Null(channel.Element("hits")!.Attribute("listeners"));
        Assert.Null(channel.Element("hits")!.Attribute("relays"));
    }

    [Fact]
    public void OversizedRemoteTextAndChannelCountRemainBounded()
    {
        var text = new string('&', 100_000);
        var info = new PcpPlayerChannelInfo(new(text, text, text, text, text, track: new(text, text, text, text, text)));
        var bytes = PcpPlayerViewXml.Encode(Enumerable.Range(0, 100).Select(_ => (Guid.NewGuid(), info, 0)));
        Assert.True(bytes.Length <= PcpPlayerViewXml.MaximumResponseBytes);
        Assert.Equal(8, XDocument.Parse(Encoding.UTF8.GetString(bytes)).Descendants("channel").Count());
    }
}

public sealed class PcpPlayerInformationHttpTests
{
    [Fact]
    public async Task MetadataAndPlaylistRemainReadableDuringSingleClientPlaybackAndNeverAllowCommands()
    {
        var id = new ChannelId(Guid.NewGuid());
        var session = new ChannelSessionId(Guid.NewGuid());
        var current = new PcpPlayerChannelInfo(new("first", contactUrl: "https://example.test/bbs", contentType: "FLV"));
        await using var host = new PcpHttpFlvPlaybackHost(playerChannelInfo: _ => current);
        host.Start();
        await using var source = host.Create(id, session);
        await source.ConsumeAsync(new(id, session, 0, 0x64616568, 0, 0,
            new byte[] { 0x46, 0x4C, 0x56, 1, 5, 0, 0, 0, 9, 0, 0, 0, 0 }, null), CancellationToken.None);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var streaming = await http.GetAsync(host.StreamUrlFor(id), HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, streaming.StatusCode);
        Assert.Equal(1, host.ActiveStreamClientCount);
        var baseUrl = $"http://127.0.0.1:{host.LocalEndPoint.Port}";
        Assert.Contains(host.StreamUrlFor(id), await http.GetStringAsync(host.PlaylistUrlFor(id)));
        var xml = XDocument.Parse(await http.GetStringAsync(baseUrl + "/admin?cmd=viewxml"));
        Assert.Equal("first", (string?)xml.Descendants("channel").Single().Attribute("name"));
        current = current with { Metadata = new("updated", bitrateKbps: 2000), Listeners = 7, Relays = 3 };
        xml = XDocument.Parse(await http.GetStringAsync(baseUrl + "/admin?cmd=viewxml"));
        Assert.Equal("updated", (string?)xml.Descendants("channel").Single().Attribute("name"));
        Assert.Equal("7", (string?)xml.Descendants("hits").Single().Attribute("listeners"));
        using var secondStream = await http.GetAsync(host.StreamUrlFor(id), HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, secondStream.StatusCode);
        foreach (var path in new[] { "/admin?cmd=stop", "/admin?cmd=bump", "/admin?cmd=viewxml&cmd=stop", "/pls/" + Guid.NewGuid().ToString("N") + ".pls" })
        {
            using var response = await http.GetAsync(baseUrl + path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        Assert.Equal(1, host.ActiveStreamClientCount);
        await source.DisposeAsync();
        xml = XDocument.Parse(await http.GetStringAsync(baseUrl + "/admin?cmd=viewxml"));
        Assert.Empty(xml.Descendants("channel"));
    }
}
