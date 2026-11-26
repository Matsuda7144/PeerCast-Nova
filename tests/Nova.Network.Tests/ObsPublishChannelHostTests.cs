using System.Net;
using System.Net.Sockets;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Publishing;

namespace PeerCast.Nova.Network.Tests;

public sealed class ObsPublishChannelHostTests
{
    [Fact]
    public async Task PublishingContractConstructionAndUnstartedDisposalNeverBind()
    {
        var occupied = new TcpListener(IPAddress.Loopback, 0);
        occupied.Start();
        try
        {
            var endpoint = (IPEndPoint)occupied.LocalEndpoint;
            await using var host = new ObsPublishChannelHost(new()
            {
                Ingest = new() { Port = endpoint.Port },
                RelayPort = endpoint.Port,
                Http = new() { Port = endpoint.Port },
            });
            Assert.Equal(ChannelSessionState.Created, host.Snapshot.State);
            Assert.Equal(0, host.RecordingRegistry.Count);
            Assert.Throws<InvalidOperationException>(() => host.IngestEndPoint);
            Assert.Throws<InvalidOperationException>(() => host.RelayEndPoint);
            Assert.Throws<InvalidOperationException>(() => host.HttpStreamUrl);
            await host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            AssertUnconnectedCancellation(await host.Completion);
            Assert.Equal(endpoint, occupied.LocalEndpoint);
        }
        finally
        {
            occupied.Stop();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublishingContractUnconnectedWaitEndsByCancelOrDispose(bool cancel)
    {
        using var caller = new CancellationTokenSource();
        await using var host = Create();
        host.Start(caller.Token);
        var ingest = host.IngestEndPoint;
        var relay = host.RelayEndPoint;
        var http = new IPEndPoint(IPAddress.Loopback, new Uri(host.HttpStreamUrl).Port);
        Assert.Equal(ChannelSessionState.Negotiating, host.Snapshot.State);
        Assert.False(host.Completion.IsCompleted);
        Assert.False(host.HeadAvailable.IsCompleted);
        Assert.Equal(1, host.RecordingRegistry.Count);
        if (cancel) await caller.CancelAsync();
        else await host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        var result = await host.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        AssertUnconnectedCancellation(result);
        Assert.Equal(0, host.RecordingRegistry.Count);
        Assert.False(host.IsRegistered);
        Assert.False(await host.HeadAvailable);
        AssertPortReleased(ingest);
        AssertPortReleased(relay);
        AssertPortReleased(http);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublishingContractUnconnectedCleanupReleasesBorrowedHttpSourceOnly(bool cancel)
    {
        await using var http = new PcpHttpFlvPlaybackHost();
        http.Start();
        var endpoint = http.LocalEndPoint;
        var registry = new RecordingStreamSourceLeaseRegistry();
        using var caller = new CancellationTokenSource();
        await using var host = Create(http, registry);
        host.Start(caller.Token);
        Assert.Equal(1, http.ActiveSourceCount);
        Assert.Equal(1, registry.Count);
        if (cancel) await caller.CancelAsync();
        else await host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        AssertUnconnectedCancellation(await host.Completion.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(0, http.ActiveSourceCount);
        Assert.Equal(0, registry.Count);
        Assert.Equal(endpoint, http.LocalEndPoint);
        Assert.Throws<InvalidOperationException>(() => http.Start());
    }

    [Fact]
    public async Task PublishingContractStartRacingDisposeCannotLeaveGenerationRunning()
    {
        for (var iteration = 0; iteration < 16; iteration++)
        {
            await using var host = Create();
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var start = Task.Run(async () =>
            {
                await release.Task;
                try { host.Start(); }
                catch (ObjectDisposedException) { }
            });
            var dispose = Task.Run(async () =>
            {
                await release.Task;
                await host.DisposeAsync();
            });
            release.TrySetResult();
            await Task.WhenAll(start, dispose).WaitAsync(TimeSpan.FromSeconds(10));
            AssertUnconnectedCancellation(await host.Completion);
            Assert.Equal(0, host.RecordingRegistry.Count);
            Assert.Equal(0, host.Fanout.ActiveChildCount);
            Assert.Throws<ObjectDisposedException>(() => host.Start());
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PublishingContractPortCollisionCleansPartialStartup(int collision)
    {
        var listeners = Enumerable.Range(0, 3).Select(_ => new TcpListener(IPAddress.Loopback, 0)).ToArray();
        foreach (var listener in listeners) listener.Start();
        var endpoints = listeners.Select(listener => (IPEndPoint)listener.LocalEndpoint).ToArray();
        try
        {
            for (var index = 0; index < listeners.Length; index++)
            {
                if (index != collision) listeners[index].Stop();
            }
            await using var host = new ObsPublishChannelHost(new()
            {
                Ingest = new() { Port = endpoints[0].Port },
                RelayPort = endpoints[1].Port,
                Http = new() { Port = endpoints[2].Port },
            });
            host.Start();
            var result = await host.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(ObsPublishChannelEndReason.StartupFailed, result.EndReason);
            Assert.True(result.ShutdownCompleted);
            Assert.False(result.HttpDrained);
            Assert.Equal(0, host.RecordingRegistry.Count);
            Assert.False(host.IsRegistered);
            for (var index = 0; index < endpoints.Length; index++)
            {
                if (index != collision) AssertPortReleased(endpoints[index]);
            }
        }
        finally
        {
            foreach (var listener in listeners) listener.Stop();
        }
    }

    [Fact]
    public async Task PublishingContractPublisherConnectRacingCancellationDoesNotInventCleanEos()
    {
        await using var host = Create();
        using var caller = new CancellationTokenSource();
        host.Start(caller.Token);
        var endpoint = host.IngestEndPoint;
        using var client = new TcpClient(AddressFamily.InterNetwork);
        var connect = client.ConnectAsync(endpoint);
        await caller.CancelAsync();
        try { await connect.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (SocketException) { }
        AssertUnconnectedCancellation(await host.Completion.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(0, host.RecordingRegistry.Count);
        AssertPortReleased(endpoint);
    }

    private static void AssertUnconnectedCancellation(ObsPublishChannelResult result)
    {
        Assert.Equal(ObsPublishChannelEndReason.HostCancelled, result.EndReason);
        Assert.Equal(0, result.AcceptedPacketCount);
        Assert.Equal(0, result.HeadCount);
        Assert.False(result.HttpDrained);
        Assert.NotEqual(PcpRelayDrainOutcome.Completed, result.RelayDrain);
        Assert.True(result.ShutdownCompleted);
    }

    private static void AssertPortReleased(IPEndPoint endpoint)
    {
        var probe = new TcpListener(endpoint);
        try { probe.Start(); }
        finally { probe.Stop(); }
    }

    [Fact]
    public async Task Publish_registers_one_generation_replays_HEAD_and_drains_before_unregister()
    {
        await using var http = new PcpHttpFlvPlaybackHost();
        http.Start();
        var registry = new RecordingStreamSourceLeaseRegistry();
        await using var host = Create(http, registry);
        Assert.Equal(ChannelSessionState.Created, host.Snapshot.State);
        host.Start();
        var treeSource = (IPcpRelayTreeSource)host;
        Assert.False(treeSource.ObserveRelayTree().LocalInputConnected);
        Assert.True(host.IsRegistered);
        Assert.Equal(1, registry.Count);
        Assert.Equal(1, http.ActiveSourceCount);
        Assert.Equal(IPAddress.Loopback, host.RelayEndPoint.Address);
        using var client = await PublishAsync(host);
        await ConfigureAsync(client);
        Assert.True(await host.HeadAvailable.WaitAsync(TimeSpan.FromSeconds(10)));
        var tree = treeSource.ObserveRelayTree();
        Assert.True(tree.LocalInputConnected);
        Assert.Null(tree.Upstream);
        Assert.Equal(host.ChannelId, tree.ChannelId);
        Assert.Equal(host.SessionId, tree.OwnerId);
        var late = new CopySink();
        await using var registration = host.Fanout.AddChild(late);
        await late.WaitAsync(1);
        Assert.True(late.Packets[0].Replay);
        Assert.Equal(0x64616568U, late.Packets[0].Type);
        await client.SendAsync(9, 1, 37, ObsFlvPublishingSessionTests.Idr);
        await client.SendAsync(8, 1, 38, ObsFlvPublishingSessionTests.Audio);
        await StopAsync(client);
        var result = await host.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ObsPublishChannelEndReason.PublisherStopped, result.EndReason);
        Assert.Equal(3, result.AcceptedPacketCount);
        Assert.Equal(1, result.HeadCount);
        Assert.Equal(2, result.DataCount);
        Assert.Equal(PcpRelayDrainOutcome.Completed, result.RelayDrain);
        Assert.True(result.HttpDrained);
        Assert.True(result.ShutdownCompleted);
        Assert.Equal(3, late.Packets.Length);
        Assert.Equal(result.AcceptedByteCount, late.Packets.Sum(packet => packet.Bytes.Length));
        Assert.False(host.IsRegistered);
        Assert.Equal(0, registry.Count);
        Assert.Equal(0, http.ActiveSourceCount);
        Assert.Equal(ChannelSessionState.Stopped, host.Snapshot.State);
        Assert.False(treeSource.ObserveRelayTree().LocalInputConnected);
        Assert.False(treeSource.ObserveRelayTree().Running);
        Assert.Throws<InvalidOperationException>(() => host.Start());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EOF_or_host_cancellation_does_not_report_a_clean_drain(bool cancel)
    {
        await using var http = new PcpHttpFlvPlaybackHost();
        http.Start();
        var registry = new RecordingStreamSourceLeaseRegistry();
        using var caller = new CancellationTokenSource();
        await using var host = Create(http, registry);
        host.Start(caller.Token);
        using var client = await PublishAsync(host);
        await ConfigureAsync(client);
        Assert.True(await host.HeadAvailable.WaitAsync(TimeSpan.FromSeconds(10)));
        if (cancel) await caller.CancelAsync();
        else client.EndInput();
        var result = await host.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(cancel ? ObsPublishChannelEndReason.HostCancelled : ObsPublishChannelEndReason.Disconnected, result.EndReason);
        Assert.False(result.HttpDrained);
        Assert.NotEqual(PcpRelayDrainOutcome.Completed, result.RelayDrain);
        Assert.True(result.ShutdownCompleted);
        Assert.Equal(0, registry.Count);
        Assert.Equal(0, http.ActiveSourceCount);
    }

    [Fact]
    public async Task Protocol_rejection_retains_a_fixed_fault_and_removes_the_channel()
    {
        await using var host = Create();
        host.Start();
        using var client = await PublishAsync(host);
        await client.SendAsync(9, 1, 0, [0x1C, 0, 0, 0, 0]);
        var result = await host.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ObsPublishChannelEndReason.ProtocolRejected, result.EndReason);
        Assert.Equal(ChannelFaultCategory.ProtocolViolation, host.Snapshot.Fault!.Category);
        Assert.Equal(0, result.AcceptedPacketCount);
        Assert.False(host.IsRegistered);
        Assert.True(result.ShutdownCompleted);
    }

    [Fact]
    public async Task Duplicate_channel_reservation_never_replaces_the_existing_owner()
    {
        await using var http = new PcpHttpFlvPlaybackHost();
        http.Start();
        var registry = new RecordingStreamSourceLeaseRegistry();
        var id = new ChannelId(Guid.NewGuid());
        await using var first = Create(http, registry, id);
        first.Start();
        Assert.True(registry.TryGet(id, out var factory));
        await using var duplicate = Create(http, registry, id);
        duplicate.Start();
        Assert.Equal(ObsPublishChannelEndReason.StartupFailed,
            (await duplicate.Completion.WaitAsync(TimeSpan.FromSeconds(10))).EndReason);
        Assert.True(first.IsRegistered);
        Assert.True(registry.TryGet(id, out var stillCurrent));
        Assert.Same(factory, stillCurrent);
        Assert.Equal(1, http.ActiveSourceCount);
    }

    [Fact]
    public async Task Same_key_and_channel_in_a_new_host_get_a_fresh_generation_and_no_stale_HEAD()
    {
        await using var http = new PcpHttpFlvPlaybackHost();
        http.Start();
        var registry = new RecordingStreamSourceLeaseRegistry();
        var id = new ChannelId(Guid.NewGuid());
        var first = Create(http, registry, id);
        first.Start();
        using (var publisher = await PublishAsync(first))
        {
            await ConfigureAsync(publisher);
            Assert.True(await first.HeadAvailable.WaitAsync(TimeSpan.FromSeconds(10)));
            await StopAsync(publisher);
            await first.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        }
        await using var second = Create(http, registry, id);
        second.Start();
        Assert.NotEqual(first.SessionId, second.SessionId);
        Assert.True(second.IsRegistered);
        await first.DisposeAsync();
        Assert.True(second.IsRegistered);
        Assert.Equal(1, registry.Count);
        Assert.Equal(1, http.ActiveSourceCount);
        var late = new CopySink();
        await using var child = second.Fanout.AddChild(late);
        Assert.Empty(late.Packets);
        using var newPublisher = await PublishAsync(second);
        await ConfigureAsync(newPublisher);
        Assert.True(await second.HeadAvailable.WaitAsync(TimeSpan.FromSeconds(10)));
        await late.WaitAsync(1);
        Assert.False(late.Packets[0].Replay);
        await StopAsync(newPublisher);
        var result = await second.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, result.HeadCount);
        Assert.Equal(0, result.DataCount);
    }

    [Fact]
    public async Task Additional_TCP_publisher_is_rejected_without_displacing_channel_owner()
    {
        await using var host = Create();
        host.Start();
        using var first = await PublishAsync(host);
        using var second = new TcpClient();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await second.ConnectAsync(host.IngestEndPoint, deadline.Token);
        Assert.Equal(0, await second.GetStream().ReadAsync(new byte[1], deadline.Token));
        await ConfigureAsync(first);
        Assert.True(await host.HeadAvailable.WaitAsync(TimeSpan.FromSeconds(10)));
        await StopAsync(first);
        Assert.Equal(ObsPublishChannelEndReason.PublisherStopped,
            (await host.Completion.WaitAsync(TimeSpan.FromSeconds(10))).EndReason);
    }

    [Fact]
    public async Task A_slow_optional_relay_child_is_removed_without_failing_ingest_or_HTTP()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = Create(fanout: new PcpRelayFanoutOptions { MaxPendingPacketsPerChild = 2 });
        host.Start();
        await using var child = host.Fanout.AddChild(new BlockingSink(entered));
        using var client = await PublishAsync(host);
        await ConfigureAsync(client);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await client.SendAsync(9, 1, 0, ObsFlvPublishingSessionTests.Idr);
        for (var i = 0; i < 8; i++) await client.SendAsync(8, 1, (uint)(i + 1), ObsFlvPublishingSessionTests.Audio);
        await StopAsync(client);
        var result = await host.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        await child.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(PcpRelayChildEndReason.SlowConsumer, child.Snapshot.EndReason);
        Assert.Equal(ObsPublishChannelEndReason.PublisherStopped, result.EndReason);
        Assert.Equal(10, result.AcceptedPacketCount);
        Assert.True(result.HttpDrained);
    }

    [Fact]
    public async Task HTTP_consumer_fault_stops_the_generation_before_acceptance_advances()
    {
        await using var http = new PcpHttpFlvPlaybackHost();
        http.Start();
        await using var host = Create(http);
        host.Start();
        using var client = await PublishAsync(host);
        await http.DisposeAsync();
        await ConfigureAsync(client);
        var result = await host.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ObsPublishChannelEndReason.ConsumerFaulted, result.EndReason);
        Assert.Equal(0, result.AcceptedPacketCount);
        Assert.Equal(ChannelFaultCategory.ConsumerFailed, host.Snapshot.Fault!.Category);
    }

    [Fact]
    public async Task Dispose_before_start_and_precancelled_start_leave_no_listener_or_registration()
    {
        var registry = new RecordingStreamSourceLeaseRegistry();
        var host = Create(registry: registry);
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        Assert.Throws<OperationCanceledException>(() => host.Start(caller.Token));
        await host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ObsPublishChannelEndReason.HostCancelled, (await host.Completion).EndReason);
        Assert.Equal(0, registry.Count);
        await host.DisposeAsync();
    }

    [Fact]
    public async Task Cancellation_during_normal_stop_drain_cannot_be_reported_as_clean_EOS()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = Create();
        host.Start();
        await using var child = host.Fanout.AddChild(new BlockingSink(entered));
        using var publisher = await PublishAsync(host);
        await ConfigureAsync(publisher);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await StopAsync(publisher);
        await WaitAsync(() => host.Snapshot.State == ChannelSessionState.Stopping);
        await host.DisposeAsync();
        var result = await host.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ObsPublishChannelEndReason.HostCancelled, result.EndReason);
        Assert.False(result.HttpDrained);
        Assert.Equal(PcpRelayDrainOutcome.Canceled, result.RelayDrain);
    }

    [Fact]
    public async Task Shared_registry_rejects_duplicate_even_with_separate_HTTP_hosts()
    {
        var registry = new RecordingStreamSourceLeaseRegistry();
        var id = new ChannelId(Guid.NewGuid());
        await using var first = Create(registry: registry, id: id);
        first.Start();
        await using var second = Create(registry: registry, id: id);
        second.Start();
        var result = await second.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ObsPublishChannelEndReason.StartupFailed, result.EndReason);
        Assert.True(result.ShutdownCompleted);
        Assert.True(first.IsRegistered);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public async Task Drain_timeout_reports_incomplete_delivery_and_still_cleans_owned_resources()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = new ObsPublishChannelHost(new ObsPublishChannelOptions
        {
            Ingest = new() { Port = 0 },
            DrainTimeout = TimeSpan.FromMilliseconds(50),
        });
        host.Start();
        await using var child = host.Fanout.AddChild(new BlockingSink(entered));
        using var publisher = await PublishAsync(host);
        await ConfigureAsync(publisher);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await StopAsync(publisher);
        var result = await host.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ObsPublishChannelEndReason.PublisherStopped, result.EndReason);
        Assert.Equal(PcpRelayDrainOutcome.TimedOut, result.RelayDrain);
        Assert.False(result.HttpDrained);
        Assert.True(result.ShutdownCompleted);
        Assert.False(host.IsRegistered);
    }

    [Theory]
    [InlineData(1, 65536)]
    [InlineData(8, 65535)]
    [InlineData(8, 131071)]
    public void Options_reject_bounds_that_cannot_admit_the_mandatory_HTTP_child(int packets, int bytes)
    {
        Assert.Throws<ArgumentException>(() => new ObsPublishChannelHost(new ObsPublishChannelOptions
        {
            Fanout = new() { MaxPendingPacketsPerChild = packets, MaxPendingBytesPerChild = bytes },
        }));
    }

    [Fact]
    public async Task EffectiveBitrateKbps_ComputesMeasuredBitrateWithoutDeclaredBitrate()
    {
        var clock = new ManualNatTimeProvider();
        clock.Advance(TimeSpan.FromSeconds(1));
        await using var host = new ObsPublishChannelHost(
            new ObsPublishChannelOptions { Ingest = new() { Port = 0 } },
            channelId: null,
            httpHost: null,
            recordingRegistry: null,
            acquireRecordingOwner: null,
            recordingCleanupObserver: null,
            announceTime: clock);
        host.Start();

        Assert.Null(host.CurrentMetadata.BitrateKbps);
        Assert.Null(host.MeasuredBitrateKbps);
        Assert.Null(host.EffectiveBitrateKbps);

        using var publisher = await PublishAsync(host);
        await ConfigureAsync(publisher);
        Assert.True(await host.HeadAvailable.WaitAsync(TimeSpan.FromSeconds(10)));

        clock.Advance(TimeSpan.FromSeconds(3));
        await publisher.SendAsync(9, 1, 37, ObsFlvPublishingSessionTests.Idr);

        await WaitAsync(() => host.MeasuredBitrateKbps is not null);
        Assert.True(host.MeasuredBitrateKbps > 0);
        Assert.Equal(host.MeasuredBitrateKbps, host.EffectiveBitrateKbps);
    }

    internal static ObsPublishChannelHost Create(
        PcpHttpFlvPlaybackHost? http = null, RecordingStreamSourceLeaseRegistry? registry = null,
        ChannelId? id = null, PcpRelayFanoutOptions? fanout = null) =>
        new(new ObsPublishChannelOptions
        {
            Ingest = new() { Port = 0 },
            Fanout = fanout ?? new(),
        }, id, http, registry);

    internal static async Task<ObsRtmpTestClient> PublishAsync(ObsPublishChannelHost host)
    {
        var client = new ObsRtmpTestClient();
        await client.ConnectAsync(host.IngestEndPoint);
        await client.SetChunkBytesAsync(4096);
        await client.PublishAsync();
        return client;
    }

    internal static async Task ConfigureAsync(ObsRtmpTestClient client)
    {
        await client.SendAsync(8, 1, 0, ObsFlvPublishingSessionTests.AudioConfiguration, id: 4);
        await client.SendAsync(9, 1, 0, ObsFlvPublishingSessionTests.VideoConfiguration, id: 4);
    }

    internal static Task StopAsync(ObsRtmpTestClient client) => client.SendCommandAsync(0, "FCUnpublish", 6.0, null, "nova");

    private static async Task WaitAsync(Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!ready()) await Task.Delay(5, timeout.Token);
    }

    private sealed class BlockingSink(TaskCompletionSource entered) : IPcpRelayChildSink
    {
        public async ValueTask SendAsync(PcpRelayPacket packet, CancellationToken token)
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
    }

    private sealed class CopySink : IPcpRelayChildSink
    {
        private readonly List<(uint Type, byte[] Bytes, bool Replay)> _packets = [];
        public (uint Type, byte[] Bytes, bool Replay)[] Packets
        {
            get { lock (_packets) { return _packets.ToArray(); } }
        }
        public ValueTask SendAsync(PcpRelayPacket packet, CancellationToken token)
        {
            lock (_packets) { _packets.Add((packet.Type, packet.Payload.ToArray(), packet.IsRetainedHeadReplay)); }
            return ValueTask.CompletedTask;
        }
        public Task WaitAsync(int count) => ObsPublishChannelHostTests.WaitAsync(() => Packets.Length >= count);
    }
}
