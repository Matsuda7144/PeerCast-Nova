using System.Net;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PublishingAnnounceCapabilityTests
{
    [Theory]
    [InlineData(null, (int)PublishingTrackerReportKind.PortMissing)]
    [InlineData(0, (int)PublishingTrackerReportKind.PortClosed)]
    [InlineData(7144, (int)PublishingTrackerReportKind.PortMismatch)]
    [InlineData(17144, (int)PublishingTrackerReportKind.TrackerReportedOpen)]
    public void ExternalPortReportIsConnectionScoped(int? port, int expected)
    {
        var active = true;
        var sid = Guid.NewGuid();
        var capability = new PublishingAnnounceCapability(new(1, Guid.NewGuid(), sid, sid, Guid.NewGuid()),
            Guid.NewGuid(), new(IPAddress.Parse("192.168.1.20"), 7144),
            new(IPAddress.Parse("198.41.0.4"), 17144), new(IPAddress.Parse("198.41.0.5"), 7144), () => active);
        var observation = new PcpOlehInfo(Guid.NewGuid(), null, null,
            port is null ? null : checked((ushort)port), capability.AdvertisedEndpoint.Address);
        var report = capability.Evaluate(observation, DateTimeOffset.UtcNow);
        Assert.Equal((PublishingTrackerReportKind)expected, report.Kind);
        Assert.False(report.IndependentReachabilityVerified);
        Assert.Equal(expected == (int)PublishingTrackerReportKind.TrackerReportedOpen, report.CanAnnounce(capability));
        Assert.False(report.CanAnnounce(capability with { ConnectionId = Guid.NewGuid() }));
        active = false;
        Assert.False(report.CanAnnounce(capability));
    }

    [Fact]
    public void HeloChoosesExternalPingOrOpenPortExclusively()
    {
        var sid = Guid.NewGuid();
        var bcid = Guid.NewGuid();
        var ping = PcpHeloCodec.ParseHelo(PcpAnnounceCodec.CreateHelo("test", sid, bcid, 17144, requestPing: true));
        Assert.Equal((ushort)17144, ping.PingPort);
        Assert.Null(ping.OpenPort);
        var open = PcpHeloCodec.ParseHelo(PcpAnnounceCodec.CreateHelo("test", sid, bcid, 17144));
        Assert.Equal((ushort)17144, open.OpenPort);
        Assert.Null(open.PingPort);
    }

    [Fact]
    public void AmbiguousTrackerPortIsRejected()
    {
        var oleh = PcpHeloCodec.CreateOleh("test", Guid.NewGuid());
        var field = PcpAtom.Leaf(PcpKnownAtomNames.HeloPort, PcpWireValueCodec.EncodeUInt16(17144));
        Assert.Throws<InvalidDataException>(() => PcpAnnounceCodec.ParseOleh(
            PcpAtom.Parent(oleh.Name, oleh.Children.Concat(new[] { field, field }).ToArray())));
    }
}
