using System.Net;
using System.Text;
using System.Xml;
using PeerCast.Nova.Network.PortMapping;

namespace PeerCast.Nova.Network.Tests;

public sealed class UpnpIgdProtocolTests
{
    private static readonly NatNetworkIdentity Home = NatPortMappingTestHarness.Home;

    [Fact]
    public void Ssdp_AdmitsOnlySelectedGatewayAndUniqueValidHeaders()
    {
        var packet = "HTTP/1.1 200 OK\r\nST: upnp:rootdevice\r\nUSN: uuid:router::upnp:rootdevice\r\nLOCATION: http://192.168.1.1:1234/root.xml\r\n\r\n";
        Assert.Equal(new Uri("http://192.168.1.1:1234/root.xml"), Parse(packet));
        Assert.Null(UpnpIgdProtocol.ParseSsdp(Encoding.ASCII.GetBytes(packet), IPAddress.Parse("192.168.1.3"), Home));
        Assert.Null(Parse(packet.Replace("200 OK", "500 Error", StringComparison.Ordinal)));
        Assert.Null(Parse(packet.Replace("ST: upnp:rootdevice", "ST: ssdp:all", StringComparison.Ordinal)));
        Assert.Null(Parse(packet.Replace("USN: uuid:router::upnp:rootdevice\r\n", "", StringComparison.Ordinal)));
        Assert.Null(Parse(packet.Replace("LOCATION:", "LOCATION: http://192.168.1.1/other\r\nLOCATION:", StringComparison.Ordinal)));
        Assert.Null(Parse(new string('a', 8193)));
    }

    [Theory]
    [InlineData("http://example.com/root.xml")]
    [InlineData("http://192.168.1.3/root.xml")]
    [InlineData("http://127.0.0.1/root.xml")]
    [InlineData("http://169.254.169.254/root.xml")]
    [InlineData("https://192.168.1.1/root.xml")]
    [InlineData("http://user@192.168.1.1/root.xml")]
    [InlineData("http://192.168.1.1/root.xml#fragment")]
    [InlineData("file:///etc/passwd")]
    public void GatewayUrls_RejectExternalHostsCredentialsFragmentsAndOtherSchemes(string url) =>
        Assert.False(UpnpIgdProtocol.IsGatewayUrl(new Uri(url), Home));

    [Theory]
    [InlineData("WANIPConnection", 1)]
    [InlineData("WANIPConnection", 2)]
    [InlineData("WANPPPConnection", 1)]
    public void Description_DetectsSupportedWanService(string serviceName, int version)
    {
        var type = $"urn:schemas-upnp-org:service:{serviceName}:{version}";
        var service = UpnpIgdProtocol.ParseDescription(Description(type), new Uri("http://192.168.1.1/root.xml"), Home);
        Assert.NotNull(service);
        Assert.Equal(type, service.ServiceType);
        Assert.Equal(new Uri("http://192.168.1.1/control"), service.ControlUrl);
    }

    [Fact]
    public void Description_RejectsExternalControlAndBaseUrlsAndNonIgdDevices()
    {
        var type = UpnpIgdProtocol.ServiceTypes[0];
        var location = new Uri("http://192.168.1.1/root.xml");
        Assert.Null(UpnpIgdProtocol.ParseDescription(Description(type, "http://example.com/control"), location, Home));
        Assert.Null(UpnpIgdProtocol.ParseDescription(Description(type, "/control", "http://example.com/"), location, Home));
        Assert.Null(UpnpIgdProtocol.ParseDescription(Description(type, "/control", device: "MediaServer:1"), location, Home));
        Assert.Null(UpnpIgdProtocol.ParseDescription(Description("urn:schemas-upnp-org:service:WANIPConnection:99"), location, Home));
    }

    [Fact]
    public void Xml_RejectsDtdAndOversizedResponses()
    {
        Assert.Throws<XmlException>(() => UpnpIgdProtocol.ParseXml(Encoding.UTF8.GetBytes("<!DOCTYPE root [<!ENTITY x SYSTEM 'file:///secret'>]><root>&x;</root>")));
        Assert.Throws<XmlException>(() => UpnpIgdProtocol.ParseXml(new byte[UpnpIgdProtocol.MaximumXmlBytes + 1]));
    }

    [Fact]
    public void Soap_RequiresMatchingEnvelopeActionAndParsesErrorCodes()
    {
        var type = UpnpIgdProtocol.ServiceTypes[0];
        var success = Encoding.UTF8.GetBytes($"<s:Envelope xmlns:s='http://schemas.xmlsoap.org/soap/envelope/'><s:Body><u:AddPortMappingResponse xmlns:u='{type}'/></s:Body></s:Envelope>");
        Assert.Null(UpnpIgdTransport.ParseReply(success, type, "AddPortMapping").ErrorCode);
        Assert.Throws<XmlException>(() => UpnpIgdTransport.ParseReply(success, type, "DeletePortMapping"));
        var fault = Encoding.UTF8.GetBytes("<s:Envelope xmlns:s='http://schemas.xmlsoap.org/soap/envelope/'><s:Body><s:Fault><detail><UPnPError xmlns='urn:schemas-upnp-org:control-1-0'><errorCode>718</errorCode></UPnPError></detail></s:Fault></s:Body></s:Envelope>");
        Assert.Equal(718, UpnpIgdTransport.ParseReply(fault, type, "AddPortMapping").ErrorCode);
    }

    [Fact]
    public async Task Discovery_MulticastServiceSuppressesUnicast()
    {
        var clock = new ManualNatTimeProvider();
        var multicast = new SearchClient(clock, Reply());
        var created = 0;
        var service = await UpnpIgdProtocol.DiscoverAsync(Home, () =>
        {
            Assert.Equal(1, ++created);
            return multicast;
        }, (_, _) => Task.FromResult(Description(UpnpIgdProtocol.ServiceTypes[1])), clock, default);
        Assert.Equal(UpnpIgdProtocol.ServiceTypes[1], service?.ServiceType);
        Assert.Equal("239.255.255.250:1900", multicast.SentTo?.ToString());
        Assert.True(multicast.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Discovery_UnicastFindsServiceAfterMulticastSilenceOrUnsupportedDescription(bool unsupported)
    {
        var clock = new ManualNatTimeProvider();
        var multicast = new SearchClient(clock, unsupported ? [Reply()] : []);
        var unicast = new SearchClient(clock, Reply());
        var clients = new Queue<SearchClient>([multicast, unicast]);
        var reads = 0;
        var service = await UpnpIgdProtocol.DiscoverAsync(Home, () => clients.Dequeue(), (_, _) =>
        {
            reads++;
            return Task.FromResult(unsupported && reads == 1
                ? Description("urn:schemas-upnp-org:service:Unsupported:1")
                : Description(UpnpIgdProtocol.ServiceTypes[1]));
        }, clock, default);
        Assert.Equal(new Uri("http://192.168.1.1/control"), service?.ControlUrl);
        Assert.Empty(clients);
        Assert.Equal("192.168.1.1:1900", unicast.SentTo?.ToString());
        Assert.Contains("HOST: 192.168.1.1:1900\r\n", unicast.Request);
        Assert.Contains("ST: upnp:rootdevice\r\n", unicast.Request);
        Assert.True(multicast.Disposed && unicast.Disposed);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("other-sender")]
    [InlineData("external-location")]
    [InlineData("oversized")]
    public async Task Discovery_UnicastSilenceOrInvalidResponsesCannotCauseHttpAccess(string scenario)
    {
        var clock = new ManualNatTimeProvider();
        var reply = scenario switch
        {
            "other-sender" => Reply(sender: "192.168.1.3"),
            "external-location" => Reply(location: "http://example.com/root.xml"),
            "oversized" => (new byte[8193], IPAddress.Parse(Home.Device.GatewayAddress)),
            _ => Reply(),
        };
        var multicast = new SearchClient(clock);
        var unicast = new SearchClient(clock, scenario == "none" ? [] : [reply]);
        var clients = new Queue<SearchClient>([multicast, unicast]);
        var service = await UpnpIgdProtocol.DiscoverAsync(Home, () => clients.Dequeue(),
            (_, _) => throw new InvalidOperationException("Rejected datagrams must not authorize HTTP."),
            clock, default);
        Assert.Null(service);
        Assert.Empty(clients); // Exactly two sends, including exactly one directed search.
        Assert.True(multicast.Disposed && unicast.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Discovery_CancellationStopsWithoutSendingAnotherSearch(bool duringUnicast)
    {
        var clock = new ManualNatTimeProvider();
        using var cancellation = new CancellationTokenSource();
        var created = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UpnpIgdProtocol.DiscoverAsync(Home, () =>
        {
            created++;
            return new SearchClient(clock) { CancelOnReceive = created == (duringUnicast ? 2 : 1) ? cancellation : null };
        }, (_, _) => throw new InvalidOperationException(), clock, cancellation.Token));
        Assert.Equal(duringUnicast ? 2 : 1, created);
    }

    [Fact]
    public async Task Discovery_LocalDescriptionTimeoutAllowsOneFallback()
    {
        var clock = new ManualNatTimeProvider();
        var clients = new Queue<SearchClient>([new(clock, Reply()), new(clock, Reply())]);
        var reads = 0;
        var service = await UpnpIgdProtocol.DiscoverAsync(Home, () => clients.Dequeue(),
            (_, _) => ++reads == 1 ? throw new OperationCanceledException()
                : Task.FromResult(Description(UpnpIgdProtocol.ServiceTypes[1])), clock, default);
        Assert.NotNull(service);
        Assert.Equal(2, reads);
        Assert.Empty(clients);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProtocolSelection_NatPmpProbeStillPrecedesUpnp(bool natPmpAvailable)
    {
        var probed = false;
        var upnpCalls = 0;
        var upnp = new FakeNatRouter();
        var result = await AutomaticNatMappingBackend.DiscoverAsync(Home,
            (_, _) => { probed = true; return Task.FromResult(natPmpAvailable); },
            (_, _) =>
            {
                Assert.True(probed);
                upnpCalls++;
                return Task.FromResult<INatMappingBackend?>(upnp);
            }, default);
        Assert.Equal(natPmpAvailable ? 0 : 1, upnpCalls);
        if (natPmpAvailable) Assert.IsType<NatPmpMappingBackend>(result);
        else Assert.Same(upnp, result);
    }

    private static (byte[] Packet, IPAddress Sender) Reply(string sender = "192.168.1.1",
        string location = "http://192.168.1.1/root.xml") =>
        (Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nST: upnp:rootdevice\r\nUSN: uuid:router::upnp:rootdevice\r\nLOCATION: {location}\r\n\r\n"),
            IPAddress.Parse(sender));

    private sealed class SearchClient(ManualNatTimeProvider clock,
        params (byte[] Packet, IPAddress Sender)[] responses) : IUpnpSsdpClient
    {
        private readonly Queue<(byte[] Packet, IPAddress Sender)> replies = new(responses);
        public IPEndPoint? SentTo { get; private set; }
        public string Request { get; private set; } = "";
        public bool Disposed { get; private set; }
        public CancellationTokenSource? CancelOnReceive { get; init; }

        public Task SendAsync(byte[] packet, IPEndPoint endpoint, CancellationToken cancellationToken)
        {
            Assert.Null(SentTo);
            cancellationToken.ThrowIfCancellationRequested();
            SentTo = endpoint;
            Request = Encoding.ASCII.GetString(packet);
            return Task.CompletedTask;
        }

        public Task<(int Length, IPAddress Sender)> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            Assert.Equal(8193, buffer.Length);
            if (replies.TryDequeue(out var reply))
            {
                reply.Packet.CopyTo(buffer);
                return Task.FromResult((reply.Packet.Length, reply.Sender));
            }
            CancelOnReceive?.Cancel();
            clock.Advance(TimeSpan.FromMilliseconds(1201));
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("The per-search timeout must remain bounded.");
        }

        public void Dispose() => Disposed = true;
    }

    private static Uri? Parse(string packet) => UpnpIgdProtocol.ParseSsdp(Encoding.ASCII.GetBytes(packet), IPAddress.Parse(Home.Device.GatewayAddress), Home);

    private static byte[] Description(string serviceType, string control = "/control", string? urlBase = null, string device = "InternetGatewayDevice:2") =>
        Encoding.UTF8.GetBytes($"<root xmlns='urn:schemas-upnp-org:device-1-0'>{(urlBase is null ? "" : "<URLBase>" + urlBase + "</URLBase>")}<device><deviceType>urn:schemas-upnp-org:device:{device}</deviceType><deviceList><device><deviceType>urn:schemas-upnp-org:device:WANConnectionDevice:2</deviceType><serviceList><service><serviceType>{serviceType}</serviceType><controlURL>{control}</controlURL></service></serviceList></device></deviceList></device></root>");
}
