using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PeerCast.Nova.App.Tests;

public sealed class SystemPeerCastIndexTextSourceTests
{
    [Fact]
    public async Task ReadsUtf8IndexThroughBoundedHttp10Transport()
    {
        var body = "配信テスト<>0123456789ABCDEF0123456789ABCDEF<>127.0.0.1:7144\n";
        using var serverCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
            var server = ServeOnceAsync(listener, body, serverCancellation.Token);
            var source = SystemPeerCastIndexTextSource.Instance;
            var uri = new Uri(
                $"http://127.0.0.1:{endpoint.Port}/sp/index.txt?source=test");

            var text = await source.ReadAsync(uri, serverCancellation.Token);
            var request = await server;

            Assert.Equal(body, text);
            Assert.Contains(
                "GET /sp/index.txt?source=test HTTP/1.0\r\n",
                request,
                StringComparison.Ordinal);
            Assert.Contains(
                $"Host: 127.0.0.1:{endpoint.Port}\r\n",
                request,
                StringComparison.Ordinal);
            Assert.Contains(
                "Accept-Encoding: identity\r\n",
                request,
                StringComparison.Ordinal);
            Assert.Contains(
                "Connection: close\r\n",
                request,
                StringComparison.Ordinal);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ContentLengthCompletesBeforeServerClosesConnection()
    {
        var body = "配信テスト<>0123456789ABCDEF0123456789ABCDEF<>127.0.0.1:7144\n";
        using var serverCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var readCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var responseSent = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseConnection = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Task<string>? server = null;
        try
        {
            var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
            server = ServeOnceKeepingConnectionOpenAsync(
                listener,
                body,
                responseSent,
                releaseConnection.Task,
                serverCancellation.Token);
            var source = SystemPeerCastIndexTextSource.Instance;
            var uri = new Uri($"http://127.0.0.1:{endpoint.Port}/sp/index.txt");

            var read = source.ReadAsync(uri, readCancellation.Token).AsTask();
            await responseSent.Task.WaitAsync(serverCancellation.Token);
            var text = await read;

            Assert.Equal(body, text);
            Assert.False(server.IsCompleted);

            releaseConnection.TrySetResult(true);
            _ = await server;
        }
        finally
        {
            releaseConnection.TrySetResult(true);
            listener.Stop();
        }
    }

    [Fact]
    public async Task NonSuccessfulResponseIsRejected()
    {
        using var serverCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
            var server = ServeStatusOnceAsync(
                listener,
                "HTTP/1.0 503 Service Unavailable",
                serverCancellation.Token);
            var source = SystemPeerCastIndexTextSource.Instance;
            var uri = new Uri($"http://127.0.0.1:{endpoint.Port}/sp/index.txt");

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await source.ReadAsync(uri, serverCancellation.Token));
            _ = await server;
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task HttpsIndexIsRejectedBeforeConnecting()
    {
        var source = SystemPeerCastIndexTextSource.Instance;

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await source.ReadAsync(new Uri("https://example.invalid/index.txt")));
    }

    private static async Task<string> ServeOnceAsync(
        TcpListener listener,
        string body,
        CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = client.GetStream();
        var request = await ReadRequestAsync(stream, cancellationToken);
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.0 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header.AsMemory(), cancellationToken);
        await stream.WriteAsync(bodyBytes.AsMemory(), cancellationToken);
        await stream.FlushAsync(cancellationToken);
        return request;
    }

    private static async Task<string> ServeOnceKeepingConnectionOpenAsync(
        TcpListener listener,
        string body,
        TaskCompletionSource<bool> responseSent,
        Task releaseConnection,
        CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = client.GetStream();
        var request = await ReadRequestAsync(stream, cancellationToken);
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.0 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {bodyBytes.Length}\r\n\r\n");
        await stream.WriteAsync(header.AsMemory(), cancellationToken);
        await stream.WriteAsync(bodyBytes.AsMemory(), cancellationToken);
        await stream.FlushAsync(cancellationToken);
        responseSent.TrySetResult(true);
        await releaseConnection.WaitAsync(cancellationToken);
        return request;
    }

    private static async Task<string> ServeStatusOnceAsync(
        TcpListener listener,
        string statusLine,
        CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = client.GetStream();
        var request = await ReadRequestAsync(stream, cancellationToken);
        var response = Encoding.ASCII.GetBytes(
            $"{statusLine}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(response.AsMemory(), cancellationToken);
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
            var text = Encoding.ASCII.GetString(output.ToArray());
            if (text.Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                return text;
            }
        }

        throw new InvalidDataException("The test HTTP request was incomplete.");
    }
}
