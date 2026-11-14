using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed partial class PcpChannelSessionTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(4096)]
    public async Task Hosts_BcstReceivingThroughActualUpstreamPreservesMediaAndKeepsReportsOutOfReferrals(int chunkSize)
    {
        var (listener, connector) = CreateTransportPair(); await using var ownedListener = listener;
        using var timeout = new CancellationTokenSource(TestTimeout);
        var tracker = CreateTracker(); var consumer = new CollectingPacketConsumer();
        using var peers = new PcpPeerDirectory(ChannelGuid, SessionId.Value);
        await using var session = new PcpChannelSession(new TcpTransportConnectorAdapter(connector), tracker, consumer, CreateOptions(0), peers);
        var bcst = PcpReportedHostTests.Broadcast(ChannelGuid, PcpReportedHostTests.Host(ChannelGuid), PcpReportedHostTests.Host(ChannelGuid));
        var invalid = PcpReportedHostTests.Broadcast(Guid.NewGuid(), PcpReportedHostTests.Host(ChannelGuid));
        var server = RunAcceptedAttemptAsync(listener, 0, SessionId.Value,
            [CreatePacketAtom(100, [1, 2]), bcst, invalid, CreatePacketAtom(102, [3, 4]), CreateQuitAtom()], timeout.Token, httpChunkSize: chunkSize);
        var result = await session.RunAsync(listener.LocalEndPoint.Address.ToString(), listener.LocalEndPoint.Port, timeout.Token);
        await server;
        Assert.Equal(2, result.PacketCount); Assert.Equal(4, result.PayloadByteCount);
        Assert.Equal(new byte[] { 1, 2 }, consumer.Packets[0].Payload); Assert.Equal(new byte[] { 3, 4 }, consumer.Packets[1].Payload);
        Assert.Equal(2, peers.ObserveLocalTree().Reports.Count); Assert.Empty(peers.Snapshot());
        Assert.All(peers.ObserveLocalTree().Reports, r => { Assert.True(r.ViaBroadcast); Assert.Equal(PcpLocalHostSourceKind.UpstreamStream, r.Source.Kind); });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4096)]
    public async Task Hosts_StartupAndReceivingObservationPreserveMediaAndOwner(int chunkSize)
    {
        var (listener, connector) = CreateTransportPair();
        await using var ownedListener = listener;
        using var timeout = new CancellationTokenSource(TestTimeout);
        var tracker = CreateTracker();
        var consumer = new CollectingPacketConsumer();
        using var peers = new PcpPeerDirectory(ChannelGuid, SessionId.Value);
        await using var session = new PcpChannelSession(new TcpTransportConnectorAdapter(connector),
            tracker, consumer, CreateOptions(0), peers);
        var startup = PcpPeerDirectoryTests.Host() with { ChannelId = ChannelGuid };
        var receiving = PcpPeerDirectoryTests.Host(7150) with { ChannelId = ChannelGuid };
        var malformed = PcpAtom.Parent(PcpKnownAtomNames.Host,
            PcpAtom.Leaf(PcpAtomName.FromAscii("cid"), new byte[3]));
        var oversized = PcpAtom.Parent(PcpKnownAtomNames.Host,
            PcpAtom.Leaf(PcpAtomName.FromAscii("xtra"), new byte[1024]));
        var wrongChannel = PcpPeerDirectoryTests.Host(7151) with { ChannelId = Guid.NewGuid() };
        var server = RunAcceptedAttemptAsync(listener, 0, SessionId.Value,
            [CreatePacketAtom(100, [1, 2, 3]), malformed, oversized,
             PcpReferralPlanner.CreateHost(wrongChannel), PcpReferralPlanner.CreateHost(receiving),
             CreatePacketAtom(103, [4]), CreateQuitAtom()], timeout.Token,
             httpChunkSize: chunkSize, preOkayAtoms: [PcpReferralPlanner.CreateHost(startup)]);
        var result = await session.RunAsync(listener.LocalEndPoint.Address.ToString(),
            listener.LocalEndPoint.Port, timeout.Token);
        await server;
        Assert.Equal(2, result.PacketCount);
        Assert.Equal(4, result.PayloadByteCount);
        Assert.Equal(103, tracker.Snapshot.StreamPosition);
        Assert.Equal(SessionId, tracker.Snapshot.SessionId);
        Assert.Equal(new byte[] { 1, 2, 3 }, consumer.Packets[0].Payload);
        Assert.Equal(new byte[] { 4 }, consumer.Packets[1].Payload);
        Assert.Equal(1, consumer.Packets[1].Sequence);
        var observed = peers.Snapshot();
        Assert.Equal(2, observed.Length);
        Assert.Equal(PcpPeerSourceKind.UpstreamHandshake, observed.Single(e => e.Host.PeerSessionId == startup.PeerSessionId).Source.Kind);
        Assert.Equal(PcpPeerSourceKind.UpstreamStream, observed.Single(e => e.Host.PeerSessionId == receiving.PeerSessionId).Source.Kind);
    }
}
