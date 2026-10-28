using System.Buffers;
using System.Net;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed partial class PcpDownstreamRelayServerTests
{
    [Fact]
    public async Task InitialInfoPrecedesRetainedHeadAndEveryLateJoinWithoutMediaAccounting()
    {
        var metadata = new ObsPublishChannelOptions { ChannelName = "配信 🎥", Genre = "音楽", Description = "概要", Comment = "コメント", ContactUrl = "https://example.invalid/", BitrateKbps = 1234 }.CreateMetadata();
        var transport = CreateTransportOptions();
        await using var fanout = new PcpRelayFanout(replayHeadForNewChildren: true);
        await using var server = new PcpDownstreamRelayServer(new IPEndPoint(IPAddress.Loopback, 0),
            fanout, ChannelId, LocalSessionId, transport, initialMetadata: metadata);
        using var timeout = new CancellationTokenSource(TestTimeout);
        var run = server.RunAsync(timeout.Token);
        byte[] head = [0x46, 0x4c, 0x56, 1];
        await fanout.ConsumeAsync(new(ChannelId, UpstreamSessionId, 0, 0x64616568, 0, 0, head, null), timeout.Token);
        var expected = PcpInitialChannelMetadata.Encode(ChannelId.Value, metadata);
        for (var index = 0; index < 2; index++)
        {
            await using var peer = await ConnectPeerAsync(server.LocalEndPoint, transport, timeout.Token);
            Assert.Equal(PcpCompatibilityOutcome.Accepted, peer.Handshake.Outcome);
            var info = await peer.ReadAtomAsync(timeout.Token);
            var encoded = new ArrayBufferWriter<byte>();
            PcpAtomTreeCodec.Write(encoded, info);
            Assert.Equal(expected, encoded.WrittenSpan.ToArray());
            Assert.Null(info.FindFirst(PcpKnownAtomNames.ChannelPacket));
            var packet = await peer.ReadAtomAsync(timeout.Token);
            Assert.Equal(head, packet.FindFirst(PcpKnownAtomNames.ChannelPacket)!.FindFirst(PcpKnownAtomNames.Data)!.Content.ToArray());
            await peer.SendQuitAsync(timeout.Token);
            await WaitForCompletedCountAsync(server, index + 1, timeout.Token);
        }
        await server.StopAsync(timeout.Token);
        var result = await run;
        Assert.Equal(2, result.Snapshot.DeliveredPacketCount);
        Assert.Equal(head.Length * 2, result.Snapshot.DeliveredByteCount);
        Assert.Equal(0, result.Snapshot.CleanupIncompleteCount);
        Assert.Equal(0, result.Snapshot.InternalFailureCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostUsesSameSnapshotLocallyAndDownstreamWhileFinishOrInterruptKeepsMediaEmpty(bool finish)
    {
        var options = new ObsPublishChannelOptions
        {
            ChannelName = "世代設定",
            Genre = "genre",
            Description = "description",
            Comment = "comment",
            ContactUrl = "https://example.invalid/",
            BitrateKbps = 512,
            Ingest = new() { Port = 0 },
        };
        await using var host = new ObsPublishChannelHost(options);
        host.Start();
        Assert.Same(host.InitialMetadata, host.Snapshot.Metadata);
        using var timeout = new CancellationTokenSource(TestTimeout);
        // This peer negotiates the host's generated ChannelId, rather than the fixture channel.
        var transport = CreateTransportOptions();
        await using var connection = await new PeerCast.Nova.Network.Transport.TcpTransportConnector(transport)
            .ConnectAsync(host.RelayEndPoint.Address.ToString(), host.RelayEndPoint.Port, timeout.Token);
        var pipe = System.IO.Pipelines.PipeReader.Create(connection.TransportStream, new System.IO.Pipelines.StreamPipeReaderOptions(leaveOpen: true));
        try
        {
            var handshake = await PcpRelayHandshakeClient.NegotiateAsync(connection, pipe, "127.0.0.1", host.RelayEndPoint.Port,
                host.ChannelId.Value, Guid.NewGuid(), 0, 1, new(32768, 65536, 16, "Nova/Test", PcpHeloCodec.LegacyServantVersion), timeout.Token);
            Assert.Equal(PcpCompatibilityOutcome.Accepted, handshake.Outcome);
            await using var reader = new PcpAtomPipeMessageReader(pipe);
            var info = await reader.ReadNextAsync(timeout.Token);
            var bytes = new ArrayBufferWriter<byte>();
            PcpAtomTreeCodec.Write(bytes, info);
            Assert.Equal(PcpInitialChannelMetadata.Encode(host.ChannelId.Value, host.InitialMetadata), bytes.WrittenSpan.ToArray());
            if (finish)
            {
                var ending = host.RequestLocalTerminationAsync(TestTimeout);
                await connection.DisposeAsync(); // A child may disconnect while Finish joins it.
                Assert.True((await ending.WaitAsync(timeout.Token)).CleanupCompleted);
            }
            else await host.DisposeAsync().AsTask().WaitAsync(timeout.Token);
            var result = await host.Completion.WaitAsync(timeout.Token);
            Assert.True(result.ShutdownCompleted);
            Assert.Equal(0, result.AcceptedPacketCount);
            Assert.Equal(0, result.AcceptedByteCount);
            Assert.Equal(0, result.HeadCount);
            Assert.Same(host.InitialMetadata, host.Snapshot.Metadata);
        }
        finally { await pipe.CompleteAsync(); }
    }
}
