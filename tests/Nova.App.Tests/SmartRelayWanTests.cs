using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.PortMapping;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;
using static PeerCast.Nova.App.Tests.PublicChannelTestFixture;

namespace PeerCast.Nova.App.Tests;

public sealed partial class SmartRelayWanTests
{
    [Fact]
    public async Task OffAndSavingNeverBindOrAcquireMapping()
    {
        await using var test = await Fixture.Create(wan: false);
        Assert.Null(test.Host);
        await using var channel = await test.Base.ViewingAsync(test.Token);
        Assert.Null(test.Host);
        var next = GenesisGuiNextConnectionInput.From(test.Base.State.Connection.Saved) with { WanRelayEnabled = true };
        Assert.True((await test.Base.State.Connection.SaveAsync(next)).Saved);
        Assert.False(test.Base.State.Connection.Startup.WanRelayEnabled);
        Assert.True(test.Base.State.Connection.HasPendingRestart);
        Assert.Null(test.Host);
        Assert.Empty(test.Manager.ReadState().RegisteredInternalPorts);
        Assert.Empty(test.Router.Calls);
    }

    [Fact]
    public async Task TwoChannelsShareFixedPortAndForwardDistinctHeadData()
    {
        await using var test = await Fixture.Create();
        Assert.Null(test.Host);
        await using var one = await test.Base.ViewingAsync(test.Token);
        await using var two = await test.Base.ViewingAsync(test.Token);
        await one.Owner.PublicReady; await two.Owner.PublicReady;
        Assert.Equal(test.Port, test.Host!.LocalEndPoint!.Port);
        Assert.Equal(2, test.Host.Snapshot.Channels);
        Assert.Equal(new[] { test.Port }, test.Manager.ReadState().RegisteredInternalPorts);
        await using var first = await WirePeer.Connect(test.Port, one.Owner.ChannelId, test.Token);
        await using var second = await WirePeer.Connect(test.Port, two.Owner.ChannelId, test.Token);
        Assert.Equal(one.Owner.SessionId.Value, first.Handshake.RemotePeer!.SessionId);
        Assert.Equal(two.Owner.SessionId.Value, second.Handshake.RemotePeer!.SessionId);
        await one.ProducerFanout.ConsumeAsync(Packet(one.Owner, 0x64616568, 0, [11]), test.Token);
        await two.ProducerFanout.ConsumeAsync(Packet(two.Owner, 0x64616568, 0, [22]), test.Token);
        await one.ProducerFanout.ConsumeAsync(Packet(one.Owner, 0x61746164, 100, [1, 2]), test.Token);
        await two.ProducerFanout.ConsumeAsync(Packet(two.Owner, 0x61746164, 200, [3, 4]), test.Token);
        Assert.Equal(new byte[] { 11 }, await first.Payload(test.Token));
        Assert.Equal(new byte[] { 1, 2 }, await first.Payload(test.Token));
        Assert.Equal(new byte[] { 22 }, await second.Payload(test.Token));
        Assert.Equal(new byte[] { 3, 4 }, await second.Payload(test.Token));
        await using var missing = await WirePeer.Connect(test.Port, new(Guid.NewGuid()), test.Token);
        Assert.Equal(404, missing.Handshake.HttpStatusCode);
        Assert.Contains("未確認", test.Host.Snapshot.Reachability);
        Assert.Equal(2, test.Base.State.NetworkPolicies.Relays.ReservedCount);
        await first.Quit(test.Token); await second.Quit(test.Token);
        await UntilAsync(() => test.Base.State.DownstreamRelays.ActiveRelayCount == 0, test.Token);
    }

    [Fact]
    public async Task LocalAndPublicShareAdmissionDirectoryReferralsAndActualCleanup()
    {
        await using var test = await Fixture.Create(total: 1);
        await using var channel = await test.Base.ViewingAsync(test.Token);
        await channel.Owner.PublicReady;
        var peers = channel.Owner.ViewingRuntime!.DownstreamServer.Peers;
        var directSid = Guid.NewGuid();
        var direct = Host(channel.Owner, directSid, "1.1.1.1", 7144);
        var alternative = Host(channel.Owner, Guid.NewGuid(), "8.8.8.8", 7145);
        peers.Observe(direct, new());
        await using var accepted = await WirePeer.Connect(test.Port, channel.Owner.ChannelId, test.Token, directSid);
        await accepted.Send(PcpReferralPlanner.CreateHost(alternative), test.Token);
        await UntilAsync(() => peers.Count == 2, test.Token);
        Assert.DoesNotContain(peers.Snapshot(), p => p.Host.PeerSessionId == directSid);
        await using var full = await WirePeer.Connect(test.Port, channel.Owner.ChannelId, test.Token);
        Assert.Equal(503, full.Handshake.HttpStatusCode);
        Assert.Equal(1003U, full.Handshake.TerminalCode);
        Assert.Equal(alternative.PeerSessionId, Assert.Single(full.Handshake.RelayHostCandidates).PeerSessionId);
        await using var local = await Peer.ConnectAsync(channel.Owner, test.Token);
        Assert.Equal(503, local.Handshake.HttpStatusCode);
        Assert.Equal(1, test.Base.State.NetworkPolicies.Relays.ReservedCount);
        await accepted.Quit(test.Token);
        await UntilAsync(() => test.Base.State.DownstreamRelays.ActiveRelayCount == 0, test.Token);
        Assert.Contains(peers.Snapshot(), p => p.Host.PeerSessionId == directSid);
        await using var localAccepted = await Peer.ConnectAsync(channel.Owner, test.Token);
        Assert.Equal(200, localAccepted.Handshake.HttpStatusCode);
        await localAccepted.QuitAsync(test.Token);
    }

    [Fact]
    public async Task RetiredOwnerCannotRouteOrRemoveReplacementOnSharedPort()
    {
        await using var test = await Fixture.Create();
        await using var keep = await test.Base.ViewingAsync(test.Token);
        await keep.Owner.PublicReady;
        await using var old = await test.Base.ViewingAsync(test.Token);
        await old.Owner.PublicReady;
        await using var child = await WirePeer.Connect(test.Port, old.Owner.ChannelId, test.Token);
        await using var current = await test.Base.ViewingAsync(test.Token, old.Owner.ChannelId);
        await current.Owner.PublicReady;
        await old.Run.WaitAsync(test.Token);
        Assert.True(old.Owner.IsRetired);
        Assert.True(current.Owner.IsCurrent);
        await old.Owner.DisposeAsync();
        Assert.Equal(2, test.Host!.Snapshot.Channels);
        Assert.Equal(test.Port, test.Host.LocalEndPoint!.Port);
        await using var replacement = await WirePeer.Connect(test.Port, current.Owner.ChannelId, test.Token);
        Assert.Equal(200, replacement.Handshake.HttpStatusCode);
        Assert.Equal(current.Owner.SessionId.Value, replacement.Handshake.RemotePeer!.SessionId);
        await replacement.Quit(test.Token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NatMappingUsesBoundPortAndPreservesOtherOwners(bool reject)
    {
        await using var test = await Fixture.Create();
        test.Router.Reject = reject;
        await using var channel = await test.Base.ViewingAsync(test.Token);
        await channel.Owner.PublicReady;
        await test.Manager.ReconcileAsync(test.Token);
        Assert.Equal(test.Port, Assert.Single(test.Router.Calls).Identity.InternalPort);
        Assert.Equal(reject ? NatMappingStatus.Rejected : NatMappingStatus.Mapped, Assert.Single(test.Manager.Snapshot).Status);
        Assert.Contains("未確認", test.Host!.Snapshot.Reachability);
        Assert.Contains(reject ? "失敗" : "成功", test.Host.Snapshot.MappingText);
        if (!reject)
        {
            test.Clock.Advance(TimeSpan.FromSeconds(29));
            await test.Manager.ReconcileAsync(test.Token);
            Assert.Contains(test.Router.Calls, c => c.Operation == NatMappingOperation.Renew);
        }
        using var other = test.Manager.AcquirePublicTcpListener(new(IPAddress.Any, test.Port));
        await channel.DisposeAsync();
        Assert.Equal(GenesisSharedPublicRelayState.Stopped, test.Host.Snapshot.State);
        await test.Manager.ReconcileAsync(test.Token);
        Assert.DoesNotContain(test.Router.Calls, c => c.Operation == NatMappingOperation.Unmap);
        other.Dispose();
        await test.Manager.ReconcileAsync(test.Token);
        if (!reject) Assert.Contains(test.Router.Calls, c => c.Operation == NatMappingOperation.Unmap);
        Assert.Empty(test.Manager.ReadState().RegisteredInternalPorts);
    }

    [Fact]
    public async Task ManualMappingAndPortConflictRemainExplicit()
    {
        await using var test = await Fixture.Create();
        test.Manager.SetEnabled(false);
        await using var channel = await test.Base.ViewingAsync(test.Token);
        await channel.Owner.PublicReady;
        await test.Manager.ReconcileAsync(test.Token);
        Assert.Empty(test.Router.Calls);
        Assert.Contains("手動", test.Host!.Snapshot.MappingText);
        await channel.DisposeAsync();
        using var occupied = new TcpListener(IPAddress.Any, test.Port);
        occupied.Start();
        await using var upstream = new RelayActivationTestUpstream(test.Token);
        await using var owner = await test.Base.Channels.PrepareViewingAsync(upstream.Channel, new(IPAddress.Loopback, 0), cancellationToken: test.Token);
        var run = owner.RunAsync(test.Token);
        await Assert.ThrowsAsync<GenesisPublicRelayStartException>(() => owner.PublicReady.WaitAsync(test.Token));
        await Assert.ThrowsAsync<GenesisPublicRelayStartException>(() => run);
        Assert.Empty(test.Manager.ReadState().RegisteredInternalPorts);
        Assert.Equal(GenesisSharedPublicRelayState.Failed, test.Host.Snapshot.State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void PortZeroOrInvalidNeverCreatesWanHost(int port) =>
        Assert.ThrowsAny<Exception>(() => new GenesisGuiNextConnectionPreferences { ListenPort = port, WanRelayEnabled = true }.Validate());

    private static PcpRelayHostCandidate Host(GenesisGuiNextPublicChannelComposition.Owner owner, Guid sid, string address, int port) =>
        new(new(IPAddress.Parse(address), port), null) { ChannelId = owner.ChannelId.Value, PeerSessionId = sid, Flags = 0x12 };
    private static PcpChannelPacket Packet(GenesisGuiNextPublicChannelComposition.Owner owner, uint type, uint pos, byte[] bytes) =>
        new(owner.ChannelId, owner.SessionId, pos, type, pos, pos, bytes, null);

    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());
        internal void Advance(TimeSpan value) => Interlocked.Add(ref _ticks, value.Ticks);
    }
    private sealed class Router : INatMappingBackend
    {
        internal ConcurrentQueue<NatMappingRequest> Calls { get; } = new();
        internal bool Reject;
        internal TaskCompletionSource<NatBackendResult>? HoldMap;
        public Task<NatBackendResult> ExecuteAsync(NatMappingRequest request, CancellationToken token)
        {
            Calls.Enqueue(request);
            if (request.Operation == NatMappingOperation.Map && HoldMap is { } held) return held.Task;
            return Task.FromResult(new NatBackendResult(Reject ? NatBackendOutcome.Rejected : NatBackendOutcome.Success, request.SuggestedExternalPort, 30));
        }
    }
    private sealed class Fixture : INovaSharedPublicRelayProvider, IAsyncDisposable
    {
        internal required PublicChannelTestFixture Base;
        internal required NatPortMappingManager Manager;
        internal required int Port;
        internal Clock Clock { get; } = new();
        internal Router Router { get; } = new();
        internal GenesisSharedPublicRelay? Host;
        internal Action? BeforeGet;
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(15));
        internal CancellationToken Token => _timeout.Token;
        internal static async Task<Fixture> Create(bool wan = true, int? total = null)
        {
            using var free = new TcpListener(IPAddress.Loopback, 0); free.Start();
            var port = ((IPEndPoint)free.LocalEndpoint).Port;
            free.Stop();
            var settings = new GenesisGuiNextConnectionPreferences { ListenPort = port, WanRelayEnabled = wan, MaxRelaysTotal = total };
            var test = new Fixture { Base = await PublicChannelTestFixture.CreateAsync(settings), Port = port, Manager = null! };
            test.Manager = new(test.Router, new() { Enabled = true, RequestedLifetimeSeconds = 30 }, test.Clock);
            test.Manager.UpdateNetwork(new(new("192.168.1.1", "fixture"), "192.168.1.2", 1));
            test.Base.Channels.ConfigurePublic(settings, test);
            return test;
        }
        public GenesisSharedPublicRelay GetSharedRelay(GenesisGuiNextConnectionPreferences settings,
            Func<ChannelId, PcpRelayFanout, PcpRelayCoordinator?> resolveOwner)
        {
            BeforeGet?.Invoke();
            return Host ??= new(settings, Manager, resolveOwner,
                new() { SourceAllowed = a => a is not null && IPAddress.IsLoopback(a) });
        }
        public async ValueTask DisposeAsync()
        {
            try { await Base.DisposeAsync(); if (Host is not null) await Host.DisposeAsync(); }
            finally { await Manager.DisposeAsync(); _timeout.Dispose(); }
        }
    }
    private sealed class WirePeer(TcpTransportConnection connection, PipeReader reader, PcpRelayHandshakeResult handshake) : IAsyncDisposable
    {
        internal PcpRelayHandshakeResult Handshake => handshake;
        internal static async Task<WirePeer> Connect(int port, ChannelId channel, CancellationToken token, Guid? sid = null)
        {
            var connection = await new TcpTransportConnector(Transport).ConnectAsync("127.0.0.1", port, token);
            var reader = PipeReader.Create(connection.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
            try
            {
                var result = await PcpRelayHandshakeClient.NegotiateAsync(connection, reader, "127.0.0.1", port,
                    channel.Value, sid ?? Guid.NewGuid(), 0, 1, new(32768, 65536, 16, "PeerCastNova/Test", PcpHeloCodec.LegacyServantVersion), token);
                return new(connection, reader, result);
            }
            catch { await reader.CompleteAsync(); await connection.DisposeAsync(); throw; }
        }
        internal ValueTask Send(PcpAtom atom, CancellationToken token)
        { var bytes = new ArrayBufferWriter<byte>(); PcpAtomTreeCodec.Write(bytes, atom); return connection.WriteAsync(bytes.WrittenMemory, token); }
        internal ValueTask SendBytes(ReadOnlyMemory<byte> bytes, CancellationToken token) => connection.WriteAsync(bytes, token);
        internal ValueTask<int> Read(Memory<byte> buffer, CancellationToken token) => connection.ReadAsync(buffer, token);
        internal ValueTask Quit(CancellationToken token) => Send(PcpQuitCodec.Create(1000), token);
        internal async Task<byte[]> Payload(CancellationToken token)
        {
            await using var atoms = new PcpAtomPipeMessageReader(reader);
            var atom = await atoms.ReadNextAsync(token);
            return atom.FindFirst(PcpKnownAtomNames.ChannelPacket)!.FindFirst(PcpKnownAtomNames.Data)!.Content.ToArray();
        }
        internal async Task<uint> ReadQuit(CancellationToken token)
        {
            await using var atoms = new PcpAtomPipeMessageReader(reader);
            while (true)
            {
                var atom = await atoms.ReadNextAsync(token);
                if (atom.Name == PcpKnownAtomNames.Quit) return PcpWireValueCodec.DecodeUInt32(atom.Content.Span);
            }
        }
        public async ValueTask DisposeAsync() { await reader.CompleteAsync(); await connection.DisposeAsync(); }
    }
}
