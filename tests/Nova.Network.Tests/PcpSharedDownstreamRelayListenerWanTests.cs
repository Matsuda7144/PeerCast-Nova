using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed partial class PcpSharedDownstreamRelayListenerTests
{
    private static PcpWanIngressOptions Wan => new() { SourceAllowed = a => a is not null && IPAddress.IsLoopback(a) };

    [Theory]
    [InlineData("http")]
    [InlineData("channel")]
    [InlineData("ping-helo")]
    [InlineData("ping-oversize")]
    public async Task WanSharedRejectsMalformedIngressAndKeepsOneListenerHealthy(string kind)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        var routes = new PcpDownstreamRelayRouteRegistry();
        var handler = new RecordingRoutedHandler(1);
        await using var listener = new PcpSharedDownstreamRelayListener(new(IPAddress.Loopback, 0), routes, handler, wanOptions: Wan);
        var run = listener.RunAsync(timeout.Token);
        await using var bad = await new TcpTransportConnector().ConnectAsync(listener.LocalEndPoint, timeout.Token);
        var bytes = kind switch
        {
            "http" => "POST / HTTP/1.0\r\n\r\n"u8.ToArray(),
            "channel" => "GET /channel/invalid HTTP/1.0\r\nx-peercast-pcp:1\r\n\r\n"u8.ToArray(),
            _ => BadPing(kind),
        };
        await bad.WriteAsync(bytes, timeout.Token);
        // A bounded HTTP rejection or immediate close is acceptable for malformed
        // control input; neither may reach an Owner or kill the shared accept loop.
        var buffer = new byte[512];
        while (await bad.ReadAsync(buffer, timeout.Token) != 0) { }
        await PcpSmartRelayRecoveryTests.Until(() => listener.Snapshot.CompletedConnectionCount == 1);
        Assert.Equal(0, listener.WanIngress!.PendingHandshakes);
        Assert.Empty(handler.Calls);
        Assert.False(run.IsCompleted);
        await using var fanout = new PcpRelayFanout();
        await using var route = routes.Register(FirstChannelId, fanout);
        await using var valid = await new TcpTransportConnector().ConnectAsync(listener.LocalEndPoint, timeout.Token);
        await valid.WriteAsync(new PcpRelayRequest { ChannelId = FirstChannelId.Value, Host = "127.0.0.1" }.Encode(), timeout.Token);
        await handler.WaitForExpectedCallsAsync(timeout.Token);
        await listener.StopAsync(timeout.Token);
        Assert.Equal(0, (await run).Snapshot.CleanupFailureCount);
    }

    [Fact]
    public async Task WanSharedAcceptBudgetsTimeoutsAndStopBoundSlowUnidentifiedPeers()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        using var clock = new AnnounceManualTimeProvider();
        await using var listener = new PcpSharedDownstreamRelayListener(new(IPAddress.Loopback, 0), new(),
            new RecordingRoutedHandler(1), wanOptions: Wan with { Clock = clock });
        var run = listener.RunAsync(timeout.Token);
        var endpoint = listener.LocalEndPoint;
        var connector = new TcpTransportConnector();
        await using var first = await connector.ConnectAsync(endpoint, timeout.Token);
        await using var second = await connector.ConnectAsync(endpoint, timeout.Token);
        await first.WriteAsync("GET "u8.ToArray(), timeout.Token);
        await second.WriteAsync("p"u8.ToArray(), timeout.Token);
        await PcpSmartRelayRecoveryTests.Until(() => listener.WanIngress!.PendingHandshakes == 2 && clock.ActiveTimerCount >= 3);
        await using var denied = await connector.ConnectAsync(endpoint, timeout.Token);
        Assert.Equal(0, await denied.ReadAsync(new byte[1], timeout.Token));
        Assert.Equal(2, listener.Snapshot.AcceptedConnectionCount);
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(0, await second.ReadAsync(new byte[1], timeout.Token));
        await PcpSmartRelayRecoveryTests.Until(() => listener.WanIngress!.PendingHandshakes == 1);
        clock.Advance(TimeSpan.FromSeconds(7)); // HTTP budget began at accept, not after protocol identification.
        Assert.Equal(0, await first.ReadAsync(new byte[1], timeout.Token));
        await PcpSmartRelayRecoveryTests.Until(() => listener.WanIngress!.PendingHandshakes == 0);
        Assert.True(listener.WanIngress!.RejectedConnections > 0);
        await using var partial = await connector.ConnectAsync(endpoint, timeout.Token);
        await partial.WriteAsync("GET "u8.ToArray(), timeout.Token);
        await PcpSmartRelayRecoveryTests.Until(() => listener.WanIngress!.PendingHandshakes == 1);
        listener.RequestStop();
        await listener.StopAsync(timeout.Token);
        var result = await run;
        Assert.Equal(0, result.Snapshot.ActiveConnectionCount);
        Assert.Equal(0, result.Snapshot.CleanupFailureCount);
        Assert.Equal(0, listener.WanIngress.PendingHandshakes);
        await Assert.ThrowsAsync<SocketException>(() => connector.ConnectAsync(endpoint, timeout.Token).AsTask());
    }

    [Fact]
    public async Task WanSharedPingRetainsStandardPreambleAndDoesNotRouteOrReserveMedia()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        var sid = Guid.NewGuid();
        var handler = new RecordingRoutedHandler(1);
        await using var listener = new PcpSharedDownstreamRelayListener(new(IPAddress.Loopback, 0), new(), handler,
            wanOptions: Wan, pingSessionId: sid);
        var run = listener.RunAsync(timeout.Token);
        await using var client = await new TcpTransportConnector().ConnectAsync(listener.LocalEndPoint, timeout.Token);
        var bytes = new ArrayBufferWriter<byte>();
        var preamble = new byte[PcpPreamble.Size]; PcpPreamble.Write(preamble);
        PcpAtomTreeCodec.Write(bytes, PcpHeloCodec.CreateHelo("PeerCastStation/Test", Guid.NewGuid()));
        foreach (var value in preamble.Concat(bytes.WrittenSpan.ToArray())) await client.WriteAsync(new byte[] { value }, timeout.Token);
        var pipe = PipeReader.Create(client.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
        try
        {
            await using var atoms = new PcpAtomPipeMessageReader(pipe);
            Assert.Equal(sid, PcpHeloCodec.ParseOleh(await atoms.ReadNextAsync(timeout.Token)).SessionId);
            await Assert.ThrowsAsync<PcpProtocolException>(() => atoms.ReadNextAsync(timeout.Token).AsTask());
        }
        finally { await pipe.CompleteAsync(); }
        await listener.StopAsync(timeout.Token);
        Assert.Empty(handler.Calls);
        Assert.Equal(1, listener.WanIngress!.PingResponses);
        Assert.Equal(0, listener.WanIngress.ActivePings);
        Assert.Equal(0, (await run).Snapshot.RoutedConnectionCount);
    }

    private static byte[] BadPing(string kind)
    {
        var prefix = new byte[PcpPreamble.Size]; PcpPreamble.Write(prefix);
        var bytes = new ArrayBufferWriter<byte>();
        PcpAtomTreeCodec.Write(bytes, PcpAtom.Parent(PcpKnownAtomNames.Helo));
        if (kind == "ping-oversize") return prefix.Concat(new byte[] { (byte)'h', (byte)'e', (byte)'l', (byte)'o', 1, 16, 0, 0 }).ToArray();
        return prefix.Concat(bytes.WrittenSpan.ToArray()).ToArray();
    }
}
