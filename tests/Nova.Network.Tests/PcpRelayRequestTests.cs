using System.Text;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpRelayRequestTests
{
    [Fact]
    public void Encode_UsesLegacyRelayEntryHeaders()
    {
        var request = new PcpRelayRequest
        {
            ChannelId = Guid.ParseExact(
                "531dc8dfc7fb42928ac2c0a626517a87",
                "N"),
            Host = "localhost:7144",
            UserAgent = "PeerCastNova/Test",
            PcpVersion = 1,
            StreamPosition = 200000000,
        };

        var text = Encoding.ASCII.GetString(request.Encode());

        Assert.Equal(
            "GET /channel/531DC8DFC7FB42928AC2C0A626517A87 HTTP/1.0\r\n" +
            "Host:localhost:7144\r\n" +
            "User-Agent:PeerCastNova/Test\r\n" +
            "x-peercast-pcp:1\r\n" +
            "x-peercast-pos:200000000\r\n" +
            "\r\n",
            text);
    }

    [Fact]
    public void Encode_RejectsHeaderInjection()
    {
        var request = new PcpRelayRequest
        {
            ChannelId = Guid.NewGuid(),
            UserAgent = "Nova\r\nInjected: yes",
        };

        Assert.Throws<ArgumentException>(request.Encode);
    }
}
