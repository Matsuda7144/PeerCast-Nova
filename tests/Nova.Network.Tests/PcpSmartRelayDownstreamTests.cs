using System.Net;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.Network.Tests;

public sealed partial class PcpDownstreamRelayServerTests
{
    [Fact]
    public async Task SmartRecovery_PartialMediaWriteClosesWithoutAppendingQuitInsidePayload()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        var transport = CreateTransportOptions();
        await using var fanout = new PcpRelayFanout();
        PartialRecoveryWriter? writer = null;
        await using var server = new PcpDownstreamRelayServer(new(IPAddress.Loopback, 0), fanout,
            ChannelId, LocalSessionId, transport)
        { PacketWriterFactory = connection => writer = new(connection) };
        var run = server.RunAsync(timeout.Token);
        await using var peer = await ConnectPeerAsync(server.LocalEndPoint, transport, timeout.Token);
        await fanout.ConsumeAsync(CreatePacket(new byte[32]), timeout.Token);
        await writer!.PayloadEntered.Task.WaitAsync(timeout.Token);
        await fanout.SuspendForRecoveryAsync(TimeSpan.FromSeconds(2), TimeProvider.System, timeout.Token);
        await server.WaitForRecoveryCleanupAsync(TimeSpan.FromSeconds(2), TimeProvider.System, timeout.Token);
        var bytes = await peer.ReadWireToEndAsync(timeout.Token);
        Assert.Equal(PcpChannelPacketWireWriter.PrefixLength, bytes.Length);
        Assert.False(fanout.HasRetainedHead);
        await server.StopAsync(timeout.Token); await run;
    }

    private sealed class PartialRecoveryWriter(TcpTransportConnection connection) : IPcpDownstreamPacketWriter
    {
        private int _writes;
        internal TaskCompletionSource PayloadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token)
        {
            if (Interlocked.Increment(ref _writes) == 1) { await connection.WriteAsync(buffer, token); return; }
            PayloadEntered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
    }
}
