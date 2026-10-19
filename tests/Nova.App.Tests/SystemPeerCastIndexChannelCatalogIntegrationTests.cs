using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PeerCast.Nova.App.Tests;

public sealed class SystemPeerCastIndexChannelCatalogIntegrationTests
{
    [Fact]
    public async Task LoopbackIndexProjectsPeerCastRowIntoGenesisChannel()
    {
        const string channelId = "0123456789ABCDEF0123456789ABCDEF";
        var body = BuildRow(
            name: "配信&lt;テスト&gt;",
            channelId: channelId,
            tracker: string.Empty,
            genre: "game",
            detail: "格闘ゲーム",
            listeners: "12",
            relays: "3",
            bitrate: "1800",
            type: "FLV",
            artist: "Artist",
            album: "Album",
            title: "Title",
            broadcastTime: "01:35",
            comment: "コメント") + "\n";
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
            var bodyBytes = Encoding.UTF8.GetBytes(body);
            var server = ServeOnceAsync(
                listener,
                bodyBytes,
                "utf-8",
                cancellationSource.Token);
            var indexUri = new Uri(
                $"http://127.0.0.1:{endpoint.Port}/sp/index.txt");
            var fallbackTracker = new Uri("pcp://fallback.example:7146/");
            var catalog = new HttpPeerCastIndexChannelCatalog(
                indexUri,
                fallbackTracker,
                SystemPeerCastIndexTextSource.Instance);

            var snapshot = await catalog.ReadAsync(cancellationSource.Token);
            var request = await server;

            AssertProjectedChannel(snapshot, channelId);
            Assert.Contains(
                "GET /sp/index.txt HTTP/1.0\r\n",
                request,
                StringComparison.Ordinal);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task LoopbackShiftJisIndexProjectsJapanesePeerCastRow()
    {
        const string channelId = "0123456789ABCDEF0123456789ABCDEF";
        var body = BuildRow(
            name: "配信&lt;テスト&gt;",
            channelId: channelId,
            tracker: string.Empty,
            genre: "game",
            detail: "格闘ゲーム",
            listeners: "12",
            relays: "3",
            bitrate: "1800",
            type: "FLV",
            artist: "Artist",
            album: "Album",
            title: "Title",
            broadcastTime: "01:35",
            comment: "コメント") + "\n";
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var shiftJis = Encoding.GetEncoding(
            932,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
            var server = ServeOnceAsync(
                listener,
                shiftJis.GetBytes(body),
                "Shift_JIS",
                cancellationSource.Token);
            var indexUri = new Uri(
                $"http://127.0.0.1:{endpoint.Port}/sp/index.txt");
            var fallbackTracker = new Uri("pcp://fallback.example:7146/");
            var catalog = new HttpPeerCastIndexChannelCatalog(
                indexUri,
                fallbackTracker,
                SystemPeerCastIndexTextSource.Instance);

            var snapshot = await catalog.ReadAsync(cancellationSource.Token);
            _ = await server;

            AssertProjectedChannel(snapshot, channelId);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static void AssertProjectedChannel(
        GenesisChannelCatalogSnapshot snapshot,
        string channelId)
    {
        var channel = Assert.Single(snapshot.Channels);
        Assert.Equal("配信<テスト>", channel.Name);
        Assert.Equal(
            $"pcp://fallback.example:7146/channel/{channelId}",
            channel.Source);
        Assert.Equal(
            "[game - 格闘ゲーム] コメント Artist/Title/Album",
            channel.Description);
        Assert.Equal(12, channel.Listeners);
        Assert.Equal(3, channel.Relays);
        Assert.Equal(1800, channel.BitrateKbps);
        Assert.Equal(TimeSpan.FromMinutes(95), channel.Uptime);
        Assert.Equal("FLV", channel.Type);
    }

    private static async Task<string> ServeOnceAsync(
        TcpListener listener,
        byte[] body,
        string charset,
        CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = client.GetStream();
        var request = await ReadRequestAsync(stream, cancellationToken);
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.0 200 OK\r\nContent-Type: text/plain; charset={charset}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header.AsMemory(), cancellationToken);
        await stream.WriteAsync(body.AsMemory(), cancellationToken);
        await stream.FlushAsync(cancellationToken);
        return request;
    }

    private static async Task<string> ReadRequestAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[1024];
        while (output.Length < 16 * 1024)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            var request = Encoding.ASCII.GetString(output.ToArray());
            if (request.Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                return request;
            }
        }

        throw new InvalidDataException("The test HTTP request was incomplete.");
    }

    private static string BuildRow(
        string name,
        string channelId,
        string tracker,
        string genre,
        string detail,
        string listeners,
        string relays,
        string bitrate,
        string type,
        string artist,
        string album,
        string title,
        string broadcastTime,
        string comment)
    {
        var fields = new[]
        {
            name,
            channelId,
            tracker,
            string.Empty,
            genre,
            detail,
            listeners,
            relays,
            bitrate,
            type,
            artist,
            album,
            title,
            string.Empty,
            string.Empty,
            broadcastTime,
            string.Empty,
            comment,
        };
        return string.Join("<>", fields);
    }
}
