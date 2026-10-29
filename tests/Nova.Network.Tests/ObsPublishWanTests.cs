using System.Net;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Publishing;

namespace PeerCast.Nova.Network.Tests;

public sealed class ObsPublishWanTests
{
    internal static readonly ObsPublishLanInterface Nic = new("nic", "Fake NIC", true, "192.168.42.5", "255.255.255.0", 24);
    internal static ObsPublishWanOptions Options => new(true, Nic.Id, Nic.Address, 17144, "198.41.0.4", 27144);

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("0.1.2.3")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.1.2")]
    [InlineData("100.64.1.2")]
    [InlineData("192.0.0.9")]
    [InlineData("192.0.2.1")]
    [InlineData("192.31.196.1")]
    [InlineData("192.52.193.1")]
    [InlineData("192.88.99.1")]
    [InlineData("192.175.48.1")]
    [InlineData("198.18.1.1")]
    [InlineData("198.51.100.1")]
    [InlineData("203.0.113.1")]
    [InlineData("224.1.2.3")]
    [InlineData("240.1.2.3")]
    [InlineData("255.255.255.255")]
    [InlineData("::ffff:192.168.1.2")]
    [InlineData("localhost")]
    [InlineData("192.168.001.2")]
    public void SpecialOrAmbiguousAddressesFailClosed(string address)
    {
        Assert.False(PublishingIpv4.IsBindAddress(address));
        Assert.False(PublishingIpv4.IsPublicAddress(address));
        Assert.Throws<ObsPublishWanException>(() => (Options with { Address = address }).Validate());
    }

    [Theory]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.16.1.1", false)]
    [InlineData("192.168.1.5", false)]
    [InlineData("198.41.0.4", true)]
    public void BindAndSourcePolicyPermitPrivateNatOrOrdinaryGlobalIpv4(string address, bool isPublic)
    {
        Assert.True(PublishingIpv4.IsBindAddress(address));
        Assert.True(PublishingIpv4.IsSourceAddress(IPAddress.Parse(address)));
        Assert.Equal(isPublic, PublishingIpv4.IsPublicAddress(address));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void ExplicitInternalPortIsRequired(int port) => Assert.Throws<ObsPublishWanException>(() => (Options with { Port = port }).Validate());

    [Fact]
    public void EndpointIdentitiesAreIndependentAndNeverVerifiedByConfiguration()
    {
        Options.Validate();
        var status = new ObsPublishWanStatus(true, true, Options.Address, Options.Port, Options.AdvertisedAddress, Options.AdvertisedPort, ObsPublishWanFailure.None, false);
        Assert.NotEqual(status.BindPort, status.AdvertisedPort);
        Assert.False(status.ReachabilityVerified);
        Assert.Null(status.TrackerObservedEndpoint);
        Assert.Null(status.VerifiedEndpoint);
        (Options with { AdvertisedAddress = "", AdvertisedPort = 0 }).Validate();
        Assert.Throws<ObsPublishWanException>(() => (Options with { AdvertisedAddress = "192.168.1.2" }).Validate());
        Assert.Throws<ObsPublishWanException>(() => (Options with { AdvertisedAddress = "", AdvertisedPort = 1 }).Validate());
        Assert.Throws<ObsPublishWanException>(() => (Options with { AdvertisedPort = 0 }).Validate());
    }

    [Fact]
    public void NicResolutionAndModesCannotFallbackOrAutoPromote()
    {
        var interfaces = new Interfaces(Nic);
        Assert.True(Options.Resolve(interfaces).StillMatches(interfaces));
        foreach (var items in new[] { Array.Empty<ObsPublishLanInterface>(), new[] { Nic, Nic }, new[] { Nic with { Id = "other" } }, new[] { Nic with { IsUp = false } } })
            Assert.Throws<ObsPublishWanException>(() => Options.Resolve(new Interfaces(items)));
        Assert.Equal(PublishingRelayMode.Loopback, new ObsPublishChannelOptions().RelayMode);
        Assert.Equal(PublishingRelayMode.Lan, new ObsPublishChannelOptions { Lan = new(true, Nic.Id, Nic.Address, 17144) }.RelayMode);
        Assert.Throws<ObsPublishWanException>(() => new ObsPublishChannelHost(new() { Lan = new(true, Nic.Id, Nic.Address, 17144), Wan = Options }));
    }

    [Fact]
    public async Task MissingNicOrAdmissionFailsBeforeAnyListenerStarts()
    {
        foreach (var interfaces in new[] { new Interfaces(), new Interfaces(Nic) })
        {
            await using var host = new ObsPublishChannelHost(new() { Wan = Options, Ingest = new() { Port = 0 } }, interfaces: interfaces);
            host.Start();
            var result = await host.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(ObsPublishChannelEndReason.StartupFailed, result.EndReason);
            Assert.True(result.ShutdownCompleted);
            Assert.Throws<InvalidOperationException>(() => host.IngestEndPoint);
            Assert.ThrowsAny<InvalidOperationException>(() => host.RelayEndPoint);
            Assert.False(host.PublishingStatus.WanRelay!.ListenerReady);
        }
    }

    [Fact]
    public async Task NetworkChangesInvalidateOnlyTheCapturedNicAndDisposeUnsubscribes()
    {
        var interfaces = new Interfaces(Nic);
        var changes = new Changes();
        var binding = Options.Resolve(interfaces);
        await using var monitor = new ObsPublishWanNetworkLifetime(binding, interfaces, changes);
        changes.Fire();
        Assert.False(monitor.Invalidated.IsCompleted);
        interfaces.Items = [Nic with { Address = "192.168.42.6" }];
        changes.Fire();
        await monitor.Invalidated.WaitAsync(TimeSpan.FromSeconds(5));
        await monitor.DisposeAsync();
        Assert.Equal(0, changes.Subscriptions);
        changes.Fire();
    }

    [Fact]
    public void PendingAndPingLimitsAreBoundedRecoverableAndIndependentOfRelayReservations()
    {
        using var clock = new AnnounceManualTimeProvider();
        var policy = new PcpWanIngressPolicy(new() { Clock = clock, MaxPendingHandshakes = 3, MaxPendingPerAddress = 2, MaxConcurrentPings = 2, PingResponsesPerSecond = 2 });
        var a = IPAddress.Parse("192.168.1.2"); var b = IPAddress.Parse("198.41.0.4");
        using var one = policy.TryAccept(a)!;
        using var two = policy.TryAccept(a)!;
        Assert.Null(policy.TryAccept(a));
        using var three = policy.TryAccept(b)!;
        Assert.Null(policy.TryAccept(b));
        Assert.True(one.TryBeginPing()); Assert.True(two.TryBeginPing()); Assert.False(three.TryBeginPing());
        one.Handshaken(true); two.Handshaken(true);
        Assert.Equal(1, policy.Snapshot.PendingHandshakes);
        using (var sameNat = policy.TryAccept(a)) Assert.NotNull(sameNat); // Successful sessions do not consume the pending-per-IP quota.
        one.Dispose(); two.Dispose();
        Assert.False(three.TryBeginPing()); // Rate budget survives connection cleanup.
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(three.TryBeginPing());
        policy.Close();
        Assert.Null(policy.TryAccept(b));
        three.Dispose(); three.Dispose();
        Assert.Equal(0, policy.Snapshot.PendingHandshakes);
        Assert.Equal(0, policy.Snapshot.ActivePings);
        Assert.Null(new PcpWanIngressPolicy(new()).TryAccept(null));
    }

    [Fact]
    public void WanNicMasksAreValidatedWithoutRejectingExplicitHostRoutes()
    {
        foreach (var nic in new[] { Nic with { PrefixLength = 0 }, Nic with { Mask = "255.0.255.0" }, Nic with { Address = "192.168.42.0" }, Nic with { Address = "192.168.42.255" } })
            Assert.Throws<ObsPublishWanException>(() => (Options with { Address = nic.Address }).Resolve(new Interfaces(nic)));
        var hostRoute = Nic with { Address = "198.41.0.4", PrefixLength = 32, Mask = "255.255.255.255" };
        Assert.True((Options with { Address = hostRoute.Address }).Resolve(new Interfaces(hostRoute)).StillMatches(new Interfaces(hostRoute)));
    }

    internal sealed class Interfaces(params ObsPublishLanInterface[] items) : IObsPublishLanInterfaces
    {
        internal ObsPublishLanInterface[] Items { get; set; } = items;
        public IReadOnlyList<ObsPublishLanInterface> Read() => Items;
    }
    internal sealed class Changes : IObsPublishInterfaceChanges
    {
        private Action? _changed;
        internal int Subscriptions { get; private set; }
        public IDisposable Subscribe(Action changed) { Subscriptions++; _changed += changed; return new Subscription(() => { _changed -= changed; Subscriptions--; }); }
        internal void Fire() => _changed?.Invoke();
        private sealed class Subscription(Action release) : IDisposable
        {
            private int _disposed;
            public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) release(); }
        }
    }
}
