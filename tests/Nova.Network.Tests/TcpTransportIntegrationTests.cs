using System.Net;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.Network.Tests;

public sealed class TcpTransportIntegrationTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ConnectAndAccept_CanExchangeBytes()
    {
        var options = CreateOptions();

        await using var listener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            options);

        listener.Start();

        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var acceptTask = listener.AcceptAsync(timeoutSource.Token).AsTask();

        var connector = new TcpTransportConnector(options);

        await using var client = await connector.ConnectAsync(
            listener.LocalEndPoint,
            timeoutSource.Token);

        await using var server = await acceptTask;

        byte[] payload = [0x50, 0x43, 0x4E, 0x01];
        await client.WriteAsync(payload, timeoutSource.Token);

        var received = new byte[payload.Length];
        await ReadExactlyAsync(server, received, timeoutSource.Token);

        Assert.Equal(payload, received);
        Assert.Equal(client.LocalEndPoint, server.RemoteEndPoint);
        Assert.Equal(client.RemoteEndPoint, server.LocalEndPoint);
    }

    [Fact]
    public async Task Accept_WithCanceledToken_ThrowsOperationCanceledException()
    {
        await using var listener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            CreateOptions());

        listener.Start();

        using var canceledSource = new CancellationTokenSource();
        canceledSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => listener.AcceptAsync(canceledSource.Token).AsTask());
    }

    [Fact]
    public async Task Stop_CancelsPendingAccept()
    {
        await using var listener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            CreateOptions());

        listener.Start();

        var acceptTask = listener.AcceptAsync().AsTask();

        await listener.StopAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => acceptTask);
    }

    [Fact]
    public async Task ConnectionLimit_BlocksSecondAcceptUntilFirstConnectionIsDisposed()
    {
        var options = CreateOptions() with
        {
            MaxConcurrentConnections = 1,
        };

        await using var listener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            options);

        listener.Start();

        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var connector = new TcpTransportConnector(options);

        var firstAcceptTask = listener.AcceptAsync(timeoutSource.Token).AsTask();

        await using var firstClient = await connector.ConnectAsync(
            listener.LocalEndPoint,
            timeoutSource.Token);

        var firstServer = await firstAcceptTask;

        var secondAcceptTask = listener.AcceptAsync(timeoutSource.Token).AsTask();

        await using var secondClient = await connector.ConnectAsync(
            listener.LocalEndPoint,
            timeoutSource.Token);

        var earlyCompletion = await Task.WhenAny(
            secondAcceptTask,
            Task.Delay(TimeSpan.FromMilliseconds(100), timeoutSource.Token));

        Assert.NotSame(secondAcceptTask, earlyCompletion);

        await firstServer.DisposeAsync();

        await using var secondServer = await secondAcceptTask;

        Assert.Equal(secondClient.LocalEndPoint, secondServer.RemoteEndPoint);
    }

    [Fact]
    public async Task Connect_WithPreCanceledToken_ThrowsOperationCanceledException()
    {
        var connector = new TcpTransportConnector(CreateOptions());

        using var canceledSource = new CancellationTokenSource();
        canceledSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => connector.ConnectAsync(
                new IPEndPoint(IPAddress.Loopback, 9),
                canceledSource.Token).AsTask());
    }

    private static TcpTransportOptions CreateOptions()
    {
        return new TcpTransportOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(2),
            ListenBacklog = 8,
            MaxConcurrentConnections = 8,
        };
    }

    private static async Task ReadExactlyAsync(
        TcpTransportConnection connection,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        var totalRead = 0;

        while (totalRead < destination.Length)
        {
            var read = await connection.ReadAsync(
                destination[totalRead..],
                cancellationToken);

            if (read == 0)
            {
                throw new EndOfStreamException(
                    "The connection closed before the expected bytes were received.");
            }

            totalRead += read;
        }
    }
}
