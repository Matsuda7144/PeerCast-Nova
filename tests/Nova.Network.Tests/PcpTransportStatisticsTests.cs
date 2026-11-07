using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpTransportStatisticsTests
{
    [Fact]
    public void OnlyEstablishedSessionsCountAndDuplicateOwnershipDoesNotDoubleCount()
    {
        var stats = new PcpTransportStatistics();
        var first = stats.CreateCounter(1);
        first.AddReceived(100); // TCP/negotiation alone is not an established PCP session.
        Assert.Equal(0, stats.Capture().Connections);
        using var one = first.Establish();
        var generation = stats.Capture().Generation;
        using var duplicate = first.Establish();
        Assert.Equal(1, stats.Capture().Connections);
        Assert.Equal(generation, stats.Capture().Generation);
        using var two = stats.CreateCounter(2).Establish(); // Same host may own multiple sessions.
        Assert.Equal(2, stats.Capture().Connections);
        duplicate.Dispose();
        Assert.Equal(2, stats.Capture().Connections);
        first.Close();
        one.Dispose();
        Assert.Equal(1, stats.Capture().Connections);
        two.Dispose();
        Assert.Equal(0, stats.Capture().Connections);
        Assert.True(stats.Capture().Generation > generation);
    }

    [Fact]
    public async Task ConcurrentSuccessfulByteUpdatesAndCapturesAreSafe()
    {
        var stats = new PcpTransportStatistics();
        var counter = stats.CreateCounter(1);
        using var session = counter.Establish();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 1000; i++)
            {
                counter.AddSent(3); counter.AddReceived(7);
                Assert.True(stats.Capture().IsValid);
            }
        })));
        Assert.Equal(12000, stats.Capture().SentBytes);
        Assert.Equal(28000, stats.Capture().ReceivedBytes);
    }

    [Fact]
    public void OverflowAndDuplicateTransportIdentityAreUnknownRatherThanWrappedTotals()
    {
        var stats = new PcpTransportStatistics();
        var counter = stats.CreateCounter(1);
        using var session = counter.Establish();
        counter.AddSent(long.MaxValue);
        counter.AddSent(1);
        Assert.False(stats.Capture().IsValid);
        using var conflict = stats.CreateCounter(1).Establish();
        Assert.Equal(1, stats.Capture().Connections);
        Assert.False(stats.Capture().IsValid);
    }

    [Fact]
    public async Task LoopbackDirectAndPipeReaderCountTransportReadsOnceAndClearAfterClose()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var peer = new TcpClient();
        var accepted = listener.AcceptTcpClientAsync(timeout.Token).AsTask();
        await peer.ConnectAsync((IPEndPoint)listener.LocalEndpoint, timeout.Token);
        var stats = new PcpTransportStatistics();
        await using var connection = new TcpTransportConnection(await accepted, statistics: stats);
        Assert.Equal(0, stats.Capture().Connections);
        using var session = connection.ObservePcpSession();
        await peer.GetStream().WriteAsync(new byte[17], timeout.Token);
        var read = 0;
        var buffer = new byte[17];
        while (read < 17) read += await connection.ReadAsync(buffer.AsMemory(read), timeout.Token);
        await connection.WriteAsync(new byte[11], timeout.Token);
        await peer.GetStream().ReadExactlyAsync(new byte[11], timeout.Token);
        var reader = PipeReader.Create(connection.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
        await peer.GetStream().WriteAsync(new byte[23], timeout.Token);
        long consumed = 0;
        while (consumed < 23)
        {
            var result = await reader.ReadAsync(timeout.Token);
            consumed += result.Buffer.Length;
            reader.AdvanceTo(result.Buffer.End);
        }
        Assert.Equal(40, stats.Capture().ReceivedBytes);
        Assert.Equal(11, stats.Capture().SentBytes);
        await reader.CompleteAsync();
        await connection.DisposeAsync();
        Assert.Equal(0, stats.Capture().Connections);
        Assert.Equal(0, stats.Capture().ReceivedBytes);
    }

    [Fact]
    public void SynchronousAndArrayIoUsesReturnedReadLengthsAndCountsWritesOnce()
    {
        var stats = new PcpTransportStatistics();
        var counter = stats.CreateCounter(1);
        using var session = counter.Establish();
        using var stream = new TransportObservationStream(new MemoryStream(new byte[5], writable: true), counter);
        Assert.Equal(5, stream.Read(new byte[20], 0, 20));
        Assert.Equal(0, stream.Read(new byte[20], 0, 20));
        stream.Position = 0;
        stream.Write(new byte[3], 0, 3);
        Assert.Equal(5, stats.Capture().ReceivedBytes);
        Assert.Equal(3, stats.Capture().SentBytes);
        Assert.True(stats.Capture().IsValid);
    }

    [Fact]
    public async Task FailedOrCancelledIoDoesNotCountRequestedBufferAsSuccessfulBytes()
    {
        var stats = new PcpTransportStatistics();
        var counter = stats.CreateCounter(1);
        using var session = counter.Establish();
        using var stream = new TransportObservationStream(new FailingStream(), counter);
        await Assert.ThrowsAsync<IOException>(() => stream.WriteAsync(new byte[123]).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream.ReadAsync(new byte[456]).AsTask());
        Assert.Equal(0, stats.Capture().SentBytes);
        Assert.Equal(0, stats.Capture().ReceivedBytes);
        Assert.False(stats.Capture().IsValid);
    }

    private sealed class FailingStream : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new IOException("Controlled write failure"));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromCanceled<int>(new CancellationToken(true));
    }
}
