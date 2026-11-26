using System.IO.Pipelines;
using System.Net;
using System.Text;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Policies;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;
using static PeerCast.Nova.App.Tests.PublicChannelTestFixture;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextPublicChannelCompositionTests
{
    private static CancellationTokenSource TimeoutSource() => new(TimeSpan.FromSeconds(20));

    [Fact]
    public async Task StartupCompositionIsInertAndUsesTheExistingRegistry()
    {
        await using var fixture = await CreateAsync(new() { ListenPort = 7144, UseIpv6 = true, MaxRelaysTotal = 1 });
        Assert.Same(fixture.State.DownstreamRelays, fixture.Channels.Registry);
        Assert.Equal(0, fixture.Channels.OwnerCount);
        Assert.Equal(0, fixture.State.NetworkPolicies.Relays.ReservedCount);
        Assert.Equal(1, fixture.Store.LoadCount);
        Assert.False(fixture.Channels.IsDisposed);
    }

    [Fact]
    public async Task ExplicitReceiveOwnerIsViewingAndPreparationDoesNotBindOrConnectUpstream()
    {
        await using var fixture = await CreateAsync();
        var id = new ChannelId(Guid.NewGuid());
        await using var owner = await fixture.Channels.PrepareViewingAsync(
            new("Upstream source", $"pcp://tracker.invalid:7144/channel/{id.Value:N}"), new(IPAddress.Any, 0));
        Assert.Equal(RelayChannelKind.Viewing, owner.Kind);
        Assert.Equal(id, owner.ChannelId);
        Assert.Equal(0, owner.RequestedEndPoint.Port);
        Assert.Equal(IPAddress.Any, owner.RequestedEndPoint.Address);
        Assert.Equal(PcpRelayCoordinatorState.Created, owner.ViewingRuntime!.State);
        Assert.Equal(PcpDownstreamRelayServerState.Created, owner.DownstreamSnapshot.State);
        Assert.False(owner.ActivationStarted);
        Assert.Throws<InvalidOperationException>(() => owner.LocalEndPoint);
        var binding = Assert.IsType<GenesisDownstreamRelayOwnerRegistry.Owner>(
            fixture.State.DownstreamRelays.Resolve(id, owner.ViewingRuntime.DownstreamFanout));
        Assert.Equal(owner.SessionId, binding.SessionId);
        Assert.Equal(RelayChannelKind.Viewing, binding.Kind);
        Assert.Equal(0, fixture.State.DownstreamRelays.ActiveRelayCount);
        Assert.Equal(1, fixture.Store.LoadCount);
    }

    [Fact]
    public async Task ExplicitProducerOwnerIsBroadcastAndKeepsProducerFanoutOwnership()
    {
        await using var fixture = await CreateAsync();
        var id = new ChannelId(Guid.NewGuid());
        var generation = new ChannelSessionId(Guid.NewGuid());
        await using var fanout = new PcpRelayFanout();
        var requested = new IPEndPoint(IPAddress.Any, 0);
        var owner = await fixture.Channels.PrepareBroadcastAsync(id, generation, fanout, requested);
        requested.Port = 7144; // Must not mutate the prepared endpoint or bind plan.
        Assert.Equal(0, owner.RequestedEndPoint.Port);
        Assert.Equal(RelayChannelKind.Broadcast, owner.Kind);
        Assert.Equal(generation, owner.SessionId);
        Assert.False(owner.ActivationStarted);
        Assert.Throws<InvalidOperationException>(() => owner.LocalEndPoint);
        var registration = Assert.IsType<GenesisDownstreamRelayOwnerRegistry.Owner>(fixture.State.DownstreamRelays.Resolve(id, fanout));
        Assert.Equal(RelayChannelKind.Broadcast, registration.Kind);
        await Task.WhenAll(owner.DisposeAsync().AsTask(), owner.DisposeAsync().AsTask());
        Assert.True(owner.IsDisposed);
        Assert.True(owner.IsRetired);
        Assert.Equal(0, fixture.Channels.OwnerCount);
        Assert.False(owner.TryReserve(1, out _));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => owner.RunAsync());
        await using var producerChild = fanout.AddChild(new NoopSink());
        Assert.Equal(1, fanout.ActiveChildCount);
        using var independentOwner = fixture.State.DownstreamRelays.Register(new(Guid.NewGuid()), new(Guid.NewGuid()), RelayChannelKind.Viewing);
        Assert.True(independentOwner.TryReserve(1, out var lease));
        lease.Dispose(); // Composition does not own/dispose the registry.
    }

    [Theory]
    [InlineData(false, 7144)]
    [InlineData(false, 12345)]
    [InlineData(true, 0)]
    public async Task FixedPortsAndIpv6AreRejectedBeforeAnyOwnerIsRegistered(bool ipv6, int port)
    {
        await using var fixture = await CreateAsync();
        await using var fanout = new PcpRelayFanout();
        await Assert.ThrowsAsync<ArgumentException>(async () => await fixture.Channels.PrepareBroadcastAsync(
            new(Guid.NewGuid()), new(Guid.NewGuid()), fanout, new(ipv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback, port)));
        Assert.Equal(0, fixture.Channels.OwnerCount);
    }

    [Theory]
    [InlineData("http://127.0.0.1/channel/0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("pcp://127.0.0.1:7144/channel/not-an-id")]
    public async Task InvalidReceiveSourceIsNotGuessedAsAChannel(string source)
    {
        await using var fixture = await CreateAsync();
        await Assert.ThrowsAsync<ArgumentException>(async () => await fixture.Channels.PrepareViewingAsync(
            new("invalid", source), new(IPAddress.Loopback, 0)));
        Assert.Equal(0, fixture.Channels.OwnerCount);
    }

    [Fact]
    public async Task DuplicateGenerationOrFanoutCannotRetireTheValidOwner()
    {
        await using var fixture = await CreateAsync(new() { MaxRelaysTotal = 1 });
        var id = new ChannelId(Guid.NewGuid());
        var generation = new ChannelSessionId(Guid.NewGuid());
        await using var fanout = new PcpRelayFanout();
        await using var otherFanout = new PcpRelayFanout();
        await using var owner = await fixture.Channels.PrepareBroadcastAsync(id, generation, fanout, new(IPAddress.Loopback, 0));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Channels.PrepareBroadcastAsync(
            id, generation, otherFanout, new(IPAddress.Loopback, 0)));
        await Assert.ThrowsAsync<ArgumentException>(async () => await fixture.Channels.PrepareBroadcastAsync(
            id, new(Guid.NewGuid()), fanout, new(IPAddress.Loopback, 0)));
        Assert.False(owner.IsRetired);
        Assert.Equal(1, fixture.Channels.OwnerCount);
        Assert.True(owner.TryReserve(1, out var lease));
        lease.Dispose();
    }

    [Fact]
    public async Task CancellationBeforePreparationOrActivationCannotBind()
    {
        await using var fixture = await CreateAsync();
        await using var fanout = new PcpRelayFanout();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await fixture.Channels.PrepareBroadcastAsync(
            new(Guid.NewGuid()), new(Guid.NewGuid()), fanout, new(IPAddress.Loopback, 0),
            cancellationToken: new(true)));
        Assert.Equal(0, fixture.Channels.OwnerCount);
        await using var owner = await fixture.Channels.PrepareBroadcastAsync(
            new(Guid.NewGuid()), new(Guid.NewGuid()), fanout, new(IPAddress.Loopback, 0));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owner.RunAsync(new(true)));
        Assert.False(owner.ActivationStarted);
        Assert.Equal(PcpDownstreamRelayServerState.Created, owner.DownstreamSnapshot.State);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task StartupNullAndZeroLimitsReachTheActualOwnedPcpAdmission(int limit)
    {
        using var timeout = TimeoutSource();
        await using var fixture = await CreateAsync(limit switch
        {
            0 => new() { MaxRelaysTotal = 0 },
            1 => new() { MaxRelaysPerBroadcastChannel = 0 },
            2 => new() { MaxRelaysPerViewingChannel = 0 },
            _ => new(),
        });
        await using var channel = limit == 2
            ? await fixture.ViewingAsync(timeout.Token) : await fixture.BroadcastAsync(timeout.Token);
        await using var peer = await Peer.ConnectAsync(channel.Owner, timeout.Token);
        if (limit < 0)
        {
            Accepted(peer);
            Assert.Equal(1, fixture.State.NetworkPolicies.Relays.ReservedCount);
            await peer.QuitAsync(timeout.Token);
        }
        else Full(peer);
        await UntilAsync(() => channel.Owner.DownstreamSnapshot.CompletedConnectionCount == 1, timeout.Token);
        await UntilAsync(() => fixture.State.DownstreamRelays.ActiveRelayCount == 0, timeout.Token);
    }

    [Fact]
    public async Task TotalAndPerChannelLimitsShareOnePolicyAcrossActualBroadcastAndViewingOwners()
    {
        using var timeout = TimeoutSource();
        await using var fixture = await CreateAsync(new()
        {
            MaxRelaysTotal = 3,
            MaxRelaysPerBroadcastChannel = 2,
            MaxRelaysPerViewingChannel = 1,
        });
        await using var broadcast = await fixture.BroadcastAsync(timeout.Token);
        await using var viewing = await fixture.ViewingAsync(timeout.Token);
        await using var other = await fixture.BroadcastAsync(timeout.Token);
        Assert.Equal(0, fixture.State.DownstreamRelays.ActiveRelayCount); // Real upstream PCP is not counted.
        await using var b1 = await Peer.ConnectAsync(broadcast.Owner, timeout.Token);
        await using var b2 = await Peer.ConnectAsync(broadcast.Owner, timeout.Token);
        await using var b3 = await Peer.ConnectAsync(broadcast.Owner, timeout.Token);
        await using var v1 = await Peer.ConnectAsync(viewing.Owner, timeout.Token);
        await using var totalDenied = await Peer.ConnectAsync(other.Owner, timeout.Token);
        Accepted(b1);
        Accepted(b2);
        Full(b3);
        Accepted(v1);
        Full(totalDenied);
        Assert.Equal(3, fixture.State.NetworkPolicies.Relays.ReservedCount);
        await b1.QuitAsync(timeout.Token);
        await UntilAsync(() => fixture.State.DownstreamRelays.ActiveRelayCount == 2, timeout.Token);
        await using var viewDenied = await Peer.ConnectAsync(viewing.Owner, timeout.Token);
        Full(viewDenied);
        await using var otherAccepted = await Peer.ConnectAsync(other.Owner, timeout.Token);
        Accepted(otherAccepted);
        Assert.Equal(3, fixture.State.DownstreamRelays.ActiveRelayCount);
        // QUIT reception does not await the server's transport/session cleanup.
        await UntilAsync(() => broadcast.Owner.DownstreamSnapshot.CompletedConnectionCount == 2 &&
            viewing.Owner.DownstreamSnapshot.CompletedConnectionCount == 1, timeout.Token);
        Assert.Equal(1, broadcast.Owner.DownstreamSnapshot.ActiveConnectionCount);
        Assert.Equal(1, viewing.Owner.DownstreamSnapshot.ActiveConnectionCount);
    }

    [Fact]
    public async Task ConcurrentAdmissionAndConcurrentOwnerShutdownDoNotExceedOrLeakTheTotal()
    {
        using var timeout = TimeoutSource();
        await using var fixture = await CreateAsync(new() { MaxRelaysTotal = 1 });
        await using var broadcast = await fixture.BroadcastAsync(timeout.Token);
        await using var viewing = await fixture.ViewingAsync(timeout.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => broadcast.Owner.RunAsync(timeout.Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => viewing.Owner.RunAsync(timeout.Token));
        var peers = await Task.WhenAll(Enumerable.Range(0, 16).Select(i =>
            Peer.ConnectAsync(i % 2 == 0 ? broadcast.Owner : viewing.Owner, timeout.Token)));
        try
        {
            var admitted = Assert.Single(peers, p => p.Handshake.Outcome == PcpCompatibilityOutcome.Accepted);
            foreach (var peer in peers) if (!ReferenceEquals(peer, admitted)) Full(peer);
            Assert.Equal(1, fixture.State.NetworkPolicies.Relays.ReservedCount);
            var shutdown = fixture.Channels.DisposeAsync().AsTask();
            Assert.Same(shutdown, fixture.Channels.DisposeAsync().AsTask());
            var ownerShutdown = broadcast.Owner.DisposeAsync().AsTask();
            Assert.Same(ownerShutdown, broadcast.Owner.DisposeAsync().AsTask());
            await Task.WhenAll(shutdown, ownerShutdown);
            await broadcast.Run.WaitAsync(timeout.Token);
            await viewing.Run.WaitAsync(timeout.Token);
            Assert.True(broadcast.Owner.IsDisposed);
            Assert.True(viewing.Owner.IsDisposed);
            Assert.Equal(0, fixture.Channels.OwnerCount);
            await UntilAsync(() => fixture.State.NetworkPolicies.Relays.ReservedCount == 0, timeout.Token);
        }
        finally { foreach (var peer in peers) await peer.DisposeAsync(); }
    }

    [Fact]
    public async Task GenerationChangeRetiresOldAdmissionButKeepsOldTcpCapacityUntilCleanup()
    {
        using var timeout = TimeoutSource();
        await using var fixture = await CreateAsync(new() { MaxRelaysPerBroadcastChannel = 1 });
        var id = new ChannelId(Guid.NewGuid());
        await using var old = await fixture.BroadcastAsync(timeout.Token, id);
        await using var peer = await Peer.ConnectAsync(old.Owner, timeout.Token);
        Accepted(peer);
        await using var current = await fixture.BroadcastAsync(timeout.Token, id);
        Assert.NotEqual(old.Owner.SessionId, current.Owner.SessionId);
        Assert.True(old.Owner.IsRetired);
        Assert.Equal(1, old.Owner.DownstreamSnapshot.ActiveConnectionCount);
        await using var denied = await Peer.ConnectAsync(current.Owner, timeout.Token);
        Full(denied);
        Assert.Equal(1, fixture.State.DownstreamRelays.ActiveRelayCount);
        await peer.DisposeAsync(); // Actual remote TCP disconnect.
        await UntilAsync(() => fixture.State.DownstreamRelays.ActiveRelayCount == 0, timeout.Token);
        await using var replacement = await Peer.ConnectAsync(current.Owner, timeout.Token);
        Accepted(replacement);
        await old.Owner.DisposeAsync();
        await old.Run.WaitAsync(timeout.Token);
        Assert.Equal(1, fixture.State.DownstreamRelays.ActiveRelayCount);
        Assert.False(current.Owner.IsRetired);
    }

    [Fact]
    public async Task RetiringOwnerDoesNotPretendItsLiveConnectionHasClosed()
    {
        using var timeout = TimeoutSource();
        await using var fixture = await CreateAsync(new() { MaxRelaysTotal = 1 });
        await using var channel = await fixture.BroadcastAsync(timeout.Token);
        await using var peer = await Peer.ConnectAsync(channel.Owner, timeout.Token);
        Accepted(peer);
        channel.Owner.Retire();
        channel.Owner.Retire();
        Assert.Equal(1, fixture.State.DownstreamRelays.ActiveRelayCount);
        var completedBeforeDenied = channel.Owner.DownstreamSnapshot.CompletedConnectionCount;
        await using var denied = await Peer.ConnectAsync(channel.Owner, timeout.Token);
        Full(denied);
        await UntilAsync(() => channel.Owner.DownstreamSnapshot.CompletedConnectionCount > completedBeforeDenied,
            timeout.Token);
        Assert.Equal(completedBeforeDenied + 1, channel.Owner.DownstreamSnapshot.CompletedConnectionCount);
        Assert.Equal(1, channel.Owner.DownstreamSnapshot.ActiveConnectionCount);
        Assert.Equal(1, fixture.State.DownstreamRelays.ActiveRelayCount);
        Assert.Equal(1, fixture.State.NetworkPolicies.Relays.ReservedCount);
        await peer.QuitAsync(timeout.Token);
        await UntilAsync(() => channel.Owner.DownstreamSnapshot.ActiveConnectionCount == 0 &&
            fixture.State.DownstreamRelays.ActiveRelayCount == 0 &&
            fixture.State.NetworkPolicies.Relays.ReservedCount == 0, timeout.Token);
        Assert.Equal(completedBeforeDenied + 2, channel.Owner.DownstreamSnapshot.CompletedConnectionCount);
        Assert.Equal(0, channel.Owner.DownstreamSnapshot.ActiveConnectionCount);
        Assert.Equal(0, fixture.State.DownstreamRelays.ActiveRelayCount);
        Assert.Equal(0, fixture.State.NetworkPolicies.Relays.ReservedCount);
    }

    [Fact]
    public async Task SavedChangesLiveApplyToActiveOwnerWithoutBreakingExistingRelay()
    {
        using var timeout = TimeoutSource();
        await using var fixture = await CreateAsync(new() { MaxRelaysTotal = 1 });
        await using var channel = await fixture.BroadcastAsync(timeout.Token);
        await using var accepted = await Peer.ConnectAsync(channel.Owner, timeout.Token);
        Accepted(accepted);
        Assert.True((await fixture.State.Connection.SaveAsync(
            GenesisGuiNextConnectionInput.From(new() { MaxRelaysTotal = 0 }))).Saved);
        // 既存の接続は維持されたまま、新しいリレー受け入れは即時に拒否される。
        await using var denied = await Peer.ConnectAsync(channel.Owner, timeout.Token);
        Full(denied);
        Assert.Equal(1, fixture.State.NetworkPolicies.Relays.ReservedCount);
        await using var restarted = await LoadAsync(fixture.Store);
        await using var producer = new PcpRelayFanout();
        await using var next = await restarted.PublicChannels.PrepareBroadcastAsync(
            new(Guid.NewGuid()), new(Guid.NewGuid()), producer, new(IPAddress.Loopback, 0));
        var run = next.RunAsync(timeout.Token);
        await using var deniedOnRestart = await Peer.ConnectAsync(next, timeout.Token);
        Full(deniedOnRestart);
        await next.DisposeAsync();
        await run.WaitAsync(timeout.Token);
    }

    [Fact]
    public async Task ActualUpstreamPlaybackAndRecordingConsumersDoNotConsumeRelayReservations()
    {
        using var timeout = TimeoutSource();
        await using var fixture = await CreateAsync(new() { MaxRelaysTotal = 1 });
        await using var channel = await fixture.ViewingAsync(timeout.Token);
        var runtime = channel.Owner.ViewingRuntime!;
        Assert.Equal(0, fixture.State.DownstreamRelays.ActiveRelayCount);
        await using var playback = await runtime.AttachPlaybackConsumerAsync(new NoopPacketConsumer());
        Assert.NotNull(playback);
        await using var recording = await runtime.RecordingStreamSourceLeaseFactory.AcquireAsync(
            new RecordingSinkStartRequest(new RecordingCandidate(channel.Owner.ChannelId,
                new ChannelMetadataSnapshot(name: "in memory", streamExtension: "flv")), Guid.NewGuid()), timeout.Token);
        Assert.NotNull(recording);
        Assert.Equal(2, runtime.DownstreamFanout.ActiveChildCount);
        Assert.Equal(0, fixture.State.DownstreamRelays.ActiveRelayCount);
        await using var peer = await Peer.ConnectAsync(channel.Owner, timeout.Token);
        Accepted(peer);
        Assert.Equal(3, runtime.DownstreamFanout.ActiveChildCount);
        Assert.Equal(1, fixture.State.NetworkPolicies.Relays.ReservedCount);
        await peer.QuitAsync(timeout.Token);
        await UntilAsync(() => fixture.State.DownstreamRelays.ActiveRelayCount == 0, timeout.Token);
        Assert.Equal(2, runtime.DownstreamFanout.ActiveChildCount);
    }

    [Theory]
    [InlineData("wrong-channel")]
    [InlineData("bad-helo")]
    [InlineData("timeout")]
    [InlineData("disconnect")]
    [InlineData("cancel")]
    public async Task InterruptedOwnedNegotiationReleasesOnlyAfterActualCleanup(string mode)
    {
        using var timeout = TimeoutSource();
        using var ownerCancel = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        await using var fixture = await CreateAsync(new() { MaxRelaysTotal = 1 });
        await using var channel = await fixture.BroadcastAsync(ownerCancel.Token,
            childOptions: new() { HandshakeTimeout = mode == "timeout" ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromSeconds(5) });
        var id = mode == "wrong-channel" ? Guid.NewGuid() : channel.Owner.ChannelId.Value;
        await using var client = await new TcpTransportConnector(Transport).ConnectAsync(
            "127.0.0.1", channel.Owner.LocalEndPoint.Port, timeout.Token);
        var reader = PipeReader.Create(client.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
        try
        {
            await client.WriteAsync(Encoding.ASCII.GetBytes(
                $"GET /channel/{id:N} HTTP/1.0\r\nx-peercast-pcp:1\r\nx-peercast-pos:0\r\n\r\n"), timeout.Token);
            var response = await PcpHttpHeaderBlockReader.ReadResponseAsync(reader, 32768, timeout.Token);
            Assert.Equal(mode == "wrong-channel" ? 404 : 200, response.StatusCode);
            if (mode == "wrong-channel") Assert.Equal(0, fixture.State.DownstreamRelays.ActiveRelayCount);
            else
            {
                Assert.Equal(1, fixture.State.DownstreamRelays.ActiveRelayCount);
                if (mode == "bad-helo") await client.WriteAsync(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 }, timeout.Token);
                if (mode == "disconnect") await client.DisposeAsync();
                if (mode == "cancel") ownerCancel.Cancel();
            }
            await UntilAsync(() => channel.Owner.DownstreamSnapshot.CompletedConnectionCount == 1, timeout.Token);
            await UntilAsync(() => fixture.State.DownstreamRelays.ActiveRelayCount == 0, timeout.Token);
            Assert.Equal(0, channel.Owner.DownstreamSnapshot.ActiveConnectionCount);
            Assert.Equal(0, channel.Owner.DownstreamSnapshot.CleanupIncompleteCount);
        }
        finally { await reader.CompleteAsync(); }
    }

    [Fact]
    public async Task ApplicationShutdownClosesOwnersBeforeRetiringRegistryAndPolicy()
    {
        using var timeout = TimeoutSource();
        var fixture = await CreateAsync(new() { MaxRelaysTotal = 1 });
        await using var producer = new PcpRelayFanout();
        var owner = await fixture.Channels.PrepareBroadcastAsync(new(Guid.NewGuid()), new(Guid.NewGuid()),
            producer, new(IPAddress.Loopback, 0));
        var run = owner.RunAsync(timeout.Token);
        await using var peer = await Peer.ConnectAsync(owner, timeout.Token);
        Accepted(peer);
        await fixture.DisposeAsync();
        await run.WaitAsync(timeout.Token);
        Assert.True(owner.IsRetired);
        Assert.True(owner.IsDisposed);
        Assert.True(fixture.Channels.IsDisposed);
        Assert.Equal(0, fixture.Channels.OwnerCount);
        Assert.Equal(0, fixture.State.DownstreamRelays.ActiveRelayCount);
        Assert.Equal(0, fixture.State.NetworkPolicies.Relays.ReservedCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await fixture.Channels.PrepareBroadcastAsync(
            new(Guid.NewGuid()), new(Guid.NewGuid()), producer, new(IPAddress.Loopback, 0)));
        await using var stillProducerOwned = producer.AddChild(new NoopSink());
        Assert.Equal(1, producer.ActiveChildCount);
        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task ConcurrentPreparationAndShutdownLeaveNoUnownedPreparedRuntime()
    {
        await using var fixture = await CreateAsync();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 24).Select(async _ =>
        {
            await start.Task;
            try
            {
                return await fixture.Channels.PrepareViewingAsync(
                    new("prepared only", $"pcp://tracker.invalid:7144/channel/{Guid.NewGuid():N}"), new(IPAddress.Any, 0));
            }
            catch (ObjectDisposedException) { return null; }
        }).ToArray();
        var stop = Task.Run(async () => { await start.Task; await fixture.Channels.DisposeAsync(); });
        start.SetResult();
        var owners = await Task.WhenAll(tasks);
        await stop.WaitAsync(TimeSpan.FromSeconds(10));
        foreach (var owner in owners)
        {
            if (owner is null) continue;
            Assert.True(owner.IsDisposed);
            Assert.False(owner.ActivationStarted);
        }
        Assert.Equal(0, fixture.Channels.OwnerCount);
        Assert.Equal(0, fixture.State.DownstreamRelays.ActiveRelayCount);
    }

    [Fact]
    public async Task ShutdownImmediatelyClosesPreparedActivationBeforeAsyncCleanup()
    {
        await using var fixture = await CreateAsync();
        await using var fanout = new PcpRelayFanout();
        var owner = await fixture.Channels.PrepareBroadcastAsync(new(Guid.NewGuid()), new(Guid.NewGuid()),
            fanout, new(IPAddress.Any, 0));
        var cleanup = fixture.Channels.DisposeAsync();
        Assert.True(owner.IsRetired);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => owner.RunAsync());
        await cleanup;
        Assert.False(owner.ActivationStarted);
        Assert.True(owner.IsDisposed);
        Assert.Equal(0, fixture.Channels.OwnerCount);
    }

    [Fact]
    public async Task ReplacedPreparedOwnerCannotActivateAndOldCloseCannotRemoveNewBinding()
    {
        await using var fixture = await CreateAsync(new() { MaxRelaysTotal = 1 });
        await using var oldFanout = new PcpRelayFanout();
        await using var newFanout = new PcpRelayFanout();
        var id = new ChannelId(Guid.NewGuid());
        await using var old = await fixture.Channels.PrepareBroadcastAsync(id, new(Guid.NewGuid()), oldFanout, new(IPAddress.Any, 0));
        await using var current = await fixture.Channels.PrepareBroadcastAsync(id, new(Guid.NewGuid()), newFanout, new(IPAddress.Any, 0));
        Assert.True(old.IsRetired);
        Assert.False(old.IsCurrent);
        Assert.False(old.CanReserve);
        Assert.True(current.IsCurrent);
        Assert.True(current.CanReserve);
        await Assert.ThrowsAsync<InvalidOperationException>(() => old.RunAsync());
        Assert.False(old.ActivationStarted);
        await old.DisposeAsync();
        Assert.Equal(1, fixture.Channels.OwnerCount);
        Assert.False(current.IsRetired);
        Assert.True(current.TryReserve(1, out var lease));
        Assert.False(current.CanReserve);
        lease.Dispose();
        Assert.True(current.CanReserve);
    }

    [Fact]
    public async Task ProducerFanoutResourceBoundStillRejectsAndReleasesTheProvisionalPolicySlot()
    {
        using var timeout = TimeoutSource();
        await using var fixture = await CreateAsync(new() { MaxRelaysTotal = 1 });
        await using var fanout = new PcpRelayFanout(new() { MaxChildren = 1 });
        await using var localConsumer = fanout.AddChild(new NoopSink());
        await using var owner = await fixture.Channels.PrepareBroadcastAsync(new(Guid.NewGuid()), new(Guid.NewGuid()),
            fanout, new(IPAddress.Loopback, 0));
        var run = owner.RunAsync(timeout.Token);
        await using var peer = await Peer.ConnectAsync(owner, timeout.Token);
        Full(peer);
        await UntilAsync(() => owner.DownstreamSnapshot.CompletedConnectionCount == 1, timeout.Token);
        Assert.Equal(0, fixture.State.DownstreamRelays.ActiveRelayCount);
        Assert.Equal(1, fanout.ActiveChildCount);
        await owner.DisposeAsync();
        await run.WaitAsync(timeout.Token);
    }

    [Fact]
    public async Task DisposalDoesNotRequireTheCallingGuiSynchronizationContextToBePumped()
    {
        await using var fixture = await CreateAsync();
        await using var fanout = new PcpRelayFanout();
        var owner = await fixture.Channels.PrepareBroadcastAsync(new(Guid.NewGuid()), new(Guid.NewGuid()),
            fanout, new(IPAddress.Any, 0));
        var previous = SynchronizationContext.Current;
        var context = new NeverPumpedContext();
        Task cleanup;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            cleanup = fixture.Channels.DisposeAsync().AsTask();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        await cleanup.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, context.PostCount);
        Assert.True(owner.IsDisposed);
    }

    private static void Accepted(Peer peer)
    {
        Assert.Equal(200, peer.Handshake.HttpStatusCode);
        Assert.Equal(PcpCompatibilityOutcome.Accepted, peer.Handshake.Outcome);
    }
    private static void Full(Peer peer)
    {
        Assert.Equal(503, peer.Handshake.HttpStatusCode);
        Assert.Equal(PcpCompatibilityOutcome.RelayFull, peer.Handshake.Outcome);
        Assert.Equal(PcpKnownAtomNames.Oleh, peer.Handshake.ReceivedAtomNames[0]);
        Assert.Equal(PcpKnownAtomNames.Quit, peer.Handshake.TerminalAtom);
    }
    private sealed class NeverPumpedContext : SynchronizationContext
    {
        private int _posts;
        public int PostCount => Volatile.Read(ref _posts);
        public override void Post(SendOrPostCallback d, object? state) => Interlocked.Increment(ref _posts);
    }

    private sealed class NoopSink : IPcpRelayChildSink
    {
        public ValueTask SendAsync(PcpRelayPacket packet, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
    private sealed class NoopPacketConsumer : IPcpChannelPacketConsumer
    {
        public ValueTask ConsumeAsync(PcpChannelPacket packet, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
