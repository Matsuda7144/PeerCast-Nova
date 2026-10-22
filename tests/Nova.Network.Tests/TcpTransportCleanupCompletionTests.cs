using System.Net;
using System.Net.Sockets;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.Network.Tests;

public sealed class TcpTransportCleanupCompletionTests
{
    [Fact]
    public async Task DuplicateDisposeAndIsDisposedDoNotProveOwnerCallbackCompleted()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var accept = listener.AcceptTcpClientAsync(timeout.Token);
        using var peer = new TcpClient();
        await peer.ConnectAsync((IPEndPoint)listener.LocalEndpoint, timeout.Token);
        var accepted = await accept;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        await using var connection = new TcpTransportConnection(accepted, () =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
        });
        Assert.False(connection.CleanupCompletion.IsCompleted);
        var disposing = Task.Run(async () => await connection.DisposeAsync());
        try
        {
            await entered.Task.WaitAsync(timeout.Token);
            Assert.True(connection.IsDisposed);
            Assert.False(connection.CleanupCompletion.IsCompleted);
            await connection.DisposeAsync();
            Assert.False(connection.CleanupCompletion.IsCompleted);
        }
        finally { release.Set(); }
        await disposing.WaitAsync(timeout.Token);
        await connection.CleanupCompletion.WaitAsync(timeout.Token);
        Assert.True(connection.CleanupCompletion.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task OwnerCallbackFailureStillObservesRealSocketClosure()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var accept = listener.AcceptTcpClientAsync(timeout.Token);
        using var peer = new TcpClient();
        await peer.ConnectAsync((IPEndPoint)listener.LocalEndpoint, timeout.Token);
        await using var connection = new TcpTransportConnection(await accept, () => throw new InvalidOperationException("callback"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await connection.DisposeAsync());
        await connection.CleanupCompletion.WaitAsync(timeout.Token);
        Assert.True(connection.IsDisposed);
        Assert.True(connection.CleanupCompletion.IsCompletedSuccessfully);
        Assert.Equal(0, await peer.GetStream().ReadAsync(new byte[1], timeout.Token));
        await connection.DisposeAsync();
    }
}
