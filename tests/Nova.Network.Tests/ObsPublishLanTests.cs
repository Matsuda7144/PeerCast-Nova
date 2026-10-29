using System.Net;
using System.Net.Sockets;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Publishing;

namespace PeerCast.Nova.Network.Tests;

public sealed class ObsPublishLanTests
{
    private static ObsPublishLanInterface Nic => new("nic-a", "LAN", true, "192.168.42.5", "255.255.255.0", 24);
    private static ObsPublishLanOptions Options => new(true, "nic-a", Nic.Address, 17144);

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("127.0.0.1")]
    [InlineData("224.1.1.1")]
    [InlineData("255.255.255.255")]
    [InlineData("169.254.1.1")]
    [InlineData("8.8.8.8")]
    [InlineData("::1")]
    [InlineData("::ffff:192.168.42.5")]
    [InlineData("host.invalid")]
    [InlineData("10.1")]
    public void RejectsNonExplicitPrivateIpv4(string address) =>
        Assert.Throws<ObsPublishLanException>(() => (Options with { Address = address }).Validate());

    [Theory]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.1.2")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.42.5")]
    public void AcceptsPrivateIpv4Configuration(string address) => (Options with { Address = address }).Validate();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void RejectsNonExplicitPort(int port) =>
        Assert.Throws<ObsPublishLanException>(() => (Options with { Port = port }).Validate());

    [Fact]
    public void OffAllowsEmptyOrStaleSelectionButRejectsCorruptValues()
    {
        new ObsPublishLanOptions().Validate();
        (Options with { Enabled = false }).Validate();
        foreach (var options in new[] {
            new ObsPublishLanOptions(InterfaceId: null!), new ObsPublishLanOptions(Address: null!),
            new ObsPublishLanOptions(Address: "host.invalid"), new ObsPublishLanOptions(Port: -1),
            new ObsPublishLanOptions(InterfaceId: "bad\nNIC"),
        })
            Assert.Throws<ObsPublishLanException>(options.Validate);
    }

    [Fact]
    public void ResolvesOnlyUniqueUpNicAndConsistentMask()
    {
        var provider = new Interfaces(Nic);
        var binding = Options.Resolve(provider);
        Assert.True(binding.Accepts(IPAddress.Parse("192.168.42.90")));
        foreach (var address in new[] { "192.168.43.5", "192.168.42.0", "192.168.42.255", "127.0.0.1", "8.8.8.8", "::ffff:192.168.42.2" })
            Assert.False(binding.Accepts(IPAddress.Parse(address)));
        Assert.False(binding.Accepts(null));
        foreach (var entries in new[]
        {
            Array.Empty<ObsPublishLanInterface>(), new[] { Nic with { IsUp = false } },
            new[] { Nic with { Id = "another-nic" } }, new[] { Nic, Nic with { Id = "duplicate" } },
            new[] { Nic with { Mask = "255.0.255.0" } }, new[] { Nic with { PrefixLength = 23 } },
            new[] { Nic with { PrefixLength = 32, Mask = "255.255.255.255" } },
        })
        {
            provider.Items = entries;
            Assert.Throws<ObsPublishLanException>(() => Options.Resolve(provider));
        }
        provider.Items = [Nic with { Address = "192.168.42.0" }];
        Assert.Throws<ObsPublishLanException>(() => (Options with { Address = "192.168.42.0" }).Resolve(provider));
    }

    [Fact]
    public async Task OffRetainsLoopbackAndNeverUsesLanDependencies()
    {
        await using var host = new ObsPublishChannelHost(new() { Ingest = new() { Port = 0 } },
            interfaces: new ThrowingInterfaces(), registerRelay: (_, _, _) => throw new InvalidOperationException());
        host.Start();
        Assert.Equal(IPAddress.Loopback, host.RelayEndPoint.Address);
        Assert.False(host.PublishingStatus.LanRelay!.Enabled);
        Assert.Equal(IPAddress.Loopback, host.IngestEndPoint.Address);
        Assert.StartsWith("http://127.0.0.1:", host.HttpStreamUrl);
    }

    [Fact]
    public async Task RevalidatesAtStartAndMissingNicAllocatesNoListenerOrRegistration()
    {
        var interfaces = new Interfaces(Nic);
        var registered = false;
        await using var host = new ObsPublishChannelHost(new() { Lan = Options, Ingest = new() { Port = 0 } },
            interfaces: interfaces, registerRelay: (_, _, _) => { registered = true; throw new InvalidOperationException(); });
        Assert.Equal(0, interfaces.ReadCount);
        interfaces.Items = [];
        host.Start();
        var result = await host.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, interfaces.ReadCount);
        Assert.False(registered);
        Assert.Equal(ObsPublishChannelEndReason.StartupFailed, result.EndReason);
        Assert.True(result.ShutdownCompleted);
        Assert.Throws<InvalidOperationException>(() => host.IngestEndPoint);
        Assert.ThrowsAny<InvalidOperationException>(() => host.RelayEndPoint);
        Assert.Equal(ObsPublishLanFailure.InterfaceUnavailable, host.PublishingStatus.LanRelay!.Failure);
    }

    [Fact]
    public async Task MissingRegistryFailsClosedBeforeBinding()
    {
        await using var host = new ObsPublishChannelHost(new() { Lan = Options }, interfaces: new Interfaces(Nic));
        host.Start();
        var result = await host.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(result.ShutdownCompleted);
        Assert.Throws<InvalidOperationException>(() => host.IngestEndPoint);
        Assert.Equal(ObsPublishLanFailure.AdmissionUnavailable, host.PublishingStatus.LanRelay!.Failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceGateClosesBeforeHandshakeAndMetadata(bool throws)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var fanout = new PcpRelayFanout();
        var called = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = new ChannelId(Guid.NewGuid());
        await using var server = new PcpDownstreamRelayServer(new(IPAddress.Loopback, 0), fanout, id, Guid.NewGuid(),
            initialMetadata: new ObsPublishChannelOptions().CreateMetadata(), acceptRemoteAddress: remote =>
            {
                Assert.Equal(IPAddress.Loopback, remote);
                called.TrySetResult();
                if (throws) throw new IOException("unavailable");
                return false;
            });
        var run = server.RunAsync(timeout.Token);
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(server.LocalEndPoint, timeout.Token);
        await called.Task.WaitAsync(timeout.Token);
        var received = await client.GetStream().ReadAsync(new byte[1], timeout.Token);
        Assert.Equal(0, received); // Not even an HTTP reply or CHAN/info escapes the gate.
        Assert.Equal(0, fanout.ActiveChildCount);
        Assert.Equal(0, server.Snapshot.AcceptedHandshakeCount);
        await server.StopAsync(timeout.Token);
        var result = await run;
        Assert.Equal(0, result.Snapshot.ActiveConnectionCount);
        Assert.Equal(0, result.Snapshot.CleanupIncompleteCount);
    }

    private sealed class Interfaces(params ObsPublishLanInterface[] items) : IObsPublishLanInterfaces
    {
        internal ObsPublishLanInterface[] Items { get; set; } = items;
        internal int ReadCount { get; private set; }
        public IReadOnlyList<ObsPublishLanInterface> Read() { ReadCount++; return Items; }
    }
    private sealed class ThrowingInterfaces : IObsPublishLanInterfaces
    {
        public IReadOnlyList<ObsPublishLanInterface> Read() => throw new IOException();
    }
}
