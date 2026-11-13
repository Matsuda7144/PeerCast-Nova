using System.IO.Pipelines;
using System.Net;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed partial class PcpDownstreamRelayServerTests
{
    [Fact]
    public async Task HostWireCountsEstablishedRelaysWithoutCountingPingHandshakeOrInternalChild()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using var fanout = new PcpRelayFanout(new() { MaxChildren = 2 });
        var internalChild = fanout.AddChild(new HostRuntimeSink());
        using var admission = new WanAdmission(1);
        var transport = new TcpTransportOptions { MaxConcurrentConnections = 2 };
        await using var server = new PcpDownstreamRelayServer(new(IPAddress.Loopback, 0), fanout, ChannelId, LocalSessionId,
            transportOptions: transport, admission: admission,
            initialMetadata: new(name: "runtime", contentType: "FLV"), wanOptions: FixtureWan);
        var run = server.RunAsync(timeout.Token);
        await using var wire = new HostRuntimeWire(ChannelId, new ChannelSessionId(LocalSessionId));
        wire.Read = () =>
        {
            var current = server.ReadRuntimeCapacity();
            return PublishingAnnounceRuntimeSnapshot.Create(wire.Status(ChannelSessionState.Receiving, true, true),
                true, admission.CanReserve, current.CanAccept, fanout.CanAddChild, current.RelayCount, 0);
        };
        await wire.StartAsync();
        Assert.Equal(0U, PublishingHostRuntimeTests.Number(await wire.NextAsync(), "numr"));
        await using var pending = await new TcpTransportConnector().ConnectAsync(server.LocalEndPoint, timeout.Token);
        await WaitForAcceptedCountAsync(server, 1, timeout.Token);
        await using (var ping = await new TcpTransportConnector().ConnectAsync(server.LocalEndPoint, timeout.Token))
        {
            await WaitForAcceptedCountAsync(server, 2, timeout.Token);
            wire.Advance();
            var fullTcp = await wire.NextAsync();
            Assert.Equal(0U, PublishingHostRuntimeTests.Number(fullTcp, "numr"));
            Assert.Equal(0x11, PublishingHostRuntimeTests.Flags(fullTcp));
            await ping.WriteAsync(PingRequest(PcpHeloCodec.CreateHelo("ping", Guid.NewGuid())), timeout.Token);
            var pipe = PipeReader.Create(ping.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
            await using var reader = new PcpAtomPipeMessageReader(pipe);
            Assert.Equal(PcpKnownAtomNames.Oleh, (await reader.ReadNextAsync(timeout.Token)).Name);
            await pipe.CompleteAsync();
        }
        await WaitForCompletedCountAsync(server, 1, timeout.Token);
        await using (var peer = await ConnectPeerAsync(server.LocalEndPoint, transport, timeout.Token))
        {
            Assert.Equal(PcpCompatibilityOutcome.Accepted, peer.Handshake.Outcome);
            while (server.ReadRuntimeCapacity().RelayCount != 1) await Task.Delay(5, timeout.Token);
            wire.Advance();
            var relaying = await wire.NextAsync();
            if (PublishingHostRuntimeTests.Number(relaying, "numr") == 0)
            {
                // One HOST may already have been admitted before the runtime
                // changed. Verify the live count and request the latest snapshot.
                Assert.Equal(1, server.ReadRuntimeCapacity().RelayCount);
                wire.Pulse();
                relaying = await wire.NextAsync();
            }
            Assert.Equal(1U, PublishingHostRuntimeTests.Number(relaying, "numr"));
            Assert.Equal(0x11, PublishingHostRuntimeTests.Flags(relaying));
            Assert.Equal(1, admission.ReservationCount);
        }
        await WaitForCompletedCountAsync(server, 2, timeout.Token);
        while (!fanout.CanAddChild) await Task.Delay(5, timeout.Token);
        wire.Advance();
        var recovered = await wire.NextAsync();
        Assert.Equal(0U, PublishingHostRuntimeTests.Number(recovered, "numr"));
        Assert.Equal(0x13, PublishingHostRuntimeTests.Flags(recovered));
        Assert.Equal(0, admission.ReservationCount);
        Assert.True(await wire.StopAsync());
        await server.StopAsync();
        await run;
        await internalChild.DisposeAsync();
    }

    private sealed class HostRuntimeSink : IPcpRelayChildSink
    {
        public ValueTask SendAsync(PcpRelayPacket packet, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
