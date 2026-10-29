using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Policies;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.App.Tests;

/// <summary>Managed loopback protocol tests; no GUI or public listener startup.</summary>
public sealed class GenesisDownstreamRelayAdmissionIntegrationTests
{
    private static readonly TcpTransportOptions Transport = new()
    {
        ConnectTimeout = TimeSpan.FromSeconds(2),
        MaxConcurrentConnections = 32,
    };
    private static CancellationTokenSource TimeoutSource() => new(TimeSpan.FromSeconds(20));

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task StandaloneOptionalAdmissionPreservesUnlimitedAnd503RelayFull(int limit)
    {
        using var timeout = TimeoutSource();
        using var policy = new RelayAdmissionPolicy(limit switch
        {
            0 => new(MaxRelaysTotal: 0),
            1 => new(MaxRelaysPerBroadcastChannel: 0),
            2 => new(MaxRelaysPerViewingChannel: 0),
            _ => new(),
        });
        using var registry = new GenesisDownstreamRelayOwnerRegistry(policy);
        var id = new ChannelId(Guid.NewGuid());
        await using var fanout = new PcpRelayFanout();
        using var owner = registry.Register(id, new(Guid.NewGuid()),
            limit == 1 ? RelayChannelKind.Broadcast : RelayChannelKind.Viewing, fanout);
        await using var server = new PcpDownstreamRelayServer(new(IPAddress.Loopback, 0),
            fanout, id, Guid.NewGuid(), Transport, admission: owner);
        var run = server.RunAsync(timeout.Token);
        await using var peer = await Peer.ConnectAsync(server.LocalEndPoint, id, timeout.Token);
        if (limit < 0)
        {
            AssertAccepted(peer);
            Assert.Equal(1, registry.ActiveRelayCount);
            await peer.QuitAsync(timeout.Token);
        }
        else AssertRelayFull(peer);
        await UntilAsync(() => server.Snapshot.CompletedConnectionCount == 1, timeout.Token);
        await UntilAsync(() => registry.ActiveRelayCount == 0, timeout.Token);
        Assert.Equal(0, policy.ReservedCount);
        await server.StopAsync(timeout.Token);
        await run.WaitAsync(timeout.Token);
    }

    [Fact]
    public async Task PublishingRegistrationUsesSharedBroadcastPolicyAndRetainsOldLeases()
    {
        await using var fixture = await PublicChannelTestFixture.CreateAsync(new() { MaxRelaysTotal = 1, MaxRelaysPerBroadcastChannel = 1 });
        using var timeout = TimeoutSource();
        var factory = new SystemNovaObsPublishingRuntimeFactory(fixture.State);
        var id = new ChannelId(Guid.NewGuid());
        await using var fanout = new PcpRelayFanout();
        using var owner = factory.Register(id, new(Guid.NewGuid()), fanout);
        Assert.Equal(RelayChannelKind.Broadcast, owner.Kind);
        Assert.Same(owner, fixture.State.DownstreamRelays.Resolve(id, fanout));
        Assert.Equal(0, fixture.State.PublicChannels.OwnerCount); // No second server/composition owner.
        await using var server = new PcpDownstreamRelayServer(new(IPAddress.Loopback, 0),
            fanout, id, Guid.NewGuid(), Transport, admission: owner, acceptRemoteAddress: address => IPAddress.IsLoopback(address!));
        var run = server.RunAsync(timeout.Token);
        await using var peer = await Peer.ConnectAsync(server.LocalEndPoint, id, timeout.Token);
        AssertAccepted(peer);
        owner.Dispose();
        Assert.Equal(1, fixture.State.DownstreamRelays.ActiveRelayCount);
        Assert.False(owner.TryReserve(long.MaxValue, out _));
        await using var nextFanout = new PcpRelayFanout();
        using var next = factory.Register(id, new(Guid.NewGuid()), nextFanout);
        Assert.False(next.TryReserve(long.MaxValue, out _)); // Retired TCP still consumes total/channel capacity.
        await peer.QuitAsync(timeout.Token);
        await UntilAsync(() => fixture.State.DownstreamRelays.ActiveRelayCount == 0, timeout.Token);
        Assert.True(next.TryReserve(long.MaxValue, out var lease));
        lease!.Dispose();
        await server.StopAsync(timeout.Token);
        await run;
    }

    [Fact]
    public async Task SharedListenerSharesTotalAndPerRoleLimitsWithoutDisconnectingExistingRelays()
    {
        using var timeout = TimeoutSource();
        using var policy = new RelayAdmissionPolicy(new(3, 2, 1));
        using var registry = new GenesisDownstreamRelayOwnerRegistry(policy);
        var broadcastId = new ChannelId(Guid.NewGuid());
        var viewingId = new ChannelId(Guid.NewGuid());
        var otherId = new ChannelId(Guid.NewGuid());
        await using var broadcastFanout = new PcpRelayFanout();
        await using var viewingFanout = new PcpRelayFanout();
        await using var otherFanout = new PcpRelayFanout();
        using var broadcast = registry.Register(broadcastId, new(Guid.NewGuid()), RelayChannelKind.Broadcast, broadcastFanout);
        using var viewing = registry.Register(viewingId, new(Guid.NewGuid()), RelayChannelKind.Viewing, viewingFanout);
        using var other = registry.Register(otherId, new(Guid.NewGuid()), RelayChannelKind.Viewing, otherFanout);
        var routes = new PcpDownstreamRelayRouteRegistry();
        await using var route1 = routes.Register(broadcastId, broadcastFanout);
        await using var route2 = routes.Register(viewingId, viewingFanout);
        await using var route3 = routes.Register(otherId, otherFanout);
        await using var listener = new PcpSharedDownstreamRelayListener(new(IPAddress.Loopback, 0), routes,
            new PcpDownstreamChildSessionRoutedConnectionHandler(Guid.NewGuid(), resolveAdmission: registry.Resolve), Transport);
        var run = listener.RunAsync(timeout.Token);
        await using var b1 = await Peer.ConnectAsync(listener.LocalEndPoint, broadcastId, timeout.Token);
        await using var b2 = await Peer.ConnectAsync(listener.LocalEndPoint, broadcastId, timeout.Token);
        await using var deniedBroadcast = await Peer.ConnectAsync(listener.LocalEndPoint, broadcastId, timeout.Token);
        await using var v1 = await Peer.ConnectAsync(listener.LocalEndPoint, viewingId, timeout.Token);
        await using var deniedTotal = await Peer.ConnectAsync(listener.LocalEndPoint, otherId, timeout.Token);
        AssertAccepted(b1);
        AssertAccepted(b2);
        AssertAccepted(v1);
        AssertRelayFull(deniedBroadcast);
        AssertRelayFull(deniedTotal);
        Assert.Equal(3, registry.ActiveRelayCount);
        Assert.Equal(2, broadcastFanout.ActiveChildCount);
        Assert.Equal(1, viewingFanout.ActiveChildCount);
        await b1.QuitAsync(timeout.Token);
        await UntilAsync(() => registry.ActiveRelayCount == 2, timeout.Token);
        await using var deniedViewing = await Peer.ConnectAsync(listener.LocalEndPoint, viewingId, timeout.Token);
        AssertRelayFull(deniedViewing);
        await using var admittedOther = await Peer.ConnectAsync(listener.LocalEndPoint, otherId, timeout.Token);
        AssertAccepted(admittedOther);
        Assert.Equal(3, registry.ActiveRelayCount);
        await listener.StopAsync(timeout.Token);
        await run.WaitAsync(timeout.Token);
        await UntilAsync(() => registry.ActiveRelayCount == 0, timeout.Token);
    }

    [Fact]
    public async Task ConcurrentProtocolAdmissionsAcrossChannelsCannotExceedTotal()
    {
        using var timeout = TimeoutSource();
        using var policy = new RelayAdmissionPolicy(new(MaxRelaysTotal: 1));
        using var registry = new GenesisDownstreamRelayOwnerRegistry(policy);
        var id1 = new ChannelId(Guid.NewGuid());
        var id2 = new ChannelId(Guid.NewGuid());
        await using var fanout1 = new PcpRelayFanout();
        await using var fanout2 = new PcpRelayFanout();
        using var owner1 = registry.Register(id1, new(Guid.NewGuid()), RelayChannelKind.Broadcast, fanout1);
        using var owner2 = registry.Register(id2, new(Guid.NewGuid()), RelayChannelKind.Viewing, fanout2);
        var routes = new PcpDownstreamRelayRouteRegistry();
        await using var route1 = routes.Register(id1, fanout1);
        await using var route2 = routes.Register(id2, fanout2);
        await using var listener = new PcpSharedDownstreamRelayListener(new(IPAddress.Loopback, 0), routes,
            new PcpDownstreamChildSessionRoutedConnectionHandler(Guid.NewGuid(), resolveAdmission: registry.Resolve), Transport);
        var run = listener.RunAsync(timeout.Token);
        var peers = await Task.WhenAll(Enumerable.Range(0, 12).Select(i =>
            Peer.ConnectAsync(listener.LocalEndPoint, i % 2 == 0 ? id1 : id2, timeout.Token)));
        try
        {
            var accepted = Assert.Single(peers, peer => peer.Handshake.Outcome == PcpCompatibilityOutcome.Accepted);
            foreach (var peer in peers)
                if (!ReferenceEquals(peer, accepted)) AssertRelayFull(peer);
            Assert.Equal(1, registry.ActiveRelayCount);
            await accepted.QuitAsync(timeout.Token);
            await UntilAsync(() => registry.ActiveRelayCount == 0, timeout.Token);
            await using var replacement = await Peer.ConnectAsync(listener.LocalEndPoint, id2, timeout.Token);
            AssertAccepted(replacement);
        }
        finally { foreach (var peer in peers) await peer.DisposeAsync(); }
        await listener.StopAsync(timeout.Token);
        await run.WaitAsync(timeout.Token);
        await UntilAsync(() => registry.ActiveRelayCount == 0, timeout.Token);
    }

    [Fact]
    public async Task OldLiveGenerationKeepsCapacityUntilItsActualDisconnect()
    {
        using var timeout = TimeoutSource();
        using var policy = new RelayAdmissionPolicy(new(MaxRelaysPerViewingChannel: 1));
        using var registry = new GenesisDownstreamRelayOwnerRegistry(policy);
        var id = new ChannelId(Guid.NewGuid());
        await using var oldFanout = new PcpRelayFanout();
        await using var newFanout = new PcpRelayFanout();
        using var oldOwner = registry.Register(id, new(Guid.NewGuid()), RelayChannelKind.Viewing, oldFanout);
        await using var oldServer = new PcpDownstreamRelayServer(new(IPAddress.Loopback, 0), oldFanout, id,
            Guid.NewGuid(), Transport, admission: oldOwner);
        var oldRun = oldServer.RunAsync(timeout.Token);
        await using var oldPeer = await Peer.ConnectAsync(oldServer.LocalEndPoint, id, timeout.Token);
        AssertAccepted(oldPeer);
        using var newOwner = registry.Register(id, new(Guid.NewGuid()), RelayChannelKind.Viewing, newFanout);
        oldOwner.Dispose();
        await using var newServer = new PcpDownstreamRelayServer(new(IPAddress.Loopback, 0), newFanout, id,
            Guid.NewGuid(), Transport, admission: newOwner);
        var newRun = newServer.RunAsync(timeout.Token);
        await using var denied = await Peer.ConnectAsync(newServer.LocalEndPoint, id, timeout.Token);
        AssertRelayFull(denied);
        Assert.Equal(1, oldFanout.ActiveChildCount);
        Assert.Equal(1, registry.ActiveRelayCount);
        await oldPeer.DisposeAsync(); // Remote disconnect, not just cancellation.
        await UntilAsync(() => registry.ActiveRelayCount == 0, timeout.Token);
        await using var replacement = await Peer.ConnectAsync(newServer.LocalEndPoint, id, timeout.Token);
        AssertAccepted(replacement);
        oldOwner.Dispose();
        Assert.Equal(1, registry.ActiveRelayCount);
        registry.Dispose(); // Does not close replacement or erase occupied capacity.
        Assert.Equal(1, newFanout.ActiveChildCount);
        Assert.Equal(1, policy.ReservedCount);
        await using var deniedAfterDispose = await Peer.ConnectAsync(newServer.LocalEndPoint, id, timeout.Token);
        AssertRelayFull(deniedAfterDispose);
        await newServer.StopAsync(timeout.Token);
        await newRun.WaitAsync(timeout.Token);
        await oldServer.StopAsync(timeout.Token);
        await oldRun.WaitAsync(timeout.Token);
        await UntilAsync(() => registry.ActiveRelayCount == 0, timeout.Token);
    }

    [Fact]
    public async Task LocalPlaybackRecordingAndUnrelatedTcpDoNotConsumeRelayCapacity()
    {
        using var timeout = TimeoutSource();
        using var policy = new RelayAdmissionPolicy(new(MaxRelaysTotal: 1));
        using var registry = new GenesisDownstreamRelayOwnerRegistry(policy);
        var id = new ChannelId(Guid.NewGuid());
        var sessionId = new ChannelSessionId(Guid.NewGuid());
        await using var fanout = new PcpRelayFanout(new() { MaxChildren = 3 });
        using var owner = registry.Register(id, sessionId, RelayChannelKind.Viewing, fanout);
        await using var playback = fanout.AddChild(new NoopSink());
        var recordingFactory = new PcpRelayFanoutRecordingStreamSourceLeaseFactory(id, sessionId, fanout);
        recordingFactory.StartAccepting();
        await using var recording = await recordingFactory.AcquireAsync(new RecordingSinkStartRequest(
            new RecordingCandidate(id, new ChannelMetadataSnapshot(name: "test", streamExtension: "flv")),
            Guid.NewGuid()), timeout.Token);
        Assert.NotNull(recording);
        await using var server = new PcpDownstreamRelayServer(new(IPAddress.Loopback, 0), fanout, id,
            Guid.NewGuid(), Transport, admission: owner);
        var run = server.RunAsync(timeout.Token);
        Assert.Equal(2, fanout.ActiveChildCount);
        Assert.Equal(0, registry.ActiveRelayCount);
        // A connector/upstream transport has no admission role on its own.
        await using var unrelatedListener = new TcpTransportListener(new(IPAddress.Loopback, 0), Transport);
        unrelatedListener.Start();
        var accept = unrelatedListener.AcceptAsync(timeout.Token);
        await using var unrelatedClient = await new TcpTransportConnector(Transport).ConnectAsync(
            "127.0.0.1", unrelatedListener.LocalEndPoint.Port, timeout.Token);
        await using var unrelatedServer = await accept;
        Assert.Equal(0, registry.ActiveRelayCount);
        await using var peer = await Peer.ConnectAsync(server.LocalEndPoint, id, timeout.Token);
        AssertAccepted(peer);
        Assert.Equal(3, fanout.ActiveChildCount);
        Assert.Equal(1, registry.ActiveRelayCount);
        await using var policyDenied = await Peer.ConnectAsync(server.LocalEndPoint, id, timeout.Token);
        AssertRelayFull(policyDenied);
        await peer.QuitAsync(timeout.Token);
        await UntilAsync(() => registry.ActiveRelayCount == 0, timeout.Token);
        await using var thirdLocal = fanout.AddChild(new NoopSink());
        await using var fanoutDenied = await Peer.ConnectAsync(server.LocalEndPoint, id, timeout.Token);
        AssertRelayFull(fanoutDenied);
        await UntilAsync(() => server.Snapshot.CompletedConnectionCount == 3, timeout.Token);
        Assert.Equal(0, registry.ActiveRelayCount);
        Assert.Equal(3, fanout.ActiveChildCount);
        await server.StopAsync(timeout.Token);
        await run.WaitAsync(timeout.Token);
        await recordingFactory.CompleteAsync();
    }

    [Theory]
    [InlineData("wrong-channel")]
    [InlineData("bad-method")]
    [InlineData("bad-helo")]
    [InlineData("timeout")]
    [InlineData("disconnect")]
    [InlineData("cancel")]
    public async Task ValidationAndInterruptedNegotiationCannotLeakCapacity(string mode)
    {
        using var timeout = TimeoutSource();
        using var policy = new RelayAdmissionPolicy(new(MaxRelaysTotal: 1));
        using var registry = new GenesisDownstreamRelayOwnerRegistry(policy);
        var id = new ChannelId(Guid.NewGuid());
        await using var fanout = new PcpRelayFanout();
        using var owner = registry.Register(id, new(Guid.NewGuid()), RelayChannelKind.Viewing, fanout);
        await using var listener = new TcpTransportListener(new(IPAddress.Loopback, 0), Transport);
        listener.Start();
        var accept = listener.AcceptAsync(timeout.Token);
        await using var client = await new TcpTransportConnector(Transport).ConnectAsync(
            "127.0.0.1", listener.LocalEndPoint.Port, timeout.Token);
        await using var connection = await accept;
        using var sessionCancel = new CancellationTokenSource();
        await using var session = new PcpDownstreamChildSession(connection, fanout, id, Guid.NewGuid(),
            new() { HandshakeTimeout = mode == "timeout" ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromSeconds(5) },
            packetWriter: null, admission: owner);
        var run = session.RunAsync(sessionCancel.Token);
        var requestId = mode == "wrong-channel" ? Guid.NewGuid() : id.Value;
        var method = mode == "bad-method" ? "POST" : "GET";
        await client.WriteAsync(Encoding.ASCII.GetBytes(
            method + " /channel/" + requestId.ToString("N") + " HTTP/1.0\r\nx-peercast-pcp: 1\r\nx-peercast-pos: 0\r\n\r\n"), timeout.Token);
        var responseReader = PipeReader.Create(client.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
        PcpHttpResponseHeader response;
        try { response = await PcpHttpHeaderBlockReader.ReadResponseAsync(responseReader, 32768, timeout.Token); }
        finally { await responseReader.CompleteAsync(); }
        if (mode is "wrong-channel" or "bad-method")
        {
            Assert.Equal(mode == "wrong-channel" ? 404 : 400, response.StatusCode);
            Assert.Equal(0, registry.ActiveRelayCount);
        }
        else
        {
            Assert.Equal(200, response.StatusCode);
            Assert.True(registry.ContainsConnection(connection.ConnectionId));
            if (mode == "bad-helo") await client.WriteAsync(EncodeQuit(), timeout.Token);
            if (mode == "disconnect") await client.DisposeAsync();
            if (mode == "cancel") sessionCancel.Cancel();
        }
        var result = await run.WaitAsync(timeout.Token);
        Assert.False(result.HandshakeAccepted);
        Assert.Equal(mode switch
        {
            "timeout" => PcpDownstreamChildEndReason.HandshakeTimedOut,
            "disconnect" => PcpDownstreamChildEndReason.ConnectionEnded,
            "cancel" => PcpDownstreamChildEndReason.Stopped,
            _ => PcpDownstreamChildEndReason.ProtocolRejected,
        }, result.EndReason);
        Assert.True(await session.AdmissionCleanupCompletion.WaitAsync(timeout.Token));
        Assert.True(connection.CleanupCompletion.IsCompletedSuccessfully);
        Assert.Equal(0, policy.ReservedCount);
        Assert.Equal(0, registry.ActiveRelayCount);
    }

    [Fact]
    public async Task BorrowedSessionReturningDoesNotReleaseBeforeListenerOwnedTransportCleanup()
    {
        using var timeout = TimeoutSource();
        using var policy = new RelayAdmissionPolicy(new(MaxRelaysTotal: 1));
        using var registry = new GenesisDownstreamRelayOwnerRegistry(policy);
        var id = new ChannelId(Guid.NewGuid());
        await using var fanout = new PcpRelayFanout();
        using var owner = registry.Register(id, new(Guid.NewGuid()), RelayChannelKind.Viewing, fanout);
        await using var listener = new TcpTransportListener(new(IPAddress.Loopback, 0), Transport);
        listener.Start();
        var accept = listener.AcceptAsync(timeout.Token);
        var peerTask = Peer.ConnectAsync(listener.LocalEndPoint, id, timeout.Token);
        await using var connection = await accept;
        var reader = PipeReader.Create(connection.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
        try
        {
            var validation = await PcpDownstreamRelayRequestReader.ReadAsync(reader, 32768, 1, timeout.Token);
            Assert.NotNull(validation.Request);
            var handler = new PcpDownstreamChildSessionRoutedConnectionHandler(Guid.NewGuid(), resolveAdmission: registry.Resolve);
            var run = handler.RunAsync(connection, reader, validation.Request, fanout, timeout.Token).AsTask();
            await using var peer = await peerTask;
            AssertAccepted(peer);
            await peer.QuitAsync(timeout.Token);
            var result = await run.WaitAsync(timeout.Token);
            Assert.Equal(PcpDownstreamChildEndReason.QuitReceived, result.EndReason);
            Assert.True(result.CleanupCompleted); // Child cleanup is distinct from borrowed TCP cleanup.
            Assert.False(connection.IsDisposed);
            Assert.False(connection.CleanupCompletion.IsCompleted);
            Assert.Equal(1, registry.ActiveRelayCount);
            Assert.False(owner.TryReserve(999, out _));
            await reader.CompleteAsync();
            await connection.DisposeAsync();
            await UntilAsync(() => registry.ActiveRelayCount == 0, timeout.Token);
            await connection.DisposeAsync();
            Assert.Equal(0, policy.ReservedCount);
        }
        finally { await reader.CompleteAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateChildCallbackRetainsLeaseBeyondFiniteSessionShutdown(bool cancel)
    {
        using var timeout = TimeoutSource();
        using var policy = new RelayAdmissionPolicy(new(MaxRelaysTotal: 1));
        using var registry = new GenesisDownstreamRelayOwnerRegistry(policy);
        var id = new ChannelId(Guid.NewGuid());
        await using var fanout = new PcpRelayFanout(new() { MaxChildren = 1, ChildStopTimeout = TimeSpan.FromMilliseconds(50) });
        using var owner = registry.Register(id, new(Guid.NewGuid()), RelayChannelKind.Viewing, fanout);
        await using var listener = new TcpTransportListener(new(IPAddress.Loopback, 0), Transport);
        listener.Start();
        var accept = listener.AcceptAsync(timeout.Token);
        var peerTask = Peer.ConnectAsync(listener.LocalEndPoint, id, timeout.Token);
        await using var connection = await accept;
        var writer = new UncooperativeWriter();
        await using var session = new PcpDownstreamChildSession(connection, fanout, id, Guid.NewGuid(),
            options: null, writer, owner);
        using var sessionCancel = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var run = session.RunAsync(sessionCancel.Token);
        try
        {
            await using var peer = await peerTask;
            AssertAccepted(peer);
            await fanout.ConsumeAsync(new PcpChannelPacket(id, owner.SessionId, 0, 1, 100, 100,
                new byte[] { 1 }, discontinuity: null), timeout.Token);
            await writer.Entered.Task.WaitAsync(timeout.Token);
            if (cancel) sessionCancel.Cancel();
            else await peer.QuitAsync(timeout.Token);
            var result = await run.WaitAsync(timeout.Token);
            Assert.False(result.CleanupCompleted);
            Assert.True(connection.CleanupCompletion.IsCompletedSuccessfully);
            Assert.False(session.AdmissionCleanupCompletion.IsCompleted);
            Assert.Equal(1, registry.ActiveRelayCount);
            Assert.False(owner.TryReserve(999, out _));
            Assert.Equal(0, fanout.ActiveChildCount);
            writer.Release.TrySetResult();
            Assert.True(await session.AdmissionCleanupCompletion.WaitAsync(timeout.Token));
            Assert.Equal(0, registry.ActiveRelayCount);
            Assert.True(owner.TryReserve(999, out var next));
            next.Dispose();
        }
        finally { writer.Release.TrySetResult(); }
    }

    [Fact]
    public async Task AbsentRegistryKeepsLegacyAcceptanceAndMismatchedAdmissionIsRejected()
    {
        using var timeout = TimeoutSource();
        using var policy = new RelayAdmissionPolicy(new(MaxRelaysTotal: 0));
        using var registry = new GenesisDownstreamRelayOwnerRegistry(policy);
        var id = new ChannelId(Guid.NewGuid());
        await using var fanout = new PcpRelayFanout();
        using var wrongOwner = registry.Register(new(Guid.NewGuid()), new(Guid.NewGuid()), RelayChannelKind.Viewing);
        Assert.Throws<ArgumentException>(() => new PcpDownstreamRelayServer(new(IPAddress.Loopback, 0),
            fanout, id, Guid.NewGuid(), admission: wrongOwner));
        await using var server = new PcpDownstreamRelayServer(new(IPAddress.Loopback, 0), fanout, id, Guid.NewGuid(), Transport);
        var run = server.RunAsync(timeout.Token);
        await using var peer = await Peer.ConnectAsync(server.LocalEndPoint, id, timeout.Token);
        AssertAccepted(peer);
        Assert.Equal(0, registry.ActiveRelayCount);
        await server.StopAsync(timeout.Token);
        await run.WaitAsync(timeout.Token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnclassifiedRouteKeepsUnlimitedLegacyButFinitePolicyFailsClosed(bool finite)
    {
        using var timeout = TimeoutSource();
        using var policy = new RelayAdmissionPolicy(new(MaxRelaysTotal: finite ? 1 : null));
        using var registry = new GenesisDownstreamRelayOwnerRegistry(policy);
        var id = new ChannelId(Guid.NewGuid());
        await using var fanout = new PcpRelayFanout();
        var routes = new PcpDownstreamRelayRouteRegistry();
        await using var route = routes.Register(id, fanout);
        await using var listener = new PcpSharedDownstreamRelayListener(new(IPAddress.Loopback, 0), routes,
            new PcpDownstreamChildSessionRoutedConnectionHandler(Guid.NewGuid(), resolveAdmission: registry.Resolve), Transport);
        var run = listener.RunAsync(timeout.Token);
        await using var peer = await Peer.ConnectAsync(listener.LocalEndPoint, id, timeout.Token);
        if (finite) AssertRelayFull(peer);
        else AssertAccepted(peer);
        Assert.Equal(0, registry.ActiveRelayCount);
        await listener.StopAsync(timeout.Token);
        await run.WaitAsync(timeout.Token);
    }

    private static void AssertAccepted(Peer peer)
    {
        Assert.Equal(PcpCompatibilityOutcome.Accepted, peer.Handshake.Outcome);
        Assert.Equal(200, peer.Handshake.HttpStatusCode);
    }

    private static void AssertRelayFull(Peer peer)
    {
        Assert.Equal(PcpCompatibilityOutcome.RelayFull, peer.Handshake.Outcome);
        Assert.Equal(503, peer.Handshake.HttpStatusCode);
        Assert.Equal(PcpKnownAtomNames.Oleh, peer.Handshake.ReceivedAtomNames[0]);
        Assert.Equal(PcpKnownAtomNames.Quit, peer.Handshake.TerminalAtom);
    }

    private static async Task UntilAsync(Func<bool> predicate, CancellationToken token)
    {
        while (!predicate()) await Task.Delay(10, token);
    }

    private static byte[] EncodeQuit()
    {
        var writer = new ArrayBufferWriter<byte>();
        PcpAtomTreeCodec.Write(writer, PcpAtom.Leaf(PcpKnownAtomNames.Quit, PcpWireValueCodec.EncodeUInt32(0)));
        return writer.WrittenSpan.ToArray();
    }

    private sealed class NoopSink : IPcpRelayChildSink
    {
        public ValueTask SendAsync(PcpRelayPacket packet, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class UncooperativeWriter : IPcpDownstreamPacketWriter
    {
        private int _calls;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                Entered.TrySetResult();
                await Release.Task;
                return;
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private sealed class Peer(TcpTransportConnection connection, PipeReader reader, PcpRelayHandshakeResult handshake) : IAsyncDisposable
    {
        private int _disposed;
        public PcpRelayHandshakeResult Handshake { get; } = handshake;
        public ValueTask QuitAsync(CancellationToken token) => connection.WriteAsync(EncodeQuit(), token);
        public static async Task<Peer> ConnectAsync(IPEndPoint endpoint, ChannelId channelId, CancellationToken token)
        {
            var connection = await new TcpTransportConnector(Transport).ConnectAsync(endpoint.Address.ToString(), endpoint.Port, token);
            var reader = PipeReader.Create(connection.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
            try
            {
                var result = await PcpRelayHandshakeClient.NegotiateAsync(connection, reader,
                    endpoint.Address.ToString(), endpoint.Port, channelId.Value, Guid.NewGuid(), 0, 1,
                    new PcpRelayHandshakeSettings(32768, 65536, 16, "PeerCastNova/Test", PcpHeloCodec.LegacyServantVersion), token);
                return new Peer(connection, reader, result);
            }
            catch { await reader.CompleteAsync(); await connection.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { await reader.CompleteAsync(); }
            finally { await connection.DisposeAsync(); }
        }
    }
}
