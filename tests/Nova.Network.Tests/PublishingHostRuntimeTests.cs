using System.Net;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Policies;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Protocol.Pcp;
using static PeerCast.Nova.Network.Tests.AnnounceTestData;

namespace PeerCast.Nova.Network.Tests;

public sealed class PublishingHostRuntimeTests
{
    [Fact]
    public async Task RetiredAdmissionCannotReuseTheOldHostGeneration()
    {
        using var policy = new RelayAdmissionPolicy();
        await using var wire = new HostRuntimeWire();
        using var scope = policy.OpenChannel(wire.Channel, wire.Session, RelayChannelKind.Broadcast);
        wire.Read = () => PublishingAnnounceRuntimeSnapshot.Create(wire.Status(ChannelSessionState.Receiving, true, true),
            scope.IsCurrent, scope.CanReserve, true, true, 0, 0);
        await wire.StartAsync(); await wire.NextAsync();
        using var replacement = policy.OpenChannel(wire.Channel, new(Guid.NewGuid()), RelayChannelKind.Broadcast);
        wire.Advance();
        await wire.Connector.Connection.CloseObserved.Task.WaitAsync(Deadline);
        Assert.False(wire.Connector.Connection.TryWrite(out _));
        Assert.False(wire.SessionStatus.TrackerReportedOpen);
        Assert.True(replacement.CanReserve);
        Assert.Equal(0, policy.ReservedCount);
    }

    [Fact]
    public async Task QueuedChangesUseLatestRuntimeAndMetadataWithOneWriter()
    {
        await using var wire = new HostRuntimeWire();
        wire.Connector.Connection.BlockWriteAt = 2;
        wire.Read = () => new(wire.Channel, wire.Session, 1, false, false, 0, 0, true);
        var start = wire.StartAsync();
        await wire.Connector.Connection.Blocked.Task.WaitAsync(Deadline);
        wire.Read = () => new(wire.Channel, wire.Session, 2, true, true, 2, 8, true);
        for (var i = 0; i < 10; i++) wire.Pulse();
        wire.Connector.Connection.Release.TrySetResult();
        await start;
        Assert.Equal(0x01, Flags(await wire.NextAsync())); // Already admitted in-flight snapshot.
        var latest = await wire.NextAsync();
        Assert.Equal(0x13, Flags(latest));
        Assert.Equal(2U, Number(latest, "numr"));
        Assert.True(wire.UpdateMetadata("latest metadata"));
        Assert.Equal("latest metadata", Name(await wire.NextAsync()));
        wire.Advance();
        Assert.Equal(2U, Number(await wire.NextAsync(), "numr"));
        Assert.Equal(1, wire.Connector.Connection.MaxActiveWrites);
    }

    [Fact]
    public async Task HostGenerationUptimeFreezesAtStopAndResetsOnNewOwner()
    {
        using var time = new AnnounceManualTimeProvider();
        time.Advance(TimeSpan.FromSeconds(100));
        await using var host = new ObsPublishChannelHost(new() { Ingest = new() { Port = 0 } }, null, null, null, null, null,
            announceTime: time);
        await using var wire = new HostRuntimeWire(host.ChannelId, host.SessionId);
        // Only the tracker capability is simulated; uptime is read from the actual localhost host.
        wire.Read = () => host.ReadAnnounceRuntime() with { CapabilityValid = true };
        host.Start();
        await wire.StartAsync();
        Assert.Equal(0U, Number(await wire.NextAsync(), "uptm"));
        time.Advance(TimeSpan.FromSeconds(45));
        wire.Advance();
        Assert.Equal(45U, Number(await wire.NextAsync(), "uptm"));
        await host.DisposeAsync();
        time.Advance(TimeSpan.FromSeconds(200));
        Assert.True(await wire.StopAsync());
        Assert.Equal(45U, Number(await wire.NextAsync(), "uptm"));
        await using var next = new ObsPublishChannelHost(new() { Ingest = new() { Port = 0 } }, null, null, null, null, null,
            announceTime: time);
        next.Start();
        await using var nextWire = new HostRuntimeWire(next.ChannelId, next.SessionId);
        nextWire.Read = () => next.ReadAnnounceRuntime() with { CapabilityValid = true };
        await nextWire.StartAsync();
        var frame = await nextWire.NextAsync();
        Assert.Equal(0U, Number(frame, "uptm"));
        Assert.NotEqual(host.SessionId, next.SessionId);
        time.Advance(TimeSpan.FromSeconds((double)uint.MaxValue + 10));
        nextWire.Advance();
        Assert.Equal(uint.MaxValue, Number(await nextWire.NextAsync(), "uptm"));
    }

    [Theory]
    [InlineData(ChannelSessionState.Created, false, false, false)]
    [InlineData(ChannelSessionState.Negotiating, true, false, false)]
    [InlineData(ChannelSessionState.Receiving, true, false, false)]
    [InlineData(ChannelSessionState.Receiving, false, true, false)]
    [InlineData(ChannelSessionState.Receiving, true, true, true)]
    [InlineData(ChannelSessionState.Stopping, true, true, false)]
    [InlineData(ChannelSessionState.Stopped, true, true, false)]
    public async Task InitialHostUsesActualMediaState(ChannelSessionState state, bool publisher, bool head, bool receiving)
    {
        await using var wire = new HostRuntimeWire();
        wire.Read = () => PublishingAnnounceRuntimeSnapshot.Create(wire.Status(state, publisher, head), true, true, true, true, 2, 7);
        await wire.StartAsync();
        var frame = await wire.NextAsync();
        Assert.Equal(receiving ? 0x13 : 0x01, Flags(frame));
        Assert.Equal(2U, Number(frame, "numr"));
        Assert.Equal(0U, Number(frame, "numl"));
        Assert.Equal(7U, Number(frame, "uptm"));
        Assert.Equal(wire.Channel.Value, PcpWireValueCodec.DecodeGuid(Field(Field(frame, "chan"), "id").Content.Span));
        Assert.Equal(wire.Session.Value, PcpWireValueCodec.DecodeGuid(Field(Field(frame, "host"), "id").Content.Span));
        Assert.Equal(wire.OwnerBroadcastId, PcpWireValueCodec.DecodeGuid(Field(Field(frame, "chan"), "bcid").Content.Span));
        Assert.Equal((ushort)17144, PcpWireValueCodec.DecodeUInt16(Field(Field(frame, "host"), "port").Content.Span));
        Assert.Equal(PcpWireValueCodec.EncodeLegacyIpv4(wire.Advertised.Address), Field(Field(frame, "host"), "ip").Content.ToArray());
        Assert.True(await wire.StopAsync());
        Assert.Equal(0x01, Flags(await wire.NextAsync()));
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task UnknownOrFullCapacityDoesNotAdvertiseRoom(bool admission, bool tcp, bool fanout)
    {
        await using var wire = new HostRuntimeWire();
        wire.Read = () => PublishingAnnounceRuntimeSnapshot.Create(wire.Status(ChannelSessionState.Receiving, true, true),
            true, admission, tcp, fanout, 3, 9);
        await wire.StartAsync();
        Assert.Equal(0x11, Flags(await wire.NextAsync()));
    }

    [Fact]
    public async Task PeriodicHostReadsLatestPolicyCapacityCountsUptimeAndReceiving()
    {
        using var policy = new RelayAdmissionPolicy(new(2, 1));
        await using var wire = new HostRuntimeWire();
        using var scope = policy.OpenChannel(wire.Channel, wire.Session, RelayChannelKind.Broadcast);
        var receiving = false;
        var relays = 0;
        wire.Read = () => PublishingAnnounceRuntimeSnapshot.Create(wire.Status(
            receiving ? ChannelSessionState.Receiving : ChannelSessionState.Negotiating, receiving, receiving),
            true, scope.CanReserve, true, true, relays, (uint)wire.Clock.GetUtcNow().ToUnixTimeSeconds());
        await wire.StartAsync();
        Assert.Equal(0x01, Flags(await wire.NextAsync())); // Correct false initial BCST is permitted.
        receiving = true;
        wire.Pulse();
        Assert.Equal(0x13, Flags(await wire.NextAsync()));
        var lease = scope.TryReserve(1).Reservation!;
        relays = 1;
        var before = policy.ReservedCount;
        wire.Advance();
        var full = await wire.NextAsync();
        Assert.Equal(0x11, Flags(full));
        Assert.Equal(1U, Number(full, "numr"));
        Assert.Equal(30U, Number(full, "uptm"));
        Assert.Equal(before, policy.ReservedCount); // Snapshot did not test-reserve capacity.
        lease.Dispose();
        relays = 0;
        wire.Advance();
        var available = await wire.NextAsync();
        Assert.Equal(0x13, Flags(available));
        Assert.Equal(0U, Number(available, "numr"));
        Assert.Equal(60U, Number(available, "uptm"));
        using var other = policy.OpenChannel(new(Guid.NewGuid()), new(Guid.NewGuid()), RelayChannelKind.Viewing);
        using var first = other.TryReserve(2).Reservation!;
        using var second = other.TryReserve(3).Reservation!;
        wire.Advance();
        Assert.Equal(0x11, Flags(await wire.NextAsync())); // Global full, even though this channel has room.
        receiving = false;
        wire.Advance();
        Assert.Equal(0x01, Flags(await wire.NextAsync()));
        Assert.Equal(1, wire.Connector.Calls);
    }

    [Fact]
    public async Task StaleGenerationCannotProduceAnotherHost()
    {
        await using var wire = new HostRuntimeWire();
        await wire.StartAsync(); await wire.NextAsync();
        wire.Read = () => new(new(Guid.NewGuid()), wire.Session, 9, true, true, 9, 9, true);
        wire.Advance();
        await wire.Connector.Connection.CloseObserved.Task.WaitAsync(Deadline);
        Assert.False(wire.Connector.Connection.TryWrite(out _));
        Assert.False(wire.SessionStatus.AnnounceSent && wire.SessionStatus.TrackerReportedOpen);
    }

    [Fact]
    public async Task StopForcesFalseFlagsWhileKeepingLatestCountAndUptime()
    {
        await using var wire = new HostRuntimeWire();
        await wire.StartAsync(); await wire.NextAsync();
        wire.Read = () => new(wire.Channel, wire.Session, 99, true, true, 4, 99, false);
        Assert.True(await wire.StopAsync());
        var stopped = await wire.NextAsync();
        Assert.Equal(0x01, Flags(stopped));
        Assert.Equal(4U, Number(stopped, "numr"));
        Assert.Equal(99U, Number(stopped, "uptm"));
        Assert.False(wire.SessionStatus.ListingRemovalConfirmed);
        Assert.Equal(0, wire.Connector.Connection.PendingReads);
    }

    internal static byte Flags(PcpAtom frame) => Field(Field(frame, "host"), "flg1").Content.Span[0];
    internal static uint Number(PcpAtom frame, string name) => PcpWireValueCodec.DecodeUInt32(Field(Field(frame, "host"), name).Content.Span);
}

internal sealed class HostRuntimeWire : IAsyncDisposable
{
    internal ChannelId Channel { get; }
    internal ChannelSessionId Session { get; }
    internal IPEndPoint Advertised { get; } = new(IPAddress.Parse("198.41.0.4"), 17144);
    internal AnnounceManualTimeProvider Clock { get; } = new();
    internal WireConnector Connector { get; } = new();
    private readonly PublishingAnnounceOwner _owner;
    private PublishingAnnounceSession? _session;
    internal Func<PublishingAnnounceRuntimeSnapshot> Read { get; set; }
    internal Guid OwnerBroadcastId { get; private set; }
    internal PublishingAnnounceStatus SessionStatus => _session!.Status;
    internal HostRuntimeWire(ChannelId? channel = null, ChannelSessionId? session = null)
    {
        Channel = channel ?? new(Guid.NewGuid());
        Session = session ?? new(Guid.NewGuid());
        _owner = new(Connector, (_, _) => throw new InvalidOperationException("No DNS in fixture."), Clock);
        Read = () => new(Channel, Session, 1, true, true, 0, 0, true);
        var oleh = PcpHeloCodec.CreateOleh("fake", Guid.NewGuid());
        Connector.Connection.ReplaceHandshake(PcpAtom.Parent(oleh.Name, oleh.Children.Concat(new[]
        {
            PcpAtom.Leaf(PcpKnownAtomNames.HeloPort, PcpWireValueCodec.EncodeUInt16(17144)),
            PcpAtom.Leaf(PcpKnownAtomNames.HeloRemoteIp, PcpWireValueCodec.EncodeLegacyIpv4(Advertised.Address)),
        }).ToArray()));
    }
    internal ObsPublishStatusSnapshot Status(ChannelSessionState state, bool publisher, bool head) =>
        new(Channel, Session, 1, state, true, publisher, head, null, "", null, null, null, null)
        { WanRelay = new(true, true, "192.168.1.2", 7144, "198.41.0.4", 17144, ObsPublishWanFailure.None, false) };
    internal async Task StartAsync()
    {
        _session = _owner.Start(new(true, "test", "pcp://198.41.0.5:7144"), Channel, Session,
            new("test", "", "", "https://example.com/", "FLV", 128), new(IPAddress.Parse("192.168.1.2"), 7144),
            Advertised, () => true, () => Read());
        var helo = Decode((await Connector.Connection.NextWriteAsync())[12..]);
        OwnerBroadcastId = PcpWireValueCodec.DecodeGuid(Field(helo, "bcid").Content.Span);
        await Clock.TimerCreated.Task.WaitAsync(Deadline);
    }
    internal async Task<PcpAtom> NextAsync() => Decode(await Connector.Connection.NextWriteAsync());
    internal bool UpdateMetadata(string name) => _session!.UpdateMetadata(new(name, "", "", "https://example.com/", "FLV", 128), Advertised);
    internal void Pulse() => _session?.NotifyRuntimeChanged();
    internal void Advance() => Clock.Advance(TimeSpan.FromSeconds(30));
    internal Task<bool> StopAsync() => _session!.StopAsync(PcpAnnounceStopReason.Finish);
    public async ValueTask DisposeAsync() { await _owner.DisposeAsync(); Clock.Dispose(); }
    internal sealed class WireConnector : IPcpAnnounceConnector
    {
        internal AnnounceFakeConnection Connection { get; } = new();
        internal int Calls;
        public Task<IPcpAnnounceConnection> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult<IPcpAnnounceConnection>(Connection); }
    }
}
