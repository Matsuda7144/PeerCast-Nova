using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PeerCast.Nova.Network.PortMapping;

internal sealed record UpnpIgdService(string ServiceType, Uri ControlUrl);
internal sealed record UpnpSoapReply(int? ErrorCode, XElement? Response);

internal interface IUpnpSsdpClient : IDisposable
{
    Task SendAsync(byte[] packet, IPEndPoint endpoint, CancellationToken cancellationToken);
    Task<(int Length, IPAddress Sender)> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken);
}

internal interface IUpnpIgdTransport
{
    Task<UpnpSoapReply> InvokeAsync(string action, IReadOnlyList<KeyValuePair<string, string>> arguments,
        CancellationToken cancellationToken);
}

/// <summary>Only the selected gateway's numeric HTTP endpoints are admitted.</summary>
internal static class UpnpIgdProtocol
{
    internal const int MaximumXmlBytes = 65536;
    internal static readonly XNamespace DeviceNamespace = "urn:schemas-upnp-org:device-1-0";
    internal static readonly XNamespace SoapNamespace = "http://schemas.xmlsoap.org/soap/envelope/";
    internal static readonly string[] ServiceTypes =
    [
        "urn:schemas-upnp-org:service:WANIPConnection:2",
        "urn:schemas-upnp-org:service:WANIPConnection:1",
        "urn:schemas-upnp-org:service:WANPPPConnection:1",
    ];

    internal static bool IsGatewayUrl(Uri uri, NatNetworkIdentity network) =>
        uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttp && uri.UserInfo.Length == 0 &&
        uri.Fragment.Length == 0 && uri.HostNameType == UriHostNameType.IPv4 &&
        IPAddress.TryParse(uri.Host, out var address) && address.ToString() == network.Device.GatewayAddress;

    internal static Uri? ParseSsdp(ReadOnlySpan<byte> packet, IPAddress sender, NatNetworkIdentity network)
    {
        if (packet.Length > 8192 || sender.ToString() != network.Device.GatewayAddress) return null;
        var lines = Encoding.ASCII.GetString(packet).Split("\r\n", StringSplitOptions.None);
        if (lines.Length < 4 || lines[0] != "HTTP/1.1 200 OK" || !lines.Contains(string.Empty)) return null;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            if (line.Length == 0) break;
            var colon = line.IndexOf(':');
            if (colon < 1 || !headers.TryAdd(line[..colon].Trim(), line[(colon + 1)..].Trim())) return null;
        }
        return headers.TryGetValue("ST", out var st) && st == "upnp:rootdevice" &&
            headers.TryGetValue("USN", out var usn) && usn.StartsWith("uuid:", StringComparison.Ordinal) &&
            headers.TryGetValue("LOCATION", out var location) && Uri.TryCreate(location, UriKind.Absolute, out var uri) &&
            IsGatewayUrl(uri, network) ? uri : null;
    }

    internal static XDocument ParseXml(byte[] bytes)
    {
        if (bytes.Length > MaximumXmlBytes) throw new XmlException("UPnP response too large.");
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumXmlBytes,
            MaxCharactersFromEntities = 0,
        });
        return XDocument.Load(reader);
    }

    internal static UpnpIgdService? ParseDescription(byte[] bytes, Uri location, NatNetworkIdentity network)
    {
        if (!IsGatewayUrl(location, network)) return null;
        var doc = ParseXml(bytes);
        var ns = DeviceNamespace;
        if (doc.Root?.Name != ns + "root") return null;
        var rootDevice = doc.Root.Element(ns + "device");
        if (rootDevice?.Element(ns + "deviceType")?.Value is not
            ("urn:schemas-upnp-org:device:InternetGatewayDevice:1" or "urn:schemas-upnp-org:device:InternetGatewayDevice:2")) return null;
        var baseUrl = location;
        if (doc.Root.Element(ns + "URLBase") is { } value &&
            (!Uri.TryCreate(value.Value, UriKind.Absolute, out baseUrl) || !IsGatewayUrl(baseUrl, network))) return null;
        foreach (var type in ServiceTypes)
        {
            foreach (var service in rootDevice.Descendants(ns + "service"))
            {
                var deviceType = service.Parent?.Parent?.Element(ns + "deviceType")?.Value;
                if (deviceType is not ("urn:schemas-upnp-org:device:WANConnectionDevice:1" or
                    "urn:schemas-upnp-org:device:WANConnectionDevice:2")) continue;
                if (service.Element(ns + "serviceType")?.Value == type &&
                    service.Element(ns + "controlURL") is { } control &&
                    Uri.TryCreate(baseUrl, control.Value, out var url) && IsGatewayUrl(url, network))
                    return new(type, url);
            }
        }
        return null;
    }

    internal static Task<UpnpIgdService?> DiscoverAsync(NatNetworkIdentity network, CancellationToken cancellationToken) =>
        DiscoverAsync(network, () => new UpnpSsdpSocket(network), async (location, token) =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, location);
            return await SendAsync(network, request, token).ConfigureAwait(false);
        }, TimeProvider.System, cancellationToken);

    internal static async Task<UpnpIgdService?> DiscoverAsync(NatNetworkIdentity network,
        Func<IUpnpSsdpClient> createClient, Func<Uri, CancellationToken, Task<byte[]>> readDescription,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        network.Validate();
        // Only a usable IGD service suppresses fallback: a valid SSDP location
        // may still describe an unsupported device or an invalid control URL.
        IPEndPoint[] endpoints =
        [
            new(IPAddress.Parse("239.255.255.250"), 1900),
            new(IPAddress.Parse(network.Device.GatewayAddress), 1900),
        ];
        foreach (var endpoint in endpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var client = createClient();
            var locations = await SearchAsync(network, endpoint, client, clock, cancellationToken).ConfigureAwait(false);
            foreach (var location in locations.OrderBy(item => item.AbsoluteUri, StringComparer.Ordinal))
            {
                try
                {
                    var bytes = await readDescription(location, cancellationToken).ConfigureAwait(false);
                    var service = ParseDescription(bytes, location, network);
                    if (service is not null) return service;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
                catch (Exception exception) when (exception is XmlException or HttpRequestException or IOException) { }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return null;
    }

    private static async Task<HashSet<Uri>> SearchAsync(NatNetworkIdentity network, IPEndPoint endpoint,
        IUpnpSsdpClient client, TimeProvider clock, CancellationToken cancellationToken)
    {
        var locations = new HashSet<Uri>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(1200), clock);
        using var search = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var packet = Encoding.ASCII.GetBytes($"M-SEARCH * HTTP/1.1\r\nHOST: {endpoint}\r\nMAN: \"ssdp:discover\"\r\nMX: 1\r\nST: upnp:rootdevice\r\n\r\n");
            await client.SendAsync(packet, endpoint, search.Token).ConfigureAwait(false);
            var buffer = new byte[8193];
            for (var count = 0; count < 64 && locations.Count < 4; count++)
            {
                var received = await client.ReceiveAsync(buffer, search.Token).ConfigureAwait(false);
                var location = ParseSsdp(buffer.AsSpan(0, received.Length), received.Sender, network);
                if (location is not null)
                {
                    locations.Add(location);
                    // A directed rootdevice response supplies one gateway
                    // description; leave time for HTTP within the manager's budget.
                    if (endpoint.Address.ToString() == network.Device.GatewayAddress) return locations;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        return locations;
    }

    internal static async Task<byte[]> SendAsync(NatNetworkIdentity network, HttpRequestMessage request,
        CancellationToken cancellationToken, bool allowSoapFault = false)
    {
        var uri = request.RequestUri!;
        if (!IsGatewayUrl(uri, network)) throw new HttpRequestException("Invalid gateway URL.");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(2));
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            MaxResponseHeadersLength = 16,
            ConnectTimeout = TimeSpan.FromSeconds(1),
            ConnectCallback = async (context, token) =>
            {
                if (context.DnsEndPoint.Host != uri.Host || context.DnsEndPoint.Port != uri.Port)
                    throw new HttpRequestException("Gateway endpoint changed.");
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    socket.Bind(new IPEndPoint(IPAddress.Parse(network.InternalAddress), 0));
                    await socket.ConnectAsync(new IPEndPoint(IPAddress.Parse(network.Device.GatewayAddress), uri.Port), token).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch { socket.Dispose(); throw; }
            },
        };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode && !(allowSoapFault && response.StatusCode == HttpStatusCode.InternalServerError))
            throw new HttpRequestException("UPnP HTTP response rejected.");
        if (response.Content.Headers.ContentLength > MaximumXmlBytes) throw new IOException("UPnP response too large.");
        using var stream = await response.Content.ReadAsStreamAsync(budget.Token).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        int length;
        while ((length = await stream.ReadAsync(buffer, budget.Token).ConfigureAwait(false)) != 0)
        {
            if (output.Length + length > MaximumXmlBytes) throw new IOException("UPnP response too large.");
            output.Write(buffer, 0, length);
        }
        var bytes = output.ToArray();
        if (response.StatusCode == HttpStatusCode.InternalServerError &&
            ParseXml(bytes).Root?.Element(SoapNamespace + "Body")?.Element(SoapNamespace + "Fault") is null)
            throw new XmlException("HTTP error without SOAP fault.");
        return bytes;
    }
}

internal sealed class UpnpSsdpSocket : IUpnpSsdpClient
{
    private readonly Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

    internal UpnpSsdpSocket(NatNetworkIdentity network)
    {
        try
        {
            socket.Bind(new IPEndPoint(IPAddress.Parse(network.InternalAddress), 0));
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface,
                IPAddress.Parse(network.InternalAddress).GetAddressBytes());
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 2);
        }
        catch { socket.Dispose(); throw; }
    }

    public async Task SendAsync(byte[] packet, IPEndPoint endpoint, CancellationToken cancellationToken) =>
        _ = await socket.SendToAsync(packet, SocketFlags.None, endpoint, cancellationToken).ConfigureAwait(false);

    public async Task<(int Length, IPAddress Sender)> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0),
            cancellationToken).ConfigureAwait(false);
        return (result.ReceivedBytes, ((IPEndPoint)result.RemoteEndPoint).Address);
    }

    public void Dispose() => socket.Dispose();
}

internal sealed class UpnpIgdTransport(NatNetworkIdentity network, UpnpIgdService service) : IUpnpIgdTransport
{
    public async Task<UpnpSoapReply> InvokeAsync(string action, IReadOnlyList<KeyValuePair<string, string>> arguments,
        CancellationToken cancellationToken)
    {
        XNamespace soap = UpnpIgdProtocol.SoapNamespace;
        XNamespace ns = service.ServiceType;
        var xml = new XDocument(new XElement(soap + "Envelope", new XAttribute(XNamespace.Xmlns + "s", soap),
            new XAttribute(soap + "encodingStyle", "http://schemas.xmlsoap.org/soap/encoding/"),
            new XElement(soap + "Body", new XElement(ns + action, new XAttribute(XNamespace.Xmlns + "u", ns),
                arguments.Select(pair => new XElement(pair.Key, pair.Value))))));
        using var request = new HttpRequestMessage(HttpMethod.Post, service.ControlUrl);
        request.Headers.Add("SOAPAction", $"\"{service.ServiceType}#{action}\"");
        request.Content = new StringContent(xml.ToString(SaveOptions.DisableFormatting), Encoding.UTF8,
            new MediaTypeHeaderValue("text/xml"));
        var bytes = await UpnpIgdProtocol.SendAsync(network, request, cancellationToken, allowSoapFault: true).ConfigureAwait(false);
        return ParseReply(bytes, service.ServiceType, action);
    }

    internal static UpnpSoapReply ParseReply(byte[] bytes, string serviceType, string action)
    {
        var doc = UpnpIgdProtocol.ParseXml(bytes);
        var body = doc.Root?.Name == UpnpIgdProtocol.SoapNamespace + "Envelope"
            ? doc.Root.Element(UpnpIgdProtocol.SoapNamespace + "Body") : null;
        if (body?.Element(UpnpIgdProtocol.SoapNamespace + "Fault") is { } fault)
        {
            XNamespace control = "urn:schemas-upnp-org:control-1-0";
            var code = fault.Element("detail")?.Element(control + "UPnPError")?.Element(control + "errorCode")?.Value;
            if (int.TryParse(code, NumberStyles.None, CultureInfo.InvariantCulture, out var error)) return new(error, null);
        }
        if (body?.Element(XName.Get(action + "Response", serviceType)) is { } response) return new(null, response);
        throw new XmlException("Invalid UPnP SOAP response.");
    }
}
