using System.Net;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PublishingMultiYpAnnounceTests
{
    [Fact]
    public async Task MultiYpAnnounce_SendsToAllTargetsWithSharedBroadcastIdAndUpdatesMetadata()
    {
        var tracker1 = IPAddress.Parse("198.41.0.5");
        var tracker2 = IPAddress.Parse("198.41.0.6");
        var conn1 = CreateOpenConnection();
        var conn2 = CreateOpenConnection();
        var connector = new RoutedConnector(endpoint => endpoint.Address.Equals(tracker1) ? conn1 : conn2);

        await using var owner = new PublishingAnnounceOwner(
            connector,
            (_, _) => throw new InvalidOperationException("Numeric endpoint must not resolve DNS."),
            reconnectBaseDelay: TimeSpan.FromMilliseconds(20));

        var options = new PublishingAnnounceOptions(true, "Primary YP", "pcp://198.41.0.5:7144")
        {
            AdditionalTargets = [new PublishingAnnounceTarget("Secondary YP", "pcp://198.41.0.6:7144")],
        };

        var advertised = new IPEndPoint(IPAddress.Parse("198.41.0.4"), 17144);
        var channelId = new ChannelId(Guid.NewGuid());
        var sessionId = new ChannelSessionId(Guid.NewGuid());
        var session = owner.Start(
            options,
            channelId,
            sessionId,
            new ChannelMetadataSnapshot("Multi YP Title", "Test", "Desc", "https://example.com/", "FLV", 512),
            new IPEndPoint(IPAddress.Parse("192.168.1.2"), 7144),
            advertised,
            () => true,
            () => new(channelId, sessionId, 1, true, false, 0, 0, true));

        var heloAtom1 = AnnounceTestData.Decode((await conn1.NextWriteAsync())[PcpPreamble.Size..]);
        var heloAtom2 = AnnounceTestData.Decode((await conn2.NextWriteAsync())[PcpPreamble.Size..]);
        Assert.NotEqual(Guid.Empty, owner.BroadcastId);
        var expectedBcid = PcpWireValueCodec.EncodeGuid(owner.BroadcastId);
        Assert.Equal(expectedBcid, AnnounceTestData.Field(heloAtom1, "bcid").Content.ToArray());
        Assert.Equal(expectedBcid, AnnounceTestData.Field(heloAtom2, "bcid").Content.ToArray());

        var bcst1 = AnnounceTestData.Decode(await conn1.NextWriteAsync());
        var bcst2 = AnnounceTestData.Decode(await conn2.NextWriteAsync());
        Assert.Equal("Multi YP Title", AnnounceTestData.Name(bcst1));
        Assert.Equal("Multi YP Title", AnnounceTestData.Name(bcst2));

        await WaitUntilAsync(() => session.Status.AnnouncedTargetCount == 2);
        Assert.Equal(2, session.Status.TotalTargetCount);
        Assert.Equal("AnnounceSent", session.Status.State);
        Assert.True(session.Status.TrackerReportedOpen);
        Assert.True(session.Status.AnnounceSent);

        Assert.True(session.UpdateMetadata(
            new ChannelMetadataSnapshot("Updated Multi Title", "Test", "Desc", "https://example.com/", "FLV", 512),
            advertised));

        var updated1 = AnnounceTestData.Decode(await conn1.NextWriteAsync());
        var updated2 = AnnounceTestData.Decode(await conn2.NextWriteAsync());
        Assert.Equal("Updated Multi Title", AnnounceTestData.Name(updated1));
        Assert.Equal("Updated Multi Title", AnnounceTestData.Name(updated2));

        Assert.True(await session.StopAsync(PcpAnnounceStopReason.ObsNormalEos));
        Assert.True(session.CleanupProven);
        Assert.True(session.Status.StopNoticeSent);
        Assert.True(conn1.Closed);
        Assert.True(conn2.Closed);
    }

    [Fact]
    public async Task MultiYpAnnounce_FailureOnOneTargetDoesNotBlockOtherTarget()
    {
        var tracker1 = IPAddress.Parse("198.41.0.5");
        var tracker2 = IPAddress.Parse("198.41.0.6");
        var conn1 = new AnnounceFakeConnection();
        conn1.ReplaceHandshake(PcpAnnounceCodec.CreateQuit());
        var conn2 = CreateOpenConnection();
        var connector = new RoutedConnector(endpoint => endpoint.Address.Equals(tracker1) ? conn1 : conn2);

        await using var owner = new PublishingAnnounceOwner(
            connector,
            (_, _) => throw new InvalidOperationException("Numeric endpoint must not resolve DNS."),
            reconnectBaseDelay: TimeSpan.FromMilliseconds(20));

        var options = new PublishingAnnounceOptions(true, "Primary YP", "pcp://198.41.0.5:7144")
        {
            AdditionalTargets = [new PublishingAnnounceTarget("Secondary YP", "pcp://198.41.0.6:7144")],
        };

        var advertised = new IPEndPoint(IPAddress.Parse("198.41.0.4"), 17144);
        var channelId = new ChannelId(Guid.NewGuid());
        var sessionId = new ChannelSessionId(Guid.NewGuid());
        var session = owner.Start(
            options,
            channelId,
            sessionId,
            new ChannelMetadataSnapshot("Resilient Stream", "", "", "https://example.com/", "FLV", 256),
            new IPEndPoint(IPAddress.Parse("192.168.1.2"), 7144),
            advertised,
            () => true,
            () => new(channelId, sessionId, 1, true, false, 0, 0, true));

        _ = await conn2.NextWriteAsync(); // HELO
        var bcst2 = AnnounceTestData.Decode(await conn2.NextWriteAsync());
        Assert.Equal("Resilient Stream", AnnounceTestData.Name(bcst2));

        await WaitUntilAsync(() => session.Status.AnnouncedTargetCount == 1);
        Assert.Equal(2, session.Status.TotalTargetCount);
        Assert.True(session.Status.AnnounceSent);

        Assert.True(await session.StopAsync(PcpAnnounceStopReason.Finish));
        Assert.True(session.CleanupProven);
    }

    private static AnnounceFakeConnection CreateOpenConnection()
    {
        var conn = new AnnounceFakeConnection();
        var oleh = PcpHeloCodec.CreateOleh("fake tracker", Guid.NewGuid());
        var fields = oleh.Children.Concat(
        [
            PcpAtom.Leaf(PcpKnownAtomNames.HeloRemoteIp, PcpWireValueCodec.EncodeLegacyIpv4(IPAddress.Parse("198.41.0.4"))),
            PcpAtom.Leaf(PcpKnownAtomNames.HeloPort, PcpWireValueCodec.EncodeUInt16(17144)),
        ]).ToList();
        conn.ReplaceHandshake(PcpAtom.Parent(oleh.Name, fields));
        return conn;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(AnnounceTestData.Deadline);
        while (!condition())
        {
            await Task.Delay(10, cts.Token);
        }
    }

    private sealed class RoutedConnector(Func<IPEndPoint, IPcpAnnounceConnection> factory) : IPcpAnnounceConnector
    {
        public Task<IPcpAnnounceConnection> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken) =>
            Task.FromResult(factory(endpoint));
    }
}
