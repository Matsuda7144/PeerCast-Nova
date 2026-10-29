using System.Net;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Protocol.Pcp;
using static PeerCast.Nova.Network.Tests.AnnounceTestData;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpAnnounceTransportTests
{
    [Fact]
    public async Task RealLoopbackTrackerSeesDirectPcpMetadataUpdatesAndOrderedStop()
    {
        await using var tracker = new AnnounceLoopbackTracker();
        await using var owner = new PcpAnnounceOwner();
        var worker = Start(owner, endpoint: PcpAnnounceEndpoint.Parse($"pcp://127.0.0.1:{tracker.Endpoint.Port}"),
            access: PcpAnnounceAccess.ForLoopbackFixture(tracker.Endpoint));
        var helo = await tracker.AcceptHeloAsync();
        var remote = Guid.NewGuid();
        var oleh = PcpHeloCodec.CreateOleh("fixture", remote);
        await tracker.WriteAsync(PcpAtom.Parent(oleh.Name, oleh.Children.Concat(new[]
        {
            PcpAtom.Leaf(PcpKnownAtomNames.HeloPort, PcpWireValueCodec.EncodeUInt16(54321)),
            PcpAtom.Leaf(PcpKnownAtomNames.HeloRemoteIp, PcpWireValueCodec.EncodeLegacyIpv4(IPAddress.Parse("203.0.113.42"))),
        }).ToArray()));
        var initial = await tracker.ReadAsync();
        Assert.Equal("Motion + tone", Name(initial));
        Assert.Equal(remote, worker.TrackerObservation!.SessionId);
        Assert.Equal(IPAddress.Parse("203.0.113.42"), worker.TrackerObservation.RemoteAddress);
        Assert.Equal((ushort)54321, worker.TrackerObservation.RemotePort);
        Assert.Equal(IPAddress.Parse("192.0.2.42"), PcpWireValueCodec.DecodeLegacyIpv4(Field(Field(initial, "host"), "ip").Content.Span));
        Assert.Equal((ushort)7144, PcpWireValueCodec.DecodeUInt16(Field(Field(initial, "host"), "port").Content.Span));
        Assert.Equal(worker.Identity.SenderSessionId, PcpHeloCodec.ParseHelo(helo).SessionId);
        Assert.True(worker.Update(worker.Identity, Document("変更後")));
        Assert.False(worker.Update(worker.Identity, Document("endpoint mutation", 9999)));
        Assert.Equal("変更後", Name(await tracker.ReadAsync()));
        var stop = worker.StopAsync(PcpAnnounceStopReason.ObsNormalEos);
        var notice = await tracker.ReadAsync();
        Assert.Equal(0, Field(Field(notice, "host"), "flg1").Content.Span[0] & 0x10);
        Assert.Equal(PcpKnownAtomNames.Quit, (await tracker.ReadAsync()).Name);
        var result = await stop.WaitAsync(Deadline);
        Assert.True(result.StopNoticeSent && result.QuitSent && result.CleanupProven);
    }

    [Theory]
    [InlineData("missing", 4)]
    [InlineData("banned", 5)]
    [InlineData("quit", 6)]
    [InlineData("oversize", 4)]
    [InlineData("nested", 4)]
    public async Task RealTrackerFailuresDoNotAnnounceOrReconnect(string kind, int expectedFailure)
    {
        await using var tracker = new AnnounceLoopbackTracker();
        await using var owner = new PcpAnnounceOwner();
        var worker = Start(owner, endpoint: PcpAnnounceEndpoint.Parse($"pcp://127.0.0.1:{tracker.Endpoint.Port}"), access: PcpAnnounceAccess.ForLoopbackFixture(tracker.Endpoint));
        await tracker.AcceptHeloAsync();
        if (kind == "oversize") await tracker.RawAsync(new byte[] { 0x6f, 0x6c, 0x65, 0x68, 1, 0, 2, 0 });
        else if (kind == "nested")
        {
            // Nine nested parents, beyond the reader's depth eight. Raw headers avoid the outgoing writer limit.
            var bytes = Enumerable.Range(0, 10).SelectMany(_ => new byte[] { 0x78, 0, 0, 0, 1, 0, 0, 128 }).ToArray();
            await tracker.RawAsync(bytes.Concat(new byte[8]).ToArray());
        }
        else
        {
            var reply = kind switch
            {
                "missing" => PcpAtom.Parent(PcpKnownAtomNames.Oleh),
                "quit" => PcpAnnounceCodec.CreateQuit(),
                _ => PcpAtom.Parent(PcpKnownAtomNames.Oleh,
                    PcpAtom.Leaf(PcpKnownAtomNames.HeloSessionId, PcpWireValueCodec.EncodeGuid(Guid.NewGuid())),
                    PcpAtom.Leaf(PcpAnnounceCodec.Disable, PcpWireValueCodec.EncodeUInt32(1))),
            };
            await tracker.WriteAsync(reply);
        }
        var result = await worker.Completion.WaitAsync(Deadline);
        Assert.Equal((PcpAnnounceFailure)expectedFailure, result.Failure);
        Assert.False(result.AnnounceSent);
        Assert.True(result.CleanupProven);
    }

    [Fact]
    public async Task SilentTrackerHasFiniteHandshakeDeadlineAndActualTransportCleanup()
    {
        await using var tracker = new AnnounceLoopbackTracker();
        await using var owner = new PcpAnnounceOwner(options: new() { HandshakeTimeout = TimeSpan.FromSeconds(1) });
        var worker = Start(owner, endpoint: PcpAnnounceEndpoint.Parse($"pcp://127.0.0.1:{tracker.Endpoint.Port}"), access: PcpAnnounceAccess.ForLoopbackFixture(tracker.Endpoint));
        await tracker.AcceptHeloAsync();
        var result = await worker.Completion.WaitAsync(Deadline);
        Assert.Equal(PcpAnnounceFailure.Timeout, result.Failure);
        Assert.True(result.CleanupProven);
        Assert.False(result.AnnounceSent);
    }

    [Fact]
    public async Task ConnectionRefusedIsReportedWithoutRetry()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        listener.Stop();
        await using var owner = new PcpAnnounceOwner();
        var worker = Start(owner, endpoint: PcpAnnounceEndpoint.Parse($"pcp://127.0.0.1:{endpoint.Port}"), access: PcpAnnounceAccess.ForLoopbackFixture(endpoint));
        var result = await worker.Completion.WaitAsync(Deadline);
        Assert.Equal(PcpAnnounceFailure.Connection, result.Failure);
        Assert.True(result.CleanupProven);
    }
}
