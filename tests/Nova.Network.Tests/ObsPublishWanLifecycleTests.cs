using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class ObsPublishWanLifecycleTests
{
    private static ObsPublishChannelHost Create(ObsPublishWanTests.Interfaces interfaces, ObsPublishWanTests.Changes changes,
        Func<IPEndPoint, IPEndPoint>? bind = null, Func<ObsPublishTerminationPoint, Task>? observe = null) =>
        new(new ObsPublishChannelOptions { Wan = ObsPublishWanTests.Options, Ingest = new() { Port = 0 } },
            null, null, null, null, null, interfaces,
            (id, session, _) => new Registration(id, session), changes,
            bindForTests: bind ?? (_ => new(IPAddress.Loopback, 0)),
            wanIngressForTests: new() { SourceAllowed = address => address is not null && IPAddress.IsLoopback(address) })
        { TerminationObserver = observe };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task WanGenerationPingAndTerminationOwnResources(int stop)
    {
        var interfaces = new ObsPublishWanTests.Interfaces(ObsPublishWanTests.Nic);
        var changes = new ObsPublishWanTests.Changes();
        await using var host = Create(interfaces, changes);
        host.Start();
        Assert.Throws<InvalidOperationException>(() => host.Start());
        Assert.Equal(IPAddress.Loopback, host.IngestEndPoint.Address);
        Assert.Equal("127.0.0.1", new Uri(host.HttpStreamUrl).Host);
        Assert.False(host.PublishingStatus.WanRelay!.ListenerReady); // Fake bind is not the configured WAN endpoint.
        Assert.False(host.PublishingStatus.WanRelay.ReachabilityVerified);
        using var publisher = await ObsPublishChannelHostTests.PublishAsync(host);
        await ObsPublishChannelHostTests.ConfigureAsync(publisher);
        Assert.True(await host.HeadAvailable.WaitAsync(TimeSpan.FromSeconds(5)));
        var before = host.Fanout.ActiveChildCount;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var ping = await new TcpTransportConnector().ConnectAsync(host.RelayEndPoint, timeout.Token);
        var bytes = new byte[PcpPreamble.Size]; PcpPreamble.Write(bytes);
        var writer = new ArrayBufferWriter<byte>();
        PcpAtomTreeCodec.Write(writer, PcpHeloCodec.CreateHelo("fixture", Guid.NewGuid()));
        await ping.WriteAsync(bytes.Concat(writer.WrittenSpan.ToArray()).ToArray(), timeout.Token);
        var pipe = PipeReader.Create(ping.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
        await using var atoms = new PcpAtomPipeMessageReader(pipe);
        Assert.Equal(host.SessionId.Value, PcpHeloCodec.ParseOleh(await atoms.ReadNextAsync(timeout.Token)).SessionId);
        await pipe.CompleteAsync();
        Assert.Equal(before, host.Fanout.ActiveChildCount);
        if (stop == 0) await ObsPublishChannelHostTests.StopAsync(publisher);
        else if (stop == 1)
        {
            var result = await host.RequestLocalTerminationAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(ObsPublishLocalTerminationOutcome.LocalCompleted, result.Outcome);
            Assert.True(result.Cleanup!.WanNetwork);
        }
        else await host.DisposeAsync();
        var ended = await host.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        if (stop == 2)
        {
            // Forced active disposal is not normal EOS. Its aggregate snapshot is
            // separate from Dispose's joined LocalCleanup proof and the WAN facts below.
            Assert.Equal(ObsPublishChannelEndReason.HostCancelled, ended.EndReason);
            Assert.False(ended.HttpDrained);
        }
        else Assert.True(ended.ShutdownCompleted);
        if (stop == 0) Assert.Equal(ObsPublishChannelEndReason.PublisherStopped, ended.EndReason);
        Assert.Equal(0, ended.Relay!.Snapshot.ActiveConnectionCount);
        Assert.Equal(0, ended.Relay.Snapshot.AdmissionCleanupPendingCount);
        Assert.Equal(0, ended.Relay.Snapshot.AdmissionCleanupFailedCount);
        Assert.Equal(0, ended.Relay.Snapshot.CleanupIncompleteCount);
        Assert.Equal(0, ended.Relay.Snapshot.WanIngress!.ActivePings);
        Assert.Equal(0, changes.Subscriptions);
    }

    [Fact]
    public async Task WanNicDisappearanceEndsCurrentGenerationWithoutRebinding()
    {
        var interfaces = new ObsPublishWanTests.Interfaces(ObsPublishWanTests.Nic);
        var changes = new ObsPublishWanTests.Changes();
        var binds = 0;
        await using var host = Create(interfaces, changes, _ => { binds++; return new(IPAddress.Loopback, 0); });
        host.Start();
        interfaces.Items = [];
        changes.Fire();
        var ended = await host.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ObsPublishChannelEndReason.TransportFailed, ended.EndReason);
        Assert.Equal(ObsPublishWanFailure.NetworkChanged, host.PublishingStatus.WanRelay!.Failure);
        Assert.True(ended.ShutdownCompleted);
        Assert.Equal(1, binds);
        Assert.Equal(0, changes.Subscriptions);
        Assert.False(host.PublishingStatus.WanRelay.ListenerReady);
    }

    [Fact]
    public async Task AcceptedFinishClosesWanGateBeforeTheOwnerReachesCleanup()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var interfaces = new ObsPublishWanTests.Interfaces(ObsPublishWanTests.Nic);
        var changes = new ObsPublishWanTests.Changes();
        await using var host = Create(interfaces, changes, observe: point =>
        {
            if (point != ObsPublishTerminationPoint.BeforeEndSelection) return Task.CompletedTask;
            entered.TrySetResult(); return release.Task;
        });
        host.Start();
        using var publisher = await ObsPublishChannelHostTests.PublishAsync(host);
        await ObsPublishChannelHostTests.ConfigureAsync(publisher);
        await host.HeadAvailable.WaitAsync(TimeSpan.FromSeconds(5));
        var finish = host.RequestLocalTerminationAsync(TimeSpan.FromSeconds(5));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var client = await new TcpTransportConnector().ConnectAsync(host.RelayEndPoint, timeout.Token);
            Assert.Equal(0, await client.ReadAsync(new byte[1], timeout.Token));
        }
        finally { release.TrySetResult(); }
        Assert.Equal(ObsPublishLocalTerminationOutcome.LocalCompleted, (await finish).Outcome);
    }

    [Fact]
    public async Task WanBindCollisionCleansPartialStartupAndLeavesUnrelatedListenerAlone()
    {
        var occupied = new TcpListener(IPAddress.Loopback, 0);
        occupied.Start();
        try
        {
            var endpoint = (IPEndPoint)occupied.LocalEndpoint;
            var interfaces = new ObsPublishWanTests.Interfaces(ObsPublishWanTests.Nic);
            var changes = new ObsPublishWanTests.Changes();
            await using var host = Create(interfaces, changes, _ => endpoint);
            host.Start();
            var result = await host.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(ObsPublishChannelEndReason.StartupFailed, result.EndReason);
            Assert.True(result.ShutdownCompleted);
            Assert.Equal(ObsPublishWanFailure.BindFailed, host.PublishingStatus.WanRelay!.Failure);
            Assert.Equal(endpoint, occupied.LocalEndpoint);
            Assert.Equal(0, changes.Subscriptions);
        }
        finally { occupied.Stop(); }
    }

    [Fact]
    public async Task WanPublishingAcquiresAndReleasesNatPortMappingLease()
    {
        await using var nat = new NatPortMappingTestHarness();
        var interfaces = new ObsPublishWanTests.Interfaces(ObsPublishWanTests.Nic);
        var changes = new ObsPublishWanTests.Changes();
        await using var host = new ObsPublishChannelHost(
            new ObsPublishChannelOptions { Wan = ObsPublishWanTests.Options, Ingest = new() { Port = 0 } },
            null, null, null, null, null, interfaces,
            (id, session, _) => new Registration(id, session), changes,
            bindForTests: _ => new(IPAddress.Any, 0),
            wanIngressForTests: new() { SourceAllowed = address => address is not null && IPAddress.IsLoopback(address) },
            portMapping: nat.Manager);

        host.Start();
        var expectedPort = host.RelayEndPoint.Port;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!nat.Manager.ReadState().RegisteredInternalPorts.Contains(expectedPort))
        {
            await Task.Delay(10, deadline.Token);
        }

        using var publisher = await ObsPublishChannelHostTests.PublishAsync(host);
        await ObsPublishChannelHostTests.ConfigureAsync(publisher);
        await ObsPublishChannelHostTests.StopAsync(publisher);
        var result = await host.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ObsPublishChannelEndReason.PublisherStopped, result.EndReason);
        Assert.DoesNotContain(expectedPort, nat.Manager.ReadState().RegisteredInternalPorts);
    }

    private sealed class Registration(ChannelId channel, ChannelSessionId session) : IPcpDownstreamRelayRegistration
    {
        public ChannelId ChannelId => channel;
        public ChannelSessionId SessionId => session;
        public int ReservationCount => 0;
        public bool TryReserve(long connectionId, [NotNullWhen(true)] out IDisposable? reservation, CancellationToken cancellationToken = default)
        { reservation = null; return false; }
        public void Dispose() { }
    }
}
