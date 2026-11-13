using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.PortMapping;
using PeerCast.Nova.Network.Tests;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;
using static PeerCast.Nova.App.Tests.PublicChannelTestFixture;

namespace PeerCast.Nova.App.Tests;

public sealed partial class SmartRelayWanTests
{
    [Fact]
    public async Task StopDuringDelayedProviderJoinsStartupWithoutLateBindOrDisposedTokenUse()
    {
        await using var test = await Fixture.Create();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        test.BeforeGet = () => { entered.TrySetResult(); release.Task.WaitAsync(test.Token).GetAwaiter().GetResult(); };
        await using var upstream = new RelayActivationTestUpstream(test.Token);
        await using var owner = await test.Base.Channels.PrepareViewingAsync(upstream.Channel, new(IPAddress.Loopback, 0), cancellationToken: test.Token);
        var run = owner.RunAsync(test.Token);
        await entered.Task.WaitAsync(test.Token);
        var stopping = owner.DisposeAsync().AsTask();
        try
        {
            Assert.True(owner.IsRetired);
            Assert.False(stopping.IsCompleted);
            Assert.Empty(test.Manager.ReadState().RegisteredInternalPorts);
        }
        finally { release.TrySetResult(); }
        await stopping.WaitAsync(test.Token);
        await run.WaitAsync(test.Token);
        Assert.True(owner.CleanupConfirmed);
        Assert.Null(test.Host!.LocalEndPoint);
        Assert.Equal(0, test.Host.Snapshot.Channels);
        Assert.Empty(test.Manager.ReadState().RegisteredInternalPorts);
        Assert.Empty(test.Router.Calls);
    }

    [Fact]
    public async Task RecoveryKeepsSharedPortOwnerAndMappingButClosesOldPublicMediaGeneration()
    {
        await using var test = await Fixture.Create();
        var clock = new ManualNatTimeProvider();
        await using var upstream = new TcpTransportListener(new(IPAddress.Loopback, 0), Transport);
        upstream.Start();
        var id = new ChannelId(Guid.NewGuid());
        var lose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var end = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Parent(0, 100, 0x41, [1, 2, 3], lose.Task);
        var second = NextParent();
        async Task<Guid> NextParent() { await first; return await Parent(103, 103, 0x55, [4, 5], end.Task); }
        async Task<Guid> Parent(uint requested, uint position, byte head, byte[] data, Task release)
        {
            await using var connection = await upstream.AcceptAsync(test.Token);
            var reader = PipeReader.Create(connection.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
            try
            {
                var request = await PcpHttpHeaderBlockReader.ReadRequestAsync(reader, 32768, test.Token);
                Assert.Equal(requested.ToString(System.Globalization.CultureInfo.InvariantCulture), request.Headers["x-peercast-pos"]);
                await connection.WriteAsync("HTTP/1.0 200 OK\r\nContent-Type:application/x-peercast-pcp\r\n\r\n"u8.ToArray(), test.Token);
                await using var atoms = new PcpAtomPipeMessageReader(reader);
                var helo = await atoms.ReadNextAsync(test.Token);
                var sid = PcpHeloCodec.ParseHelo(helo).SessionId;
                Assert.Null(helo.FindFirst(PcpKnownAtomNames.HeloPort));
                Assert.Null(helo.FindFirst(PcpKnownAtomNames.HeloPing));
                var bytes = new ArrayBufferWriter<byte>();
                foreach (var atom in new[] { PcpHeloCodec.CreateOleh("PeerCastStation/Test", Guid.NewGuid()),
                    PcpAtom.Leaf(PcpKnownAtomNames.Okay, PcpWireValueCodec.EncodeUInt32(1)),
                    Media(0, [head], 0x64616568), Media(position, data, 0x61746164) })
                    PcpAtomTreeCodec.Write(bytes, atom);
                await connection.WriteAsync(bytes.WrittenMemory, test.Token);
                await release.WaitAsync(test.Token);
                return sid;
            }
            finally { await reader.CompleteAsync(); }
        }
        PcpAtom Media(uint position, byte[] payload, uint type) => PcpAtom.Parent(PcpKnownAtomNames.Channel,
            PcpAtom.Leaf(PcpKnownAtomNames.ChannelId, PcpWireValueCodec.EncodeGuid(id.Value)),
            PcpAtom.Parent(PcpKnownAtomNames.ChannelPacket,
                PcpAtom.Leaf(PcpKnownAtomNames.Type, PcpWireValueCodec.EncodeUInt32(type)),
                PcpAtom.Leaf(PcpKnownAtomNames.Position, PcpWireValueCodec.EncodeUInt32(position)),
                PcpAtom.Leaf(PcpKnownAtomNames.Data, payload)));
        await using var owner = await test.Base.Channels.PrepareViewingAsync(new("Recovery fixture",
            $"pcp://127.0.0.1:{upstream.LocalEndPoint.Port}/channel/{id.Value:N}"), new(IPAddress.Loopback, 0),
            new() { UpstreamSession = new() { SmartRelay = new() { TimeProvider = clock, JitterSample = () => .5 } } }, test.Token);
        var run = owner.RunAsync(test.Token);
        try
        {
            await owner.PublicReady.WaitAsync(test.Token);
            var runtime = owner.ViewingRuntime!;
            await UntilAsync(() => runtime.SmartRelaySnapshot.ResumePosition == 103, test.Token);
            await using var old = await WirePeer.Connect(test.Port, id, test.Token);
            Assert.Equal(new byte[] { 0x41 }, await old.Payload(test.Token));
            lose.TrySetResult();
            Assert.Equal(1003U, await old.ReadQuit(test.Token));
            await UntilAsync(() => runtime.SmartRelaySnapshot.State == PcpSmartRelayState.Backoff && clock.ActiveTimerCount > 0, test.Token);
            Assert.False(run.IsCompleted);
            Assert.True(owner.IsCurrent);
            Assert.Equal(test.Port, test.Host!.LocalEndPoint!.Port);
            Assert.Single(test.Manager.ReadState().RegisteredInternalPorts);
            Assert.False(owner.Fanout.HasRetainedHead);
            Assert.Equal(0, owner.Fanout.ActiveChildCount);
            Assert.Equal(0, test.Base.State.NetworkPolicies.Relays.ReservedCount);
            await using var gap = await WirePeer.Connect(test.Port, id, test.Token);
            Assert.Equal(1003U, gap.Handshake.TerminalCode);
            clock.Advance(TimeSpan.FromSeconds(1));
            await UntilAsync(() => runtime.SmartRelaySnapshot.ResumePosition == 105, test.Token);
            await using var resumed = await WirePeer.Connect(test.Port, id, test.Token);
            Assert.Equal(owner.SessionId.Value, resumed.Handshake.RemotePeer!.SessionId);
            Assert.Equal(new byte[] { 0x55 }, await resumed.Payload(test.Token));
            Assert.Equal(test.Port, test.Host.LocalEndPoint!.Port);
            Assert.Equal(1, test.Host.Snapshot.Channels);
            Assert.True(runtime.SmartRelaySnapshot.Discontinuities > 0);
            await resumed.Quit(test.Token);
        }
        finally { lose.TrySetResult(); end.TrySetResult(); await owner.DisposeAsync(); }
        await run;
        Assert.Equal(owner.SessionId.Value, await first);
        Assert.Equal(owner.SessionId.Value, await second);
        Assert.True(owner.CleanupConfirmed);
        Assert.Empty(test.Manager.ReadState().RegisteredInternalPorts);
    }

    [Fact]
    public async Task StopDuringPendingMapRemovesLateSuccessWithoutRebindingOrLeakingLease()
    {
        await using var test = await Fixture.Create();
        var held = test.Router.HoldMap = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var channel = await test.Base.ViewingAsync(test.Token);
        await channel.Owner.PublicReady;
        var reconcile = test.Manager.ReconcileAsync(test.Token);
        try
        {
            await UntilAsync(() => test.Router.Calls.Count == 1, test.Token);
            await channel.DisposeAsync();
            Assert.Empty(test.Manager.ReadState().RegisteredInternalPorts);
            Assert.Equal(GenesisSharedPublicRelayState.Stopped, test.Host!.Snapshot.State);
        }
        finally { held.TrySetResult(new(NatBackendOutcome.Success, test.Port, 30)); }
        await reconcile;
        await test.Manager.ReconcileAsync(test.Token);
        Assert.Contains(test.Router.Calls, c => c.Operation == NatMappingOperation.Unmap && c.Identity.InternalPort == test.Port);
        Assert.DoesNotContain(test.Manager.Snapshot, m => m.Status == NatMappingStatus.Mapped);
    }

    [Fact]
    public async Task UnconfirmedRouteDrainClosesAdmissionAndFailsClosedForAllDisposalCallers()
    {
        await using var test = await Fixture.Create();
        await using var fanout = new PcpRelayFanout();
        var settings = test.Base.State.Connection.Startup;
        var host = new GenesisSharedPublicRelay(settings, test.Manager, (_, _) => null);
        PcpDownstreamRelayRouteRegistry.PcpDownstreamRelayRouteLease? borrowed = null;
        var id = new ChannelId(Guid.NewGuid());
        var lease = await host.AcquireAsync(routes =>
        {
            var registration = routes.Register(id, fanout);
            Assert.True(routes.RouteRegistry.TryAcquire(id.Value, out borrowed));
            return registration;
        }, test.Token);
        try
        {
            var stopping = lease.DisposeAsync().AsTask();
            Assert.Same(stopping, lease.DisposeAsync().AsTask());
            await Assert.ThrowsAsync<TimeoutException>(() => stopping);
            Assert.True(host.Snapshot.CleanupFailed);
            Assert.Equal(0, host.Snapshot.Channels);
            await Assert.ThrowsAsync<InvalidOperationException>(() => host.AcquireAsync(r => r.Register(id, fanout), test.Token));
        }
        finally { borrowed?.Dispose(); }
        var disposal = host.DisposeAsync().AsTask();
        Assert.Same(disposal, host.DisposeAsync().AsTask());
        await Assert.ThrowsAsync<IOException>(() => disposal);
        Assert.True(host.Snapshot.CleanupFailed);
    }
}
