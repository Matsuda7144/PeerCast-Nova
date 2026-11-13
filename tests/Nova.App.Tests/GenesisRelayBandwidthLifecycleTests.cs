using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Policies;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisRelayBandwidthLifecycleTests
{
    [Fact]
    public async Task TransportAndChildCompletionBothPrecedeCountAndBandwidthRelease()
    {
        using var policy = new RelayAdmissionPolicy(bandwidth: new(new(500)));
        using var registry = new GenesisDownstreamRelayOwnerRegistry(policy);
        using var old = registry.Register(new(Guid.NewGuid()), new(Guid.NewGuid()), RelayChannelKind.Viewing,
            readBitrateKbps: () => 500);
        Assert.True(old.TryReserve(1, out var lease));
        using (lease)
        {
            var transport = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var child = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cleanup = PcpDownstreamChildSession.ReleaseAdmissionAfterCleanupAsync(lease, transport.Task, child.Task);
            old.Dispose();
            using var next = registry.Register(old.ChannelId, new(Guid.NewGuid()), RelayChannelKind.Viewing,
                readBitrateKbps: () => 500);
            Assert.False(next.TryReserve(2, out _));
            transport.SetResult();
            Assert.False(cleanup.IsCompleted);
            Assert.Equal(500, policy.Snapshot.ReservedIpv4Kbps);
            Assert.Equal(1, registry.ActiveRelayCount);
            child.SetResult();
            Assert.True(await cleanup.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(0, policy.Snapshot.ReservedIpv4Kbps);
            Assert.True(next.TryReserve(2, out var current));
            using (current)
            {
                lease.Dispose();
                old.Dispose();
                Assert.Equal(500, policy.Snapshot.ReservedIpv4Kbps);
                Assert.Equal(1, registry.ActiveRelayCount);
            }
        }
        Assert.Equal(0, policy.ReservedCount);
    }

    [Fact]
    public async Task FailedTransportCleanupKeepsBothReservationsAndDeniesReplacement()
    {
        using var policy = new RelayAdmissionPolicy(bandwidth: new(new(500)));
        using var registry = new GenesisDownstreamRelayOwnerRegistry(policy);
        using var owner = registry.Register(new(Guid.NewGuid()), new(Guid.NewGuid()), RelayChannelKind.Viewing,
            readBitrateKbps: () => 500);
        Assert.True(owner.TryReserve(1, out var lease));
        using (lease)
        {
            Assert.False(await PcpDownstreamChildSession.ReleaseAdmissionAfterCleanupAsync(lease,
                Task.FromException(new IOException("Unconfirmed transport cleanup")), Task.CompletedTask));
            owner.Dispose();
            using var next = registry.Register(owner.ChannelId, new(Guid.NewGuid()), RelayChannelKind.Viewing,
                readBitrateKbps: () => 500);
            Assert.False(next.CanReserve);
            Assert.False(next.TryReserve(2, out _));
            Assert.Equal(500, policy.Snapshot.ReservedIpv4Kbps);
            Assert.Equal(1, registry.ActiveRelayCount);
        }
    }

    [Fact]
    public async Task FiniteBandwidthCannotBeBypassedByAnUnboundRoute()
    {
        using var policy = new RelayAdmissionPolicy(bandwidth: new(new(1000)));
        using var registry = new GenesisDownstreamRelayOwnerRegistry(policy);
        await using var fanout = new PcpRelayFanout();
        var admission = registry.Resolve(new(Guid.NewGuid()), fanout);
        Assert.NotNull(admission);
        Assert.False(admission.TryReserve(1, out _));
        Assert.Equal(0, policy.Snapshot.ReservedRelays);
        Assert.Equal(0, policy.Snapshot.ReservedIpv4Kbps);
    }
}
