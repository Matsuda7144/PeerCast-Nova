using System.Net;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpExternalReachabilityProbeTests
{
    [Fact]
    public async Task ProbeCoreAsync_ParsesPublicAddressAndOpenPortFromOleh()
    {
        var connector = new ProbeConnector(IPAddress.Parse("198.41.0.5"), "198.41.0.4", 17144);
        var result = await PcpExternalReachabilityProbe.ProbeCoreAsync(
            PcpAnnounceEndpoint.Parse("pcp://198.41.0.5:7144"),
            17144,
            Guid.NewGuid(),
            PcpAnnounceOwner.NormalizeOrCreateBroadcastId(null),
            connector,
            (_, _) => throw new InvalidOperationException("Numeric endpoint must not resolve DNS."),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("198.41.0.4", result.ExternalAddress);
        Assert.True(result.PortReachable);
        Assert.Null(result.FailureReason);

        var handshake = await connector.Connection.NextWriteAsync();
        var helo = PcpHeloCodec.ParseHelo(AnnounceTestData.Decode(handshake[PcpPreamble.Size..]));
        Assert.Equal((ushort)17144, helo.PingPort);
    }

    [Fact]
    public async Task ProbeCoreAsync_ReportsUnreachableWhenOlehPortIsZero()
    {
        var connector = new ProbeConnector(IPAddress.Parse("198.41.0.5"), "198.41.0.4", 0);
        var result = await PcpExternalReachabilityProbe.ProbeCoreAsync(
            PcpAnnounceEndpoint.Parse("pcp://tracker.example:7144"),
            17144,
            Guid.NewGuid(),
            PcpAnnounceOwner.NormalizeOrCreateBroadcastId(null),
            connector,
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("198.41.0.5") }),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("198.41.0.4", result.ExternalAddress);
        Assert.False(result.PortReachable);
    }

    [Fact]
    public async Task ProbeCoreAsync_IpOnlyProbeLeavesPortReachableNullAndFiltersPrivateAddress()
    {
        var publicConnector = new ProbeConnector(IPAddress.Parse("198.41.0.5"), "198.41.0.4", 0);
        var ipOnly = await PcpExternalReachabilityProbe.ProbeCoreAsync(
            PcpAnnounceEndpoint.Parse("pcp://198.41.0.5:7144"),
            0,
            Guid.NewGuid(),
            PcpAnnounceOwner.NormalizeOrCreateBroadcastId(null),
            publicConnector,
            (_, _) => throw new InvalidOperationException(),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.True(ipOnly.Succeeded);
        Assert.Equal("198.41.0.4", ipOnly.ExternalAddress);
        Assert.Null(ipOnly.PortReachable);

        var privateConnector = new ProbeConnector(IPAddress.Parse("198.41.0.5"), "192.168.1.50", 0);
        var filtered = await PcpExternalReachabilityProbe.ProbeCoreAsync(
            PcpAnnounceEndpoint.Parse("pcp://198.41.0.5:7144"),
            0,
            Guid.NewGuid(),
            PcpAnnounceOwner.NormalizeOrCreateBroadcastId(null),
            privateConnector,
            (_, _) => throw new InvalidOperationException(),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.False(filtered.Succeeded);
        Assert.Null(filtered.ExternalAddress);
        Assert.Null(filtered.PortReachable);
    }

    [Fact]
    public async Task ProbeCoreAsync_ReturnsFailureReasonWhenTrackerSendsQuitOrFailsToConnect()
    {
        var quitConnector = new ProbeConnector(IPAddress.Parse("198.41.0.5"), null, null, sendQuit: true);
        var quitResult = await PcpExternalReachabilityProbe.ProbeCoreAsync(
            PcpAnnounceEndpoint.Parse("pcp://198.41.0.5:7144"),
            17144,
            Guid.NewGuid(),
            PcpAnnounceOwner.NormalizeOrCreateBroadcastId(null),
            quitConnector,
            (_, _) => throw new InvalidOperationException(),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.False(quitResult.Succeeded);
        Assert.NotNull(quitResult.FailureReason);

        var failingConnector = new FailingConnector();
        var connectError = await PcpExternalReachabilityProbe.ProbeCoreAsync(
            PcpAnnounceEndpoint.Parse("pcp://198.41.0.5:7144"),
            17144,
            Guid.NewGuid(),
            PcpAnnounceOwner.NormalizeOrCreateBroadcastId(null),
            failingConnector,
            (_, _) => throw new InvalidOperationException(),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.False(connectError.Succeeded);
        Assert.NotNull(connectError.FailureReason);
    }

    private sealed class ProbeConnector : IPcpAnnounceConnector
    {
        private readonly IPAddress _expectedAddress;
        internal AnnounceFakeConnection Connection { get; } = new();

        public ProbeConnector(IPAddress expectedAddress, string? remoteIp, int? remotePort, bool sendQuit = false)
        {
            _expectedAddress = expectedAddress;
            if (sendQuit)
            {
                Connection.ReplaceHandshake(PcpAnnounceCodec.CreateQuit());
                return;
            }

            var oleh = PcpHeloCodec.CreateOleh("fake tracker", Guid.NewGuid());
            var fields = oleh.Children.ToList();
            if (remoteIp is not null)
            {
                fields.Add(PcpAtom.Leaf(PcpKnownAtomNames.HeloRemoteIp, PcpWireValueCodec.EncodeLegacyIpv4(IPAddress.Parse(remoteIp))));
            }

            if (remotePort is { } port)
            {
                fields.Add(PcpAtom.Leaf(PcpKnownAtomNames.HeloPort, PcpWireValueCodec.EncodeUInt16((ushort)port)));
            }

            Connection.ReplaceHandshake(PcpAtom.Parent(oleh.Name, fields));
        }

        public Task<IPcpAnnounceConnection> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
        {
            Assert.Equal(_expectedAddress, endpoint.Address);
            return Task.FromResult<IPcpAnnounceConnection>(Connection);
        }
    }

    private sealed class FailingConnector : IPcpAnnounceConnector
    {
        public Task<IPcpAnnounceConnection> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken) =>
            Task.FromException<IPcpAnnounceConnection>(new IOException("Connection refused"));
    }
}
