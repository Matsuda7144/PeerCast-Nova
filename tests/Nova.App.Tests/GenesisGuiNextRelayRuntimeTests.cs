using System.Net;
using System.Net.Sockets;
using PeerCast.Nova.Network.Pcp;
using static PeerCast.Nova.App.Tests.PublicChannelTestFixture;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextRelayRuntimeTests
{
    [Fact]
    public async Task GuiCommandRunsRealViewingCoordinatorOnlyAfterExplicitStartAndReceiving()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var fixture = await CreateAsync(new() { ListenPort = 7144 });
        await using var upstream = new RelayActivationTestUpstream(timeout.Token);
        var playback = new FakePlaybackSessionController();
        var model = new GenesisMainViewModel(playback) { RelayActivation = fixture.State.RelayActivation };
        Assert.Equal(0, fixture.Channels.OwnerCount);
        Assert.Null(model.RelayActivation.CurrentOwner); // Neither a default bind nor a 7144 plan exists.
        Assert.Equal(GenesisRelayActivationState.Stopped, model.RelaySnapshot.State);
        var notifications = 0;
        model.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(model.RelaySnapshot)) Interlocked.Increment(ref notifications); };
        var start = GenesisGuiNextRelayCommandDispatch.StartAsync(model, upstream.Row, timeout.Token);
        await UntilAsync(() => model.RelaySnapshot.ListenerBound, timeout.Token);
        var owner = model.RelayActivation.CurrentOwner!;
        Assert.Equal(PeerCast.Nova.Network.Policies.RelayChannelKind.Viewing, owner.Kind);
        Assert.Equal(upstream.ChannelId, owner.ChannelId);
        Assert.Equal(owner.SessionId, model.RelaySnapshot.SessionId);
        Assert.Equal(IPAddress.Loopback, owner.RequestedEndPoint.Address);
        Assert.Equal(0, owner.RequestedEndPoint.Port);
        Assert.Equal(GenesisRelayActivationState.Starting, model.RelaySnapshot.State);
        Assert.False(model.RelaySnapshot.ReceivingReady);
        await UntilAsync(() => model.RelaySnapshot.UpstreamReady, timeout.Token);
        Assert.Equal(GenesisRelayActivationState.Starting, model.RelaySnapshot.State);
        Assert.False(start.IsCompleted);
        Assert.Equal(owner.LocalEndPoint.ToString(), model.RelaySnapshot.EndPoint);
        await upstream.SendAsync(timeout.Token);
        Assert.True(await start.WaitAsync(timeout.Token));
        Assert.True(owner.ActivationStarted);
        Assert.True(model.RelaySnapshot.ReceivingReady);
        Assert.Equal(GenesisRelayActivationState.Running, model.RelaySnapshot.State);
        Assert.False(await GenesisGuiNextRelayCommandDispatch.StartAsync(model, upstream.Row));
        Assert.Equal(GenesisRelayActivationError.AlreadyRunning, model.RelaySnapshot.Error);
        Assert.Same(owner, model.RelayActivation.CurrentOwner);
        Assert.Equal(PlaybackSessionState.Stopped, model.PlaybackState);
        var endpoint = owner.LocalEndPoint;
        await using var peer = await Peer.ConnectAsync(owner, timeout.Token);
        Assert.Equal(PcpCompatibilityOutcome.Accepted, peer.Handshake.Outcome);
        Assert.Equal(1, owner.ReservationCount);
        // The lifecycle flag stays false until this live Owner has stopped. It
        // must not hide the active child's manual-disconnect menu in the GUI.
        await UntilAsync(() => model.ReadRelayTree(upstream.Channel).Nodes.Any(n =>
            n.Kind == GenesisRelayTreeNodeKind.Downstream && n.Phase == PcpRelayConnectionPhase.Connected), timeout.Token);
        Assert.False(model.RelaySnapshot.CleanupConfirmed);
        var tree = model.ReadRelayTree(upstream.Channel);
        Assert.Equal(GenesisRelayTreeState.Active, tree.State);
        Assert.Equal(owner.SessionId, tree.OwnerId);
        var child = Assert.Single(tree.Nodes, n => n.Kind == GenesisRelayTreeNodeKind.Downstream);
        Assert.True(GenesisRelayTreeSelection.HasDownstreamIdentity(tree, GenesisRelayTreeSelection.Key(tree, child)));
        var registry = fixture.State.DownstreamRelays;
        await GenesisGuiNextRelayCommandDispatch.StopAsync(model).WaitAsync(timeout.Token);
        Assert.Equal(GenesisRelayActivationState.Stopped, model.RelaySnapshot.State);
        Assert.True(model.RelaySnapshot.CleanupConfirmed);
        Assert.Equal(GenesisRelayTreeState.Stopped, model.ReadRelayTree(upstream.Channel).State);
        Assert.False(model.RelaySnapshot.ListenerBound);
        Assert.Null(model.RelaySnapshot.EndPoint);
        Assert.True(owner.CleanupConfirmed);
        Assert.Equal(0, registry.ActiveRelayCount);
        Assert.Same(registry, fixture.Channels.Registry);
        using var socket = new TcpClient();
        await Assert.ThrowsAsync<SocketException>(() => socket.ConnectAsync(endpoint.Address, endpoint.Port, timeout.Token).AsTask());
        Assert.True(notifications >= 4);
    }

    [Theory]
    [InlineData(null, null, 2)]
    [InlineData(0, null, 0)]
    [InlineData(null, 0, 0)]
    [InlineData(1, null, 1)]
    [InlineData(null, 1, 1)]
    [InlineData(2, 1, 1)]
    [InlineData(1, 2, 1)]
    public async Task GuiActivatedPcpUsesStartupTotalAndViewingLimitsAndRealDisconnect(int? total, int? viewing, int accepted)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var fixture = await CreateAsync(new() { MaxRelaysTotal = total, MaxRelaysPerViewingChannel = viewing });
        await using var upstream = new RelayActivationTestUpstream(timeout.Token);
        var model = new GenesisMainViewModel(new FakePlaybackSessionController()) { RelayActivation = fixture.State.RelayActivation };
        var start = GenesisGuiNextRelayCommandDispatch.StartAsync(model, upstream.Row, timeout.Token);
        await upstream.SendAsync(timeout.Token);
        Assert.True(await start.WaitAsync(timeout.Token));
        var owner = model.RelayActivation.CurrentOwner!;
        // Saving next-start values cannot mutate the shared active policy.
        Assert.True((await fixture.State.Connection.SaveAsync(GenesisGuiNextConnectionInput.From(new()
        { MaxRelaysTotal = 50, MaxRelaysPerViewingChannel = 50 }), timeout.Token)).Saved);
        var peers = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Peer.ConnectAsync(owner, timeout.Token)));
        try
        {
            Assert.Equal(accepted, peers.Count(p => p.Handshake.Outcome == PcpCompatibilityOutcome.Accepted));
            foreach (var peer in peers.Where(p => p.Handshake.Outcome != PcpCompatibilityOutcome.Accepted))
            {
                Assert.Equal(PcpCompatibilityOutcome.RelayFull, peer.Handshake.Outcome);
                Assert.Equal(503, peer.Handshake.HttpStatusCode);
            }
            Assert.Equal(accepted, fixture.State.DownstreamRelays.ActiveRelayCount);
            foreach (var peer in peers) await peer.DisposeAsync();
            await UntilAsync(() => owner.ReservationCount == 0, timeout.Token);
            await using var replacement = await Peer.ConnectAsync(owner, timeout.Token);
            Assert.Equal(accepted == 0 ? PcpCompatibilityOutcome.RelayFull : PcpCompatibilityOutcome.Accepted, replacement.Handshake.Outcome);
        }
        finally { foreach (var peer in peers) await peer.DisposeAsync(); }
        await model.RelayActivation.StopAsync().WaitAsync(timeout.Token);
        Assert.Equal(0, fixture.State.NetworkPolicies.Relays.ReservedCount);
    }

    [Fact]
    public async Task StopWhileStartingCancellationAndRestartUseFreshSession()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var fixture = await CreateAsync();
        await using var upstream = new RelayActivationTestUpstream(timeout.Token);
        var activation = fixture.State.RelayActivation;
        var firstStart = activation.StartAsync(upstream.Channel);
        await UntilAsync(() => activation.Snapshot.ListenerBound, timeout.Token);
        var old = activation.CurrentOwner!;
        await activation.StopAsync().WaitAsync(timeout.Token);
        Assert.False(await firstStart);
        await upstream.WaitForIdleAsync(timeout.Token);
        using var canceled = new CancellationTokenSource();
        var secondStart = activation.StartAsync(upstream.Channel, canceled.Token);
        await upstream.SendAsync(timeout.Token);
        Assert.True(await secondStart.WaitAsync(timeout.Token));
        var second = activation.CurrentOwner!;
        Assert.NotEqual(old.SessionId, second.SessionId);
        Assert.Equal(2, activation.Snapshot.Generation);
        await old.DisposeAsync(); // Late old terminal/dispose cannot clear the new operation.
        Assert.Same(second, activation.CurrentOwner);
        Assert.Equal(GenesisRelayActivationState.Running, activation.Snapshot.State);
        canceled.Cancel();
        await UntilAsync(() => activation.CanStart, timeout.Token);
        Assert.Equal(GenesisRelayActivationState.Failed, activation.Snapshot.State);
        Assert.Equal(GenesisRelayActivationError.Canceled, activation.Snapshot.Error);
        Assert.True(activation.Snapshot.CleanupConfirmed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealUpstreamConnectionAndNegotiationFailuresAreDistinctAndClean(bool negotiation)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var fixture = await CreateAsync();
        await using var upstream = new RelayActivationTestUpstream(timeout.Token, wrongChannel: true);
        var source = upstream.Channel;
        if (!negotiation)
        {
            // Reserve an ephemeral endpoint then close it, never use a public/fixed port.
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            source = source with { Source = $"pcp://127.0.0.1:{port}/channel/{source.ChannelId}" };
        }
        Assert.False(await fixture.State.RelayActivation.StartAsync(source).WaitAsync(timeout.Token));
        await UntilAsync(() => fixture.State.RelayActivation.CanStart, timeout.Token);
        Assert.Equal(negotiation ? GenesisRelayActivationError.NegotiationFailed : GenesisRelayActivationError.UpstreamFailed,
            fixture.State.RelayActivation.Snapshot.Error);
        Assert.True(fixture.State.RelayActivation.Snapshot.CleanupConfirmed);
        Assert.Equal(0, fixture.Channels.OwnerCount);
        Assert.Equal(0, fixture.State.DownstreamRelays.ActiveRelayCount);
    }

    [Fact]
    public async Task RealHandshakeTimeoutIsNegotiationFailureRatherThanPacketReadinessTimeout()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var fixture = await CreateAsync();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await using var activation = new GenesisGuiNextRelayActivation((channel, token) =>
            fixture.Channels.PrepareViewingAsync(channel, new(IPAddress.Loopback, 0), new()
            {
                UpstreamSession = new() { MaxReconnectAttempts = 0, HandshakeTimeout = TimeSpan.FromMilliseconds(500) },
            }, token));
        var start = activation.StartAsync(new("Silent local peer",
            $"pcp://127.0.0.1:{port}/channel/11111111111111111111111111111111"));
        using var silentPeer = await listener.AcceptTcpClientAsync(timeout.Token);
        Assert.False(await start.WaitAsync(timeout.Token));
        await UntilAsync(() => activation.CanStart, timeout.Token);
        Assert.Equal(GenesisRelayActivationError.NegotiationFailed, activation.Snapshot.Error);
        Assert.True(activation.Snapshot.CleanupConfirmed);
    }

    [Fact]
    public async Task RuntimeFailureAfterRunningIsReportedAndCleaned()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var fixture = await CreateAsync();
        await using var upstream = new RelayActivationTestUpstream(timeout.Token);
        var activation = fixture.State.RelayActivation;
        var start = activation.StartAsync(upstream.Channel);
        await upstream.SendAsync(timeout.Token);
        Assert.True(await start.WaitAsync(timeout.Token));
        await upstream.DisposeAsync();
        await UntilAsync(() => activation.CanStart, timeout.Token);
        Assert.Equal(GenesisRelayActivationState.Failed, activation.Snapshot.State);
        Assert.Equal(GenesisRelayActivationError.UpstreamFailed, activation.Snapshot.Error);
        Assert.True(activation.Snapshot.CleanupConfirmed);
        Assert.Equal(0, fixture.State.DownstreamRelays.ActiveRelayCount);
    }

    [Fact]
    public async Task GuiRelaySharesTotalCapacityWithOtherAuthoritativeChannel()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var fixture = await CreateAsync(new() { MaxRelaysTotal = 1, MaxRelaysPerViewingChannel = 1 });
        await using var broadcast = await fixture.BroadcastAsync(timeout.Token);
        await using var occupied = await Peer.ConnectAsync(broadcast.Owner, timeout.Token);
        await using var upstream = new RelayActivationTestUpstream(timeout.Token);
        var activation = fixture.State.RelayActivation;
        var start = activation.StartAsync(upstream.Channel);
        await upstream.SendAsync(timeout.Token);
        Assert.True(await start.WaitAsync(timeout.Token));
        var owner = activation.CurrentOwner!;
        await using var denied = await Peer.ConnectAsync(owner, timeout.Token);
        Assert.Equal(PcpCompatibilityOutcome.RelayFull, denied.Handshake.Outcome);
        Assert.Equal(1, fixture.State.DownstreamRelays.ActiveRelayCount);
        await occupied.DisposeAsync();
        await UntilAsync(() => fixture.State.DownstreamRelays.ActiveRelayCount == 0, timeout.Token);
        await using var admitted = await Peer.ConnectAsync(owner, timeout.Token);
        Assert.Equal(PcpCompatibilityOutcome.Accepted, admitted.Handshake.Outcome);
    }

    [Fact]
    public async Task OutstandingOwnerLeasePreventsFalseStoppedAndRetry()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var fixture = await CreateAsync();
        await using var upstream = new RelayActivationTestUpstream(timeout.Token);
        var activation = fixture.State.RelayActivation;
        var start = activation.StartAsync(upstream.Channel);
        await upstream.SendAsync(timeout.Token);
        Assert.True(await start.WaitAsync(timeout.Token));
        var owner = activation.CurrentOwner!;
        Assert.True(owner.TryReserve(90002, out var lease));
        try
        {
            await activation.StopAsync().WaitAsync(timeout.Token);
            Assert.Equal(GenesisRelayActivationState.Failed, activation.Snapshot.State);
            Assert.False(activation.Snapshot.CleanupConfirmed);
            Assert.Equal(GenesisRelayTreeState.CleanupUnconfirmed, activation.ReadRelayTree(upstream.ChannelId).State);
            Assert.Equal(1, fixture.State.DownstreamRelays.ActiveRelayCount);
            await activation.StopAsync();
            Assert.Equal(GenesisRelayActivationState.Failed, activation.Snapshot.State);
            Assert.False(await activation.StartAsync(upstream.Channel));
        }
        finally
        {
            lease!.Dispose();
            // This test deliberately leaves the historical shutdown result
            // unconfirmed. Do not use fixture app shutdown to claim recovery.
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.State.DisposeAsync().AsTask());
        }
    }

    [Fact]
    public async Task ExternalOwnerGenerationRetiresGuiRuntimeWithoutDisposingReplacement()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var fixture = await CreateAsync();
        await using var upstream = new RelayActivationTestUpstream(timeout.Token);
        var activation = fixture.State.RelayActivation;
        var start = activation.StartAsync(upstream.Channel);
        await upstream.SendAsync(timeout.Token);
        Assert.True(await start.WaitAsync(timeout.Token));
        var old = activation.CurrentOwner!;
        var replacement = await fixture.Channels.PrepareViewingAsync(upstream.Channel, new(IPAddress.Loopback, 0), cancellationToken: timeout.Token);
        await UntilAsync(() => activation.CanStart, timeout.Token);
        Assert.True(old.CleanupConfirmed);
        Assert.Equal(GenesisRelayActivationError.OwnerSuperseded, activation.Snapshot.Error);
        Assert.False(replacement.IsRetired);
        Assert.False(replacement.ActivationStarted);
        Assert.Equal(1, fixture.Channels.OwnerCount);
        await old.DisposeAsync();
        Assert.False(replacement.IsRetired);
        await replacement.DisposeAsync();
    }

    [Fact]
    public async Task ApplicationShutdownStopsLiveGuiRelayAndReleasesRealConnection()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var fixture = await CreateAsync();
        await using var upstream = new RelayActivationTestUpstream(timeout.Token);
        var activation = fixture.State.RelayActivation;
        var start = activation.StartAsync(upstream.Channel);
        await upstream.SendAsync(timeout.Token);
        Assert.True(await start.WaitAsync(timeout.Token));
        var owner = activation.CurrentOwner!;
        await using var peer = await Peer.ConnectAsync(owner, timeout.Token);
        await fixture.State.DisposeAsync();
        Assert.True(owner.CleanupConfirmed);
        Assert.Equal(0, fixture.State.DownstreamRelays.ActiveRelayCount);
        Assert.Equal(0, fixture.State.NetworkPolicies.Relays.ReservedCount);
        Assert.False(await activation.StartAsync(upstream.Channel));
    }

    [Fact]
    public async Task DisplayOnlyRowCannotActivateAndPlaybackStopDoesNotStopRelay()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var fixture = await CreateAsync();
        await using var upstream = new RelayActivationTestUpstream(timeout.Token);
        var playback = new FakePlaybackSessionController();
        var model = new GenesisMainViewModel(playback) { RelayActivation = fixture.State.RelayActivation };
        Assert.False(await GenesisGuiNextRelayCommandDispatch.StartAsync(model, new(upstream.Channel, null)));
        Assert.Equal(0, fixture.Channels.OwnerCount);
        var start = GenesisGuiNextRelayCommandDispatch.StartAsync(model, upstream.Row);
        await upstream.SendAsync(timeout.Token);
        Assert.True(await start.WaitAsync(timeout.Token));
        await GenesisGuiNextPlaybackCommandDispatch.StopAsync(model, new GenesisMainWindowInteraction(model));
        Assert.Equal(GenesisRelayActivationState.Running, model.RelaySnapshot.State);
        Assert.Equal(0, fixture.State.DownstreamRelays.ActiveRelayCount);
    }

    [Fact]
    public void RelayControlsDoNotOverlapRecordingDetailsAndKeepOwnerDrawFontPath()
    {
        Assert.True(GenesisGuiNextAppShellBridge.RelayControlsTop >= 148);
        Assert.True(GenesisGuiNextAppShellBridge.RelayStatusTop >= GenesisGuiNextAppShellBridge.RelayControlsTop + 26);
        Assert.True(GenesisGuiNextAppShellPlacement.DetailsHeight >= GenesisGuiNextAppShellBridge.RelayStatusTop + 40);
        Assert.NotEqual(GenesisGuiNextAppShellBridge.RelayStartControlId, GenesisGuiNextAppShellBridge.RelayStopControlId);
        Assert.Equal(976, GenesisGuiNextAppShellBridge.RelayStatusWidth(1000));
        Assert.Equal(376, GenesisGuiNextAppShellBridge.RelayStatusWidth(400));
        Assert.Equal(1, GenesisGuiNextAppShellBridge.RelayStatusWidth(10));
    }
}
