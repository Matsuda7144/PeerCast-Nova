using System.Net;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PublishingAnnounceIntegrationTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task DisabledOrNotReadyNeverResolvesOrConnects(bool enabled, bool ready)
    {
        var connector = new Connector();
        var dns = 0;
        await using var owner = new PublishingAnnounceOwner(connector, (_, _) =>
        { Interlocked.Increment(ref dns); throw new InvalidOperationException(); });
        var session = StartFixture(owner, new(enabled, "test", "pcp://unresolved.example"), new(Guid.NewGuid()), new(Guid.NewGuid()),
            new("test", "", "", "https://example.com/", "FLV", 128),
            new(IPAddress.Parse("192.168.1.2"), 7144), new(IPAddress.Parse("198.41.0.4"), 17144), () => ready);
        Assert.True(await session.StopAsync(PcpAnnounceStopReason.Dispose));
        Assert.Equal(0, dns);
        Assert.Equal(0, connector.Calls);
    }

    [Fact]
    public async Task PendingDnsDoesNotEscapeCleanupOrEnableReplacement()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dns = new TaskCompletionSource<IPAddress[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connector = new Connector();
        await using var owner = new PublishingAnnounceOwner(connector, (_, _) => { started.TrySetResult(); return dns.Task; });
        PublishingAnnounceSession Start() => StartFixture(owner, new(true, "test", "pcp://unresolved.example"), new(Guid.NewGuid()), new(Guid.NewGuid()),
            new("test", "", "", "https://example.com/", "FLV", 128),
            new(IPAddress.Parse("192.168.1.2"), 7144), new(IPAddress.Parse("198.41.0.4"), 17144), () => true);
        var session = Start();
        await started.Task.WaitAsync(AnnounceTestData.Deadline);
        Assert.False(await session.StopAsync(PcpAnnounceStopReason.Finish));
        Assert.Throws<InvalidOperationException>(() => Start());
        dns.SetResult(new[] { IPAddress.Parse("198.41.0.5") });
        Assert.False(session.CleanupProven);
        Assert.Equal(0, connector.Calls);
    }

    private sealed class Connector : IPcpAnnounceConnector
    {
        internal AnnounceFakeConnection Connection { get; } = new();
        internal int Calls;
        public Task<IPcpAnnounceConnection> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
        {
            Assert.Equal("198.41.0.5", endpoint.Address.ToString());
            Interlocked.Increment(ref Calls);
            return Task.FromResult<IPcpAnnounceConnection>(Connection);
        }
    }

    [Theory]
    [InlineData(17144, true)]
    [InlineData(0, false)]
    [InlineData(7144, false)]
    [InlineData(null, false)]
    public async Task ProductionGateUsesTrackerReportAndExternalEndpoint(int? port, bool allowed)
    {
        var connector = new Connector();
        var oleh = PcpHeloCodec.CreateOleh("fake", Guid.NewGuid());
        var fields = oleh.Children.Concat(new[] { PcpAtom.Leaf(PcpKnownAtomNames.HeloRemoteIp,
            PcpWireValueCodec.EncodeLegacyIpv4(IPAddress.Parse("198.41.0.4"))) }).ToList();
        if (port is not null) fields.Add(PcpAtom.Leaf(PcpKnownAtomNames.HeloPort, PcpWireValueCodec.EncodeUInt16((ushort)port)));
        connector.Connection.ReplaceHandshake(PcpAtom.Parent(oleh.Name, fields));
        await using var owner = new PublishingAnnounceOwner(connector, (_, _) => throw new InvalidOperationException("Numeric endpoint must not resolve DNS."));
        var sid = new ChannelSessionId(Guid.NewGuid());
        var session = StartFixture(owner, new(true, "test", "pcp://198.41.0.5:7144"), new(Guid.NewGuid()), sid,
            new ChannelMetadataSnapshot("test", "", "", "https://example.com/", "FLV", 128),
            new(IPAddress.Parse("192.168.1.2"), 7144), new(IPAddress.Parse("198.41.0.4"), 17144), () => true);
        var handshake = await connector.Connection.NextWriteAsync();
        var helo = PcpHeloCodec.ParseHelo(AnnounceTestData.Decode(handshake[PcpPreamble.Size..]));
        Assert.Equal(sid.Value, helo.SessionId);
        Assert.Equal((ushort)17144, helo.PingPort);
        Assert.Null(helo.OpenPort);
        if (allowed)
        {
            Assert.Equal(PcpAnnounceCodec.Broadcast, AnnounceTestData.Decode(await connector.Connection.NextWriteAsync()).Name);
            Assert.False(session.Status.IndependentReachabilityVerified);
        }
        else
        {
            await connector.Connection.CloseObserved.Task.WaitAsync(AnnounceTestData.Deadline);
            Assert.False(connector.Connection.TryWrite(out _));
        }
        Assert.True(await session.StopAsync(PcpAnnounceStopReason.ObsNormalEos));
        Assert.False(session.Status.TrackerReportedOpen);
        Assert.False(session.Status.ListingConfirmed);
        Assert.False(session.Status.ListingRemovalConfirmed);
        Assert.Equal(1, connector.Calls);
        Assert.True(connector.Connection.Closed);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("192.168.1.2")]
    [InlineData("192.0.2.1")]
    public async Task ResolverRejectsMixedOrUnsafeAnswers(string unsafeAddress)
    {
        var calls = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => PcpAnnounceProductionConnector.ResolveAsync(
            PcpAnnounceEndpoint.Parse("pcp://explicit.example:7144"), (_, _) =>
            {
                calls++;
                return Task.FromResult(new[] { IPAddress.Parse("198.41.0.5"), IPAddress.Parse(unsafeAddress) });
            }, CancellationToken.None));
        Assert.Equal(1, calls);
    }
    [Fact]
    public async Task TransientTransportDropAutomaticallyReconnectsWithLatestMetadata()
    {
        var first = new AnnounceFakeConnection();
        var second = new AnnounceFakeConnection();
        foreach (var connection in new[] { first, second })
        {
            var oleh = PcpHeloCodec.CreateOleh("fake", Guid.NewGuid());
            var fields = oleh.Children.Concat(new[]
            {
                PcpAtom.Leaf(PcpKnownAtomNames.HeloRemoteIp, PcpWireValueCodec.EncodeLegacyIpv4(IPAddress.Parse("198.41.0.4"))),
                PcpAtom.Leaf(PcpKnownAtomNames.HeloPort, PcpWireValueCodec.EncodeUInt16(17144)),
            }).ToList();
            connection.ReplaceHandshake(PcpAtom.Parent(oleh.Name, fields));
        }

        var queue = new Queue<AnnounceFakeConnection>([first, second]);
        var connectorCalls = 0;
        var connector = new SequentialConnector(() =>
        {
            Interlocked.Increment(ref connectorCalls);
            return queue.Dequeue();
        });

        await using var owner = new PublishingAnnounceOwner(
            connector,
            (_, _) => throw new InvalidOperationException("Numeric endpoint must not resolve DNS."),
            reconnectBaseDelay: TimeSpan.FromMilliseconds(10));
        var advertised = new IPEndPoint(IPAddress.Parse("198.41.0.4"), 17144);
        var session = StartFixture(
            owner,
            new(true, "test", "pcp://198.41.0.5:7144"),
            new(Guid.NewGuid()),
            new(Guid.NewGuid()),
            new("Initial Title", "", "", "https://example.com/", "FLV", 128),
            new(IPAddress.Parse("192.168.1.2"), 7144),
            advertised,
            () => true);

        _ = await first.NextWriteAsync(); // HELO
        var initialBcst = AnnounceTestData.Decode(await first.NextWriteAsync());
        Assert.Equal("Initial Title", AnnounceTestData.Name(initialBcst));

        Assert.True(session.UpdateMetadata(new("Updated Title", "", "", "https://example.com/", "FLV", 128), advertised));
        _ = await first.NextWriteAsync();

        first.Disconnect();

        _ = await second.NextWriteAsync(); // HELO on reconnect
        var reconnectBcst = AnnounceTestData.Decode(await second.NextWriteAsync());
        Assert.Equal("Updated Title", AnnounceTestData.Name(reconnectBcst));
        Assert.Equal(2, Volatile.Read(ref connectorCalls));

        Assert.True(await session.StopAsync(PcpAnnounceStopReason.Finish));
        Assert.True(session.CleanupProven);
    }

    [Fact]
    public void ReconnectPolicyAndBackoffRespectPermanentFailuresAndCap()
    {
        var id = new PcpAnnounceIdentity(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        Assert.True(PublishingAnnounceSession.ShouldReconnect(new(id, PcpAnnounceFailure.Transport, true, false, false, true, null)));
        Assert.True(PublishingAnnounceSession.ShouldReconnect(new(id, PcpAnnounceFailure.Connection, false, false, false, true, null)));
        Assert.True(PublishingAnnounceSession.ShouldReconnect(new(id, PcpAnnounceFailure.Timeout, false, false, false, true, null)));

        Assert.False(PublishingAnnounceSession.ShouldReconnect(new(id, PcpAnnounceFailure.Transport, true, false, false, false, null)));
        Assert.False(PublishingAnnounceSession.ShouldReconnect(new(id, PcpAnnounceFailure.Protocol, false, false, false, true, null)));
        Assert.False(PublishingAnnounceSession.ShouldReconnect(new(id, PcpAnnounceFailure.Banned, false, false, false, true, null)));
        Assert.False(PublishingAnnounceSession.ShouldReconnect(new(id, PcpAnnounceFailure.Cancelled, false, false, false, true, null)));
        Assert.False(PublishingAnnounceSession.ShouldReconnect(new(id, PcpAnnounceFailure.None, true, true, true, true, PcpAnnounceStopReason.Finish)));

        var baseDelay = TimeSpan.FromSeconds(5);
        Assert.Equal(TimeSpan.FromSeconds(5), PublishingAnnounceSession.ComputeReconnectDelay(0, baseDelay));
        Assert.Equal(TimeSpan.FromSeconds(10), PublishingAnnounceSession.ComputeReconnectDelay(1, baseDelay));
        Assert.Equal(TimeSpan.FromSeconds(20), PublishingAnnounceSession.ComputeReconnectDelay(2, baseDelay));
        Assert.Equal(TimeSpan.FromSeconds(30), PublishingAnnounceSession.ComputeReconnectDelay(3, baseDelay));
        Assert.Equal(TimeSpan.FromSeconds(30), PublishingAnnounceSession.ComputeReconnectDelay(10, baseDelay));
    }

    [Fact]
    public async Task PersistentBroadcastIdIsNormalizedAndPreservedAcrossOwners()
    {
        var raw = Guid.Parse("FF112233-4455-6677-8899-AABBCCDDEEFF");
        await using var first = new PublishingAnnounceOwner(raw);
        var encoded = PcpWireValueCodec.EncodeGuid(first.BroadcastId);
        Assert.Equal((byte)0, encoded[0]);
        Assert.NotEqual(Guid.Empty, first.BroadcastId);

        await using var second = new PublishingAnnounceOwner(first.BroadcastId);
        Assert.Equal(first.BroadcastId, second.BroadcastId);
    }

    private sealed class SequentialConnector(Func<IPcpAnnounceConnection> nextConnection) : IPcpAnnounceConnector
    {
        public Task<IPcpAnnounceConnection> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken) =>
            Task.FromResult(nextConnection());
    }

    private static PublishingAnnounceSession StartFixture(PublishingAnnounceOwner owner, PublishingAnnounceOptions options,
        ChannelId channel, ChannelSessionId session, ChannelMetadataSnapshot metadata, IPEndPoint bind,
        IPEndPoint advertised, Func<bool> ready) => owner.Start(options, channel, session, metadata, bind, advertised, ready,
            () => new(channel, session, 1, true, false, 0, 0, ready()));
}
