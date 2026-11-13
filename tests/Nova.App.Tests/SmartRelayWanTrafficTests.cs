using PeerCast.Nova.Network.Pcp;
using static PeerCast.Nova.App.Tests.PublicChannelTestFixture;

namespace PeerCast.Nova.App.Tests;

public sealed partial class SmartRelayWanTests
{
    [Fact]
    public async Task LocalAndSharedWritesMatchPeerBytesAcrossChildrenAndChannelsAndSurviveClose()
    {
        await using var test = await Fixture.Create(ipv4Kbps: 2000);
        await using var one = await test.Base.ViewingAsync(test.Token, bitrateKbps: 500);
        await using var two = await test.Base.ViewingAsync(test.Token, bitrateKbps: 700);
        await one.Owner.PublicReady;
        await two.Owner.PublicReady;
        var policy = test.Base.State.NetworkPolicies.Relays;
        Assert.False(policy.Traffic.Snapshot.HasObservation); // Upstream reception is excluded.
        Assert.Contains("下流PCP書込完了 Unknown", one.Owner.CapacityStatusText);
        await using var local = await WirePeer.Connect(one.Owner.LocalEndPoint.Port, one.Owner.ChannelId, test.Token);
        await using var shared = await WirePeer.Connect(test.Port, one.Owner.ChannelId, test.Token);
        await using var other = await WirePeer.Connect(test.Port, two.Owner.ChannelId, test.Token);
        Assert.All(new[] { local, shared, other }, p => Assert.Equal(200, p.Handshake.HttpStatusCode));
        long Received() => local.ReceivedBytes + shared.ReceivedBytes + other.ReceivedBytes;
        await UntilAsync(() => policy.Traffic.Snapshot.CompletedWriteBytes == Received(), test.Token);
        var before = Received();
        Assert.True(before > 0); // HTTP response and handshake are included.
        await one.ProducerFanout.ConsumeAsync(Packet(one.Owner, 0x64616568, 0, [1, 2, 3]), test.Token);
        await one.ProducerFanout.ConsumeAsync(Packet(one.Owner, 0x61746164, 100, [4, 5, 6, 7]), test.Token);
        await two.ProducerFanout.ConsumeAsync(Packet(two.Owner, 0x64616568, 0, [8]), test.Token);
        await two.ProducerFanout.ConsumeAsync(Packet(two.Owner, 0x61746164, 200, [9, 10]), test.Token);
        foreach (var peer in new[] { local, shared })
        {
            Assert.Equal(new byte[] { 1, 2, 3 }, await peer.Payload(test.Token));
            Assert.Equal(new byte[] { 4, 5, 6, 7 }, await peer.Payload(test.Token));
        }
        Assert.Equal(new byte[] { 8 }, await other.Payload(test.Token));
        Assert.Equal(new byte[] { 9, 10 }, await other.Payload(test.Token));
        var expected = before + 6 * PcpChannelPacketWireWriter.PrefixLength + 2 * (3 + 4) + 1 + 2;
        await UntilAsync(() => policy.Traffic.Snapshot.CompletedWriteBytes == expected, test.Token);
        Assert.Equal(expected, Received());
        Assert.Equal(local.ReceivedBytes + shared.ReceivedBytes, one.Owner.Traffic!.Snapshot.CompletedWriteBytes);
        Assert.Equal(other.ReceivedBytes, two.Owner.Traffic!.Snapshot.CompletedWriteBytes);
        Assert.True(policy.Traffic.Snapshot.CumulativeComplete);
        Assert.Equal(1700, policy.Snapshot.ReservedIpv4Kbps); // Actual throughput never discounts reservations.
        await local.Quit(test.Token);
        await shared.Quit(test.Token);
        await other.Quit(test.Token);
        await UntilAsync(() => policy.ReservedCount == 0, test.Token);
        Assert.True(policy.Traffic.Snapshot.CompletedWriteBytes >= expected);
        Assert.True(one.Owner.Traffic.Snapshot.CompletedWriteBytes > 0);
        Assert.Equal(0, policy.Snapshot.ReservedIpv4Kbps);
    }
}
