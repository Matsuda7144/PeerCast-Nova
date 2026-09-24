using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpHttpFlvPlaybackTests
{
    private const uint HeadPacketType = 0x64616568;
    private const uint DataPacketType = 0x61746164;

    private static readonly ChannelId ChannelId = new(
        Guid.ParseExact("531DC8DFC7FB42928AC2C0A626517A87", "N"));

    private static readonly ChannelSessionId SessionId = new(
        Guid.ParseExact("3F2504E04F8911D39A0C0305E82C3301", "N"));

    [Fact]
    public async Task Stream_SendsHttpHeadersThenOrderedBootstrapAndOwnedLivePackets()
    {
        string? readyUrl = null;
        await using var host = new PcpHttpFlvPlaybackHost(
            streamUrlReady: url => readyUrl = url);
        host.Start();
        await using var source = host.Create(ChannelId, SessionId);
        Assert.Null(readyUrl);

        byte[] header = CreateFlvHeader();
        byte[] metadata = CreateTag(18, [0x02, 0, 0, 0]);
        byte[] audioConfiguration = CreateTag(8, [0xAF, 0, 0x12, 0x10]);
        byte[] videoConfiguration = CreateTag(
            9,
            [0x17, 0, 0, 0, 0, 1, 0x64]);
        byte[] sourceHead = Combine(
            header,
            metadata,
            audioConfiguration,
            videoConfiguration);

        await source.ConsumeAsync(
            CreatePacket(0, HeadPacketType, sourceHead),
            CancellationToken.None);
        Assert.Equal(host.StreamUrlFor(ChannelId), readyUrl);

        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, host.LocalEndPoint.Port);
        var stream = client.GetStream();
        var request = Encoding.ASCII.GetBytes(
            $"GET /stream/{ChannelId} HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n");
        await stream.WriteAsync(request);

        string responseHeader = await ReadHttpHeaderAsync(stream);
        Assert.StartsWith("HTTP/1.1 200 OK\r\n", responseHeader);
        Assert.Contains("Content-Type: video/x-flv\r\n", responseHeader);

        byte[] expectedBootstrap = Combine(
            header,
            metadata,
            audioConfiguration,
            videoConfiguration);
        byte[] actualBootstrap = await ReadExactlyAsync(
            stream,
            expectedBootstrap.Length);
        Assert.Equal(expectedBootstrap, actualBootstrap);

        byte[] firstLivePacket = CreateTag(8, [0xAF, 1, 0x11, 0x22]);
        byte[] secondLivePacket = CreateTag(
            9,
            [0x27, 1, 0, 0, 0, 0x65],
            timestamp: 12_345);
        byte[] thirdLivePacket = CreateTag(
            9,
            [0x27, 1, 0, 0, 0, 0x41],
            timestamp: 24_690);
        await source.ConsumeAsync(
            CreatePacket(1, DataPacketType, firstLivePacket),
            CancellationToken.None);
        firstLivePacket.AsSpan().Fill(0xEE);
        await source.ConsumeAsync(
            CreatePacket(2, DataPacketType, secondLivePacket),
            CancellationToken.None);
        await source.ConsumeAsync(
            CreatePacket(3, DataPacketType, thirdLivePacket),
            CancellationToken.None);

        byte[] actualFirstLivePacket = await ReadExactlyAsync(
            stream,
            CreateTag(8, [0xAF, 1, 0x11, 0x22]).Length);
        byte[] actualSecondLivePacket = await ReadExactlyAsync(
            stream,
            secondLivePacket.Length);
        byte[] actualThirdLivePacket = await ReadExactlyAsync(
            stream,
            thirdLivePacket.Length);

        Assert.Equal(
            CreateTag(8, [0xAF, 1, 0x11, 0x22]),
            actualFirstLivePacket);
        Assert.Equal(secondLivePacket, actualSecondLivePacket);
        Assert.Equal(thirdLivePacket, actualThirdLivePacket);
        Assert.Equal(IPAddress.Loopback, host.LocalEndPoint.Address);
    }

    [Fact]
    public async Task Stream_ClientDisconnectReleasesItsBoundedChild()
    {
        await using var host = new PcpHttpFlvPlaybackHost();
        host.Start();
        await using var source = host.Create(ChannelId, SessionId);
        await source.ConsumeAsync(
            CreatePacket(0, HeadPacketType, CreateFlvHeader()),
            CancellationToken.None);

        var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, host.LocalEndPoint.Port);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            $"GET /stream/{ChannelId} HTTP/1.1\r\nHost: localhost\r\n\r\n"));
        var response = await ReadHttpHeaderAsync(stream);
        Assert.StartsWith("HTTP/1.1 200 OK\r\n", response);
        _ = await ReadExactlyAsync(stream, 13);
        await WaitUntilAsync(() => host.ActiveStreamClientCount == 1);

        client.Dispose();
        await WaitUntilAsync(() =>
            host.ActiveStreamClientCount == 0 && host.ActiveConnectionCount == 0);
    }

    [Fact]
    public async Task Stream_UpstreamStopClosesEstablishedHttpClient()
    {
        await using var host = new PcpHttpFlvPlaybackHost();
        host.Start();
        var source = host.Create(ChannelId, SessionId);
        await source.ConsumeAsync(
            CreatePacket(0, HeadPacketType, CreateFlvHeader()),
            CancellationToken.None);

        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, host.LocalEndPoint.Port);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            $"GET /stream/{ChannelId} HTTP/1.1\r\nHost: localhost\r\n\r\n"));
        var response = await ReadHttpHeaderAsync(stream);
        Assert.StartsWith("HTTP/1.1 200 OK\r\n", response);
        _ = await ReadExactlyAsync(stream, 13);
        await WaitUntilAsync(() => host.ActiveStreamClientCount == 1);

        await source.DisposeAsync();

        var buffer = new byte[1];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var read = await stream.ReadAsync(buffer, timeout.Token);
        Assert.Equal(0, read);
        Assert.Equal(0, host.ActiveStreamClientCount);
    }

    [Fact]
    public async Task Stream_RejectsClientsBeyondBoundAndReleasesSlotAfterDisconnect()
    {
        await using var host = new PcpHttpFlvPlaybackHost(
            new PcpHttpFlvPlaybackOptions
            {
                MaximumConcurrentStreamClients = 1,
                Transport = new PeerCast.Nova.Network.Transport.TcpTransportOptions
                {
                    MaxConcurrentConnections = 2,
                },
            });
        host.Start();
        await using var source = host.Create(ChannelId, SessionId);
        await source.ConsumeAsync(
            CreatePacket(0, HeadPacketType, CreateFlvHeader()),
            CancellationToken.None);

        using var firstClient = new TcpClient(AddressFamily.InterNetwork);
        await firstClient.ConnectAsync(IPAddress.Loopback, host.LocalEndPoint.Port);
        var firstStream = firstClient.GetStream();
        await firstStream.WriteAsync(Encoding.ASCII.GetBytes(
            $"GET /stream/{ChannelId} HTTP/1.1\r\nHost: localhost\r\n\r\n"));
        var firstResponse = await ReadHttpHeaderAsync(firstStream);
        Assert.StartsWith("HTTP/1.1 200 OK\r\n", firstResponse);
        _ = await ReadExactlyAsync(firstStream, 13);
        await WaitUntilAsync(() => host.ActiveStreamClientCount == 1);

        using var rejectedClient = new TcpClient(AddressFamily.InterNetwork);
        await rejectedClient.ConnectAsync(IPAddress.Loopback, host.LocalEndPoint.Port);
        var rejectedStream = rejectedClient.GetStream();
        await rejectedStream.WriteAsync(Encoding.ASCII.GetBytes(
            $"GET /stream/{ChannelId} HTTP/1.1\r\nHost: localhost\r\n\r\n"));
        var rejectedResponse = await ReadHttpHeaderAsync(rejectedStream);
        Assert.StartsWith("HTTP/1.1 503 Service Unavailable\r\n", rejectedResponse);

        firstClient.Dispose();
        await WaitUntilAsync(() =>
            host.ActiveStreamClientCount == 0 && host.ActiveConnectionCount == 0);

        using var replacementClient = new TcpClient(AddressFamily.InterNetwork);
        await replacementClient.ConnectAsync(IPAddress.Loopback, host.LocalEndPoint.Port);
        var replacementStream = replacementClient.GetStream();
        await replacementStream.WriteAsync(Encoding.ASCII.GetBytes(
            $"GET /stream/{ChannelId} HTTP/1.1\r\nHost: localhost\r\n\r\n"));
        var replacementResponse = await ReadHttpHeaderAsync(replacementStream);
        Assert.StartsWith("HTTP/1.1 200 OK\r\n", replacementResponse);
        _ = await ReadExactlyAsync(replacementStream, 13);
    }

    [Fact]
    public async Task HostDispose_CancelsIncompleteHttpRequestWithinBound()
    {
        var host = new PcpHttpFlvPlaybackHost();
        host.Start();

        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, host.LocalEndPoint.Port);
        await WaitUntilAsync(() => host.ActiveConnectionCount == 1);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await host.DisposeAsync().AsTask().WaitAsync(timeout.Token);

        Assert.Equal(0, host.ActiveConnectionCount);
        Assert.Equal(0, host.ActiveStreamClientCount);
        await host.DisposeAsync();
    }

    [Fact]
    public void Options_RejectClientBoundBeyondTransportConnectionLimit()
    {
        var options = new PcpHttpFlvPlaybackOptions
        {
            MaximumConcurrentStreamClients = 2,
            Transport = new PeerCast.Nova.Network.Transport.TcpTransportOptions
            {
                MaxConcurrentConnections = 1,
            },
        };

        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }

    private static PcpChannelPacket CreatePacket(
        long sequence,
        uint type,
        byte[] payload)
    {
        var wirePosition = checked((uint)(100 + sequence));
        return new PcpChannelPacket(
            ChannelId,
            SessionId,
            sequence,
            type,
            wirePosition,
            wirePosition,
            payload,
            null);
    }

    private static byte[] CreateFlvHeader() =>
    [
        (byte)'F', (byte)'L', (byte)'V', 1, 5,
        0, 0, 0, 9,
        0, 0, 0, 0,
    ];

    private static byte[] CreateTag(
        byte tagType,
        byte[] body,
        uint timestamp = 0)
    {
        var tag = new byte[11 + body.Length + 4];
        tag[0] = tagType;
        tag[1] = (byte)(body.Length >> 16);
        tag[2] = (byte)(body.Length >> 8);
        tag[3] = (byte)body.Length;
        tag[4] = (byte)(timestamp >> 16);
        tag[5] = (byte)(timestamp >> 8);
        tag[6] = (byte)timestamp;
        tag[7] = (byte)(timestamp >> 24);
        body.CopyTo(tag, 11);
        BinaryPrimitives.WriteUInt32BigEndian(
            tag.AsSpan(tag.Length - 4),
            checked((uint)(11 + body.Length)));
        return tag;
    }

    private static byte[] Combine(params byte[][] parts)
    {
        var result = new byte[parts.Sum(part => part.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }

    private static async Task<string> ReadHttpHeaderAsync(Stream stream)
    {
        var bytes = new List<byte>();
        var buffer = new byte[1];
        while (bytes.Count < 8192)
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0)
            {
                throw new EndOfStreamException(
                    "The HTTP response ended before its header was complete.");
            }

            bytes.Add(buffer[0]);
            if (bytes.Count >= 4 &&
                bytes[^4] == '\r' && bytes[^3] == '\n' &&
                bytes[^2] == '\r' && bytes[^1] == '\n')
            {
                return Encoding.ASCII.GetString(bytes.ToArray());
            }
        }

        throw new InvalidDataException("The HTTP response header exceeded the test bound.");
    }

    private static async Task<byte[]> ReadExactlyAsync(
        Stream stream,
        int length)
    {
        var result = new byte[length];
        await stream.ReadExactlyAsync(result);
        return result;
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        int timeoutMilliseconds = 3_000)
    {
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (!condition())
        {
            if (Environment.TickCount64 >= deadline)
            {
                throw new TimeoutException(
                    "The expected HTTP-FLV client state was not reached.");
            }

            await Task.Delay(10);
        }
    }
}
